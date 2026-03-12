"""
GatherWin Claude Code Agent Bridge Listener
============================================
Polls the local GatherWin bridge server and processes messages using Claude Code CLI.

Usage:
    python cc_agent.py [--port 7432] [--poll-interval 2]

How it works:
    1. GatherWin hosts a local HTTP bridge on localhost:7432
    2. This script polls GET /api/poll for pending messages
    3. For each message it runs: claude --print [message]
    4. The response is POSTed back to /api/respond
    5. GatherWin displays the response in the Claude tab chat

Requirements:
    - pip install requests
    - 'claude' command must be in PATH (Claude Code CLI)

Alternatively, you can handle messages manually in this Claude Code session:
    1. Run: python cc_agent.py --check  (process one pending message and exit)
    2. Or ask Claude Code to check: it will call the /api/poll endpoint
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


def poll_bridge(bridge_url: str) -> dict | None:
    try:
        r = requests.get(f"{bridge_url}/api/poll", timeout=5)
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


def run_claude(system_prompt: str, history: str, message: str) -> str:
    """Run Claude Code CLI in print mode and return the response."""
    # Build the full prompt — include history if present
    if history:
        full_prompt = f"{history}\nUser: {message}"
    else:
        full_prompt = message

    cmd = ["claude", "--print"]
    if system_prompt:
        cmd += ["--system-prompt", system_prompt]
    cmd.append(full_prompt)

    try:
        result = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            timeout=300  # 5 minute max
        )
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
    msg_id      = msg.get("id", "")
    agent_name  = msg.get("agent_name", "Agent")
    system_prompt = msg.get("system_prompt", "")
    history     = msg.get("history", "")
    message     = msg.get("message", "")

    print(f"\n[{agent_name}] → {message[:100]}{'...' if len(message) > 100 else ''}")

    response = run_claude(system_prompt, history, message)

    print(f"[{agent_name}] ← {response[:100]}{'...' if len(response) > 100 else ''} ({len(response)} chars)")

    ok = post_response(bridge_url, msg_id, response)
    if not ok:
        print(f"[bridge] WARNING: failed to deliver response for {msg_id}")


def main():
    parser = argparse.ArgumentParser(description="GatherWin Claude Code Agent Listener")
    parser.add_argument("--port", type=int, default=7432, help="Bridge server port (default: 7432)")
    parser.add_argument("--poll-interval", type=float, default=2.0, help="Seconds between polls (default: 2)")
    parser.add_argument("--check", action="store_true", help="Process one pending message and exit")
    args = parser.parse_args()

    bridge_url = f"http://localhost:{args.port}"

    if args.check:
        # One-shot mode: check for a single message and exit
        msg = poll_bridge(bridge_url)
        if msg == "NO_CONNECTION":
            print(f"Bridge not running at {bridge_url}. Is GatherWin open?")
            sys.exit(1)
        elif msg is None:
            print("No pending messages.")
        else:
            process_one(bridge_url, msg)
        return

    # Continuous loop
    print(f"Listening for messages from {bridge_url}/api/poll ...")
    print("Press Ctrl+C to stop.\n")

    not_connected_logged = False
    while True:
        try:
            msg = poll_bridge(bridge_url)

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
