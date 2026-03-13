"""
GatherWin Claude Code Agent Bridge Listener
============================================
Polls the local GatherWin bridge server and processes messages using Claude Code CLI.

Usage:
    python cc_agent.py [--port 7432] [--agent-id ID] [--poll-interval 2]
    python cc_agent.py --check              # process one message and exit
    python cc_agent.py --reset-session      # clear session memory for this agent

How it works:
    1. GatherWin hosts a local HTTP bridge on localhost:7432
    2. This script polls GET /api/poll/{agentId} for pending messages
    3. For each message it runs: claude --print [--continue] [message]
    4. While running, a background thread sends heartbeats every 30s so
       GatherWin shows progress instead of just "Waiting..."
    5. The response is POSTed back to /api/respond
    6. GatherWin displays the response in the Claude tab chat

Session memory:
    Each agent has its own working directory under %APPDATA%\\GatherWin\\agents\\{id}\\
    Claude Code stores its conversation history per working directory.
    After the first successful run, subsequent runs use --continue to pick up
    where the last session left off (file changes, context, etc.).
    Use --reset-session to start fresh.

Requirements:
    pip install requests
    'claude' command in PATH (Claude Code CLI)
"""
import argparse
import os
import subprocess
import sys
import threading
import time

try:
    import requests
except ImportError:
    print("Missing dependency: pip install requests", file=sys.stderr)
    sys.exit(1)

HEARTBEAT_INTERVAL = 30  # seconds between "still thinking" updates


# ── Session management ─────────────────────────────────────────────────────

def get_agent_dir(agent_id: str) -> str:
    """Return (and create) a per-agent working directory."""
    base = os.environ.get('APPDATA') or os.path.expanduser('~')
    path = os.path.join(base, 'GatherWin', 'agents', agent_id)
    os.makedirs(path, exist_ok=True)
    return path


def session_started_path(agent_dir: str) -> str:
    return os.path.join(agent_dir, '.session_started')


def has_session(agent_dir: str) -> bool:
    return os.path.exists(session_started_path(agent_dir))


def mark_session_started(agent_dir: str) -> None:
    with open(session_started_path(agent_dir), 'w') as f:
        f.write(str(time.time()))


def reset_session(agent_dir: str) -> None:
    p = session_started_path(agent_dir)
    if os.path.exists(p):
        os.remove(p)
        print(f"Session reset for {agent_dir}")
    else:
        print("No session to reset.")


# ── Bridge communication ───────────────────────────────────────────────────

def poll_bridge(bridge_url: str, agent_id: str | None) -> dict | None | str:
    endpoint = f"{bridge_url}/api/poll/{agent_id}" if agent_id else f"{bridge_url}/api/poll"
    try:
        r = requests.get(endpoint, timeout=5)
        if r.status_code == 200:
            return r.json()
        elif r.status_code == 204:
            return None
        else:
            print(f"[bridge] Unexpected status: {r.status_code}")
            return None
    except requests.ConnectionError:
        return "NO_CONNECTION"
    except Exception as e:
        print(f"[bridge] Poll error: {e}", file=sys.stderr)
        return None


def post_response(bridge_url: str, msg_id: str, content: str) -> bool:
    try:
        r = requests.post(
            f"{bridge_url}/api/respond",
            json={"id": msg_id, "content": content},
            timeout=10
        )
        return r.ok
    except Exception as e:
        print(f"[bridge] Failed to post response: {e}", file=sys.stderr)
        return False


def post_heartbeat(bridge_url: str, msg_id: str, status: str) -> None:
    try:
        requests.post(
            f"{bridge_url}/api/heartbeat",
            json={"id": msg_id, "status": status},
            timeout=5
        )
    except Exception:
        pass  # heartbeats are best-effort


# ── Heartbeat thread ───────────────────────────────────────────────────────

def heartbeat_worker(bridge_url: str, msg_id: str, agent_name: str, stop_event: threading.Event) -> None:
    """Sends a heartbeat to GatherWin every HEARTBEAT_INTERVAL seconds."""
    start = time.time()
    while not stop_event.wait(HEARTBEAT_INTERVAL):
        elapsed = int(time.time() - start)
        minutes, seconds = divmod(elapsed, 60)
        if minutes > 0:
            elapsed_str = f"{minutes}m {seconds}s"
        else:
            elapsed_str = f"{seconds}s"
        status = f"[{agent_name}] Still working... ({elapsed_str} elapsed)"
        post_heartbeat(bridge_url, msg_id, status)
        print(f"[heartbeat] {status}")


# ── Claude Code invocation ─────────────────────────────────────────────────

def run_claude(agent_id: str | None, system_prompt: str, history: str, message: str) -> str:
    """
    Run Claude Code CLI in print mode.
    If an agent_id is provided, runs from the per-agent working directory
    and uses --continue after the first successful run for session persistence.
    """
    # Build the full prompt including conversation history for context
    if history:
        full_prompt = f"{history}\nUser: {message}"
    else:
        full_prompt = message

    agent_dir = get_agent_dir(agent_id) if agent_id else None
    use_continue = agent_dir is not None and has_session(agent_dir)

    cmd = ["claude", "--print"]
    if use_continue:
        cmd += ["--continue"]
    if system_prompt and not use_continue:
        # Only pass system prompt on fresh sessions; --continue preserves it
        cmd += ["--system-prompt", system_prompt]
    cmd.append(full_prompt)

    if use_continue:
        print(f"  (continuing previous session)")
    else:
        print(f"  (starting fresh session in {agent_dir or 'cwd'})")

    try:
        result = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            timeout=1200,  # 20 minute max (matches GatherWin bridge timeout)
            cwd=agent_dir   # per-agent working directory for session isolation
        )
        if result.returncode == 0:
            response = result.stdout.strip()
            # Mark session as started after first successful run
            if agent_dir and not use_continue:
                mark_session_started(agent_dir)
            return response
        else:
            err = result.stderr.strip() or f"Exit code {result.returncode}"
            return f"[Claude Code error] {err}"
    except subprocess.TimeoutExpired:
        return "[Claude Code error] Timed out after 20 minutes"
    except FileNotFoundError:
        return (
            "[Claude Code error] 'claude' command not found. "
            "Is Claude Code installed and in your PATH?"
        )
    except Exception as e:
        return f"[Claude Code error] {e}"


# ── Message processing ─────────────────────────────────────────────────────

def process_one(bridge_url: str, msg: dict) -> None:
    msg_id        = msg.get("id", "")
    agent_id      = msg.get("agent_id")
    agent_name    = msg.get("agent_name", "Agent")
    system_prompt = msg.get("system_prompt", "")
    history       = msg.get("history", "")
    message       = msg.get("message", "")

    preview = message[:100] + ("..." if len(message) > 100 else "")
    print(f"\n[{agent_name}] → {preview}")

    # Start heartbeat thread
    stop_event = threading.Event()
    hb_thread = threading.Thread(
        target=heartbeat_worker,
        args=(bridge_url, msg_id, agent_name, stop_event),
        daemon=True
    )
    hb_thread.start()

    try:
        response = run_claude(agent_id, system_prompt, history, message)
    finally:
        stop_event.set()  # stop heartbeat regardless of success/failure

    preview_out = response[:100] + ("..." if len(response) > 100 else "")
    print(f"[{agent_name}] ← {preview_out} ({len(response)} chars)")

    if not post_response(bridge_url, msg_id, response):
        print(f"[bridge] WARNING: failed to deliver response for {msg_id}")


# ── Entry point ────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description="GatherWin Claude Code Agent Listener")
    parser.add_argument("--port",          type=int,   default=7432,  help="Bridge port (default: 7432)")
    parser.add_argument("--agent-id",      type=str,   default=None,  help="Agent ID (only process messages for this agent)")
    parser.add_argument("--poll-interval", type=float, default=2.0,   help="Seconds between polls (default: 2)")
    parser.add_argument("--check",         action="store_true",       help="Process one message and exit")
    parser.add_argument("--reset-session", action="store_true",       help="Clear session memory for this agent and exit")
    args = parser.parse_args()

    bridge_url = f"http://localhost:{args.port}"
    agent_id   = args.agent_id

    if args.reset_session:
        if not agent_id:
            print("--reset-session requires --agent-id", file=sys.stderr)
            sys.exit(1)
        reset_session(get_agent_dir(agent_id))
        return

    if agent_id:
        agent_dir = get_agent_dir(agent_id)
        has_s = has_session(agent_dir)
        print(f"Agent ID  : {agent_id}")
        print(f"Agent dir : {agent_dir}")
        print(f"Session   : {'continuing previous' if has_s else 'fresh start'}")
    print(f"Bridge    : {bridge_url}/api/poll{('/' + agent_id) if agent_id else ''}")
    print(f"Heartbeat : every {HEARTBEAT_INTERVAL}s")

    if args.check:
        msg = poll_bridge(bridge_url, agent_id)
        if msg == "NO_CONNECTION":
            print(f"Bridge not running at {bridge_url}. Is GatherWin open?")
            sys.exit(1)
        elif msg is None:
            print("No pending messages.")
        else:
            process_one(bridge_url, msg)
        return

    print("\nWaiting for messages... (Ctrl+C to stop)\n")

    not_connected_logged = False
    while True:
        try:
            msg = poll_bridge(bridge_url, agent_id)

            if msg == "NO_CONNECTION":
                if not not_connected_logged:
                    print(f"[bridge] Not reachable at {bridge_url}. Is GatherWin running?")
                    not_connected_logged = True
                time.sleep(10)
                continue

            not_connected_logged = False

            if msg is not None:
                process_one(bridge_url, msg)
            else:
                time.sleep(args.poll_interval)

        except KeyboardInterrupt:
            print("\nStopped.")
            break
        except Exception as e:
            print(f"[loop error] {e}", file=sys.stderr)
            time.sleep(5)


if __name__ == "__main__":
    main()
