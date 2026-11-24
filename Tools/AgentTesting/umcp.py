"""Call a Unity MCP tool over the local HTTP bridge: python umcp.py <tool> <args.json file or inline json>"""
import json, sys, urllib.request, os
sys.stdout.reconfigure(errors="replace")  # (a label with a character the console code page lacks crashed the print)

URL = "http://127.0.0.1:8080/mcp"
STATE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "umcp_session.txt")


def post(payload, session=None):
    data = json.dumps(payload).encode()
    req = urllib.request.Request(URL, data=data, method="POST")
    req.add_header("Content-Type", "application/json")
    req.add_header("Accept", "application/json, text/event-stream")
    if session:
        req.add_header("mcp-session-id", session)
    with urllib.request.urlopen(req, timeout=600) as r:
        sid = r.headers.get("mcp-session-id")
        body = r.read().decode("utf-8", "replace")
    msgs = []
    for line in body.splitlines():
        if line.startswith("data:"):
            try:
                msgs.append(json.loads(line[5:].strip()))
            except Exception:
                pass
    if not msgs and body.strip().startswith("{"):
        msgs.append(json.loads(body))
    return sid, msgs


def session():
    if os.path.exists(STATE):
        return open(STATE).read().strip()
    sid, _ = post({"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {
        "protocolVersion": "2025-03-26", "capabilities": {}, "clientInfo": {"name": "claude-helper", "version": "1"}}})
    post({"jsonrpc": "2.0", "method": "notifications/initialized"}, sid)
    open(STATE, "w").write(sid)
    return sid


def call(tool, args):
    for attempt in range(2):
        sid = session()
        try:
            _, msgs = post({"jsonrpc": "2.0", "id": 2, "method": "tools/call", "params": {"name": tool, "arguments": args}}, sid)
            return msgs
        except urllib.error.HTTPError as e:
            if os.path.exists(STATE):
                os.remove(STATE)
            if attempt == 1:
                raise


if __name__ == "__main__":
    tool = sys.argv[1]
    raw = sys.argv[2]
    if tool == "cs":
        tool = "execute_code"
        args = {"action": "execute", "code": open(raw, encoding="utf-8").read()}
    else:
        args = json.load(open(raw, encoding="utf-8")) if os.path.exists(raw) else json.loads(raw)
    # Only ever this project's editor (another Unity project may be open on this computer).
    args.setdefault("unity_instance", "ProtobotRebuiltV2@689891f909fd4217")
    for m in call(tool, args):
        res = m.get("result") or m.get("error")
        if isinstance(res, dict) and "content" in res:
            for c in res["content"]:
                if c.get("type") == "text":
                    print(c["text"])
        else:
            print(json.dumps(m)[:4000])
