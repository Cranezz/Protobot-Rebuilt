"""Receives report files from the test laptop: PUT /<name> saves the body into ./inbox/<name> (text, max 20 MB)."""
import os, re, sys
from http.server import BaseHTTPRequestHandler, HTTPServer

INBOX = os.path.join(os.path.dirname(os.path.abspath(__file__)), "inbox")
os.makedirs(INBOX, exist_ok=True)


class Handler(BaseHTTPRequestHandler):
    def _save(self):
        name = os.path.basename(self.path.split("?")[0]) or "report.md"
        name = re.sub(r"[^A-Za-z0-9._-]", "_", name)[:80]
        length = int(self.headers.get("Content-Length", "0"))
        if length <= 0 or length > 20 * 1024 * 1024:
            self.send_response(400); self.end_headers(); self.wfile.write(b"bad size\n"); return
        data = self.rfile.read(length)
        with open(os.path.join(INBOX, name), "wb") as f:
            f.write(data)
        self.send_response(200); self.end_headers(); self.wfile.write(b"saved " + name.encode() + b"\n")

    do_PUT = _save
    do_POST = _save

    def do_GET(self):
        self.send_response(200); self.end_headers(); self.wfile.write(b"inbox ok: PUT a file here\n")


HTTPServer((sys.argv[1], int(sys.argv[2])), Handler).serve_forever()
