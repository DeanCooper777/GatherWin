"""
GatherWin Claude Code Agent Bridge Listener
============================================
Polls the local GatherWin bridge server and processes messages using Claude Code CLI.

Usage:
    python cc_agent.py [--port 7432] [--agent-id ID] [--poll-interval 2]
    python cc_agent.py --check   # process one message and exit

When --agent-id is given, only messages for that specific agent are processed
(polls /api/poll/{agentId}). This allows one terminal window per agent.

Without --agent-id, any pending message is processed (legacy mode).

Requirements:
    pip install requests
    'claude' command in PATH (Claude Code CLI)
"""
import argparse
import json
import subprocess
import sys
import time

try:
    import requests
except ImportError:
    print("Missing dependency: pip install requests", file=sys.stderr)
    sys.exit(1)


def poll_bridge(bridge_url: str, agent_id: str | None) -> dict | None | str:
    """Returns a message dict, None (nothing pending), or 'NO_CONNECTION'."""
    endpoint = f"{bridge_url}/api/poll/{agent_id}" if agent_id else f"{bridge_url}/api/poll"
    try:
        r = requests.get(endpoint, timeout=5)
        if r.status_code == 200:
            return r.json()
        elif r.status_code == 204:
            return None
        else:
            print(f"[bridge] Unexpected status: {r.status_code} from {endpoint}")
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


def run_claude(system_prompt: str, history: str, message: str) -> str:
    """Run Claude Code CLI in print mode and return the response."""
    # Build prompt — include history context if present
    if history:
        full_prompt = f"{history}\nUser: {message}"
    else:
        full_prompt = message

    cmd = ["claude", "--print"]
    if system_prompt:
        cmd += ["--system-prompt", system_prompt]
    cmd.append(full_prompt)

    try:
        result = subprocess.run(cmd, capture_output=True, text=True, timeout=300)
        if result.returncode == 0:
            return result.stdout.strip()
        else:
            err = result.stderr.strip() or f"Exit code {result.returncode}"
            return f"[Claude Code error] {err}"
    except subprocess.TimeoutExpired:
        return "[Claude Code error] Timed out after 5 minutes"
    except FileNotFoundError:
        return (
            "[Claude Code error] 'claude' command not found. "
            "Is Claude Code installed and in your PATH?"
        )
    except Exception as e:
        return f"[Claude Code error] {e}"


def process_one(bridge_url: str, msg: dict) -> None:
    msg_id        = msg.get("id", "")
    agent_name    = msg.get("agent_name", "Agent")
    system_prompt = msg.get("system_prompt", "")
    history       = msg.get("history", "")
    message       = msg.get("message", "")

    preview = message[:100] + ("..." if len(message) > 100 else "")
    print(f"\n[{agent_name}] → {preview}")

    response = run_claude(system_prompt, history, message)

    preview_out = response[:100] + ("..." if len(response) > 100 else "")
    print(f"[{agent_name}] ← {preview_out} ({len(response)} chars)")

    if not post_response(bridge_url, msg_id, response):
        print(f"[bridge] WARNING: failed to deliver response for {msg_id}")


def main():
    parser = argparse.ArgumentParser(description="GatherWin Claude Code Agent Listener")
    parser.add_argument("--port",          type=int,   default=7432, help="Bridge port (default: 7432)")
    parser.add_argument("--agent-id",      type=str,   default=None, help="Only process messages for this agent ID")
    parser.add_argument("--poll-interval", type=float, default=2.0,  help="Seconds between polls (default: 2)")
    parser.add_argument("--check",         action="store_true",      help="Process one message and exit")
    args = parser.parse_args()

    bridge_url = f"http://localhost:{args.port}"
    agent_id   = args.agent_id

    if agent_id:
        print(f"Agent ID : {agent_id}")
    print(f"Bridge   : {bridge_url}/api/poll{('/' + agent_id) if agent_id else ''}")

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

    print("Waiting for messages... (Ctrl+C to stop)\n")

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
