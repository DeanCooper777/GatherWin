"""
Gather.is API agent helper - authenticates and provides API access.
Used by Claude Code to interact with the platform as an AI agent.
"""
import base64
import json
import os
import sys
import time
import requests
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey
from cryptography.hazmat.primitives.serialization import (
    Encoding, PublicFormat, PrivateFormat, NoEncryption
)

BASE_URL = "https://gather.is"
KEYS_DIR = os.path.expanduser("~/.gather")
AUTH_FILE = os.path.join(KEYS_DIR, "auth.json")
PRIVATE_KEY_FILE = os.path.join(KEYS_DIR, "private.key")
PUBLIC_KEY_FILE = os.path.join(KEYS_DIR, "public.pem")

def load_keys():
    with open(PRIVATE_KEY_FILE, "rb") as f:
        raw = f.read()
    private_key = Ed25519PrivateKey.from_private_bytes(raw[:32])
    with open(PUBLIC_KEY_FILE, "r") as f:
        public_pem = f.read().strip()
    return private_key, public_pem

def authenticate():
    private_key, public_pem = load_keys()

    # Request challenge
    resp = requests.post(f"{BASE_URL}/api/agents/challenge", json={"public_key": public_pem})
    resp.raise_for_status()
    challenge = resp.json()
    nonce_b64 = challenge["nonce"]
    nonce_bytes = base64.b64decode(nonce_b64)

    # Sign nonce
    signature = private_key.sign(nonce_bytes)
    sig_b64 = base64.b64encode(signature).decode()

    # Authenticate
    resp = requests.post(f"{BASE_URL}/api/agents/authenticate", json={
        "public_key": public_pem,
        "signature": sig_b64
    })
    resp.raise_for_status()
    auth = resp.json()

    # Save token
    with open(AUTH_FILE, "w") as f:
        json.dump(auth, f, indent=2)

    return auth["token"]

def get_token():
    """Get a valid token, refreshing if needed."""
    if os.path.exists(AUTH_FILE):
        with open(AUTH_FILE) as f:
            auth = json.load(f)
        token = auth.get("token", "")
        # Check expiry from JWT
        try:
            payload = token.split(".")[1]
            payload += "=" * (4 - len(payload) % 4)
            claims = json.loads(base64.b64decode(payload))
            if claims.get("exp", 0) > time.time() + 120:
                return token
        except Exception:
            pass
    return authenticate()

def api_get(path, params=None):
    token = get_token()
    resp = requests.get(f"{BASE_URL}{path}",
                       headers={"Authorization": f"Bearer {token}"},
                       params=params)
    resp.raise_for_status()
    return resp.json()

def api_post(path, data):
    token = get_token()
    resp = requests.post(f"{BASE_URL}{path}",
                        headers={"Authorization": f"Bearer {token}"},
                        json=data)
    if not resp.ok:
        print(f"ERROR {resp.status_code}: {resp.text}", file=sys.stderr)
    resp.raise_for_status()
    return resp.json()

def api_put(path, data=None):
    token = get_token()
    resp = requests.put(f"{BASE_URL}{path}",
                       headers={"Authorization": f"Bearer {token}"},
                       json=data or {})
    if not resp.ok:
        print(f"ERROR {resp.status_code}: {resp.text}", file=sys.stderr)
    resp.raise_for_status()
    return resp.json()

if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "auth"

    if cmd == "auth":
        token = get_token()
        print(json.dumps({"token": token[:20] + "...", "status": "ok"}))

    elif cmd == "create_channel":
        name = sys.argv[2]
        desc = sys.argv[3] if len(sys.argv) > 3 else ""
        result = api_post("/api/channels", {"name": name, "description": desc})
        print(json.dumps(result, indent=2))

    elif cmd == "send_message":
        channel_id = sys.argv[2]
        body = sys.argv[3]
        result = api_post(f"/api/channels/{channel_id}/messages", {"body": body})
        print(json.dumps(result, indent=2))

    elif cmd == "get_channels":
        result = api_get("/api/channels")
        print(json.dumps(result, indent=2))

    elif cmd == "get_messages":
        channel_id = sys.argv[2]
        since = sys.argv[3] if len(sys.argv) > 3 else None
        params = {"limit": 50}
        if since:
            params["since"] = since
        result = api_get(f"/api/channels/{channel_id}/messages", params)
        print(json.dumps(result, indent=2))

    elif cmd == "get_balance":
        result = api_get("/api/balance")
        print(json.dumps(result, indent=2))

    elif cmd == "me":
        result = api_get("/api/agents/me")
        print(json.dumps(result, indent=2))

    else:
        print(f"Unknown command: {cmd}", file=sys.stderr)
        sys.exit(1)
