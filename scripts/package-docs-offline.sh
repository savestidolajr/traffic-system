#!/usr/bin/env bash
set -euo pipefail

# ── Package the handover documentation site for offline reading ──
# Usage: ./scripts/package-docs-offline.sh
#
# `docs/operations/access-and-accounts.md` records that the Arinco identities
# holding this engagement's access lapse when it ends. A site ARN can only
# reach through an Azure Static Web App is therefore not sufficient on its own:
# this produces a self-contained copy ARN keeps regardless of what happens to
# the hosting.
#
# Output: docs/deliverables/Handover-Pack/arn-documentation-offline.zip

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SITE_DIR="$REPO_ROOT/docs-site"
OUT_DIR="$REPO_ROOT/docs/deliverables/Handover-Pack"
OUT_ZIP="$OUT_DIR/arn-documentation-offline.zip"

command -v zip >/dev/null 2>&1 || { echo "error: zip is not installed" >&2; exit 1; }

echo "── Building the documentation site ──"
( cd "$SITE_DIR" && npm run build )

[ -f "$SITE_DIR/dist/index.html" ] || {
    echo "error: $SITE_DIR/dist/index.html missing after build" >&2
    exit 1
}

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

cp -R "$SITE_DIR/dist/." "$STAGE/"

# Pagefind fetches its search index over HTTP, so a file:// open reads fine but
# cannot search. Both launchers serve the bundle from its own directory through
# the same Python helper, so the behaviour and the printed address are
# identical on either platform.
#
# The port is chosen at run time rather than hardcoded: 8080 is a popular
# default and a reader whose machine already has something on it would
# otherwise get an unexplained failure, or worse, a different application's
# page. `python3 -m http.server 8080` was exactly that trap.
cat > "$STAGE/serve.py" <<'EOF'
"""Serve this folder over HTTP so the documentation's search works."""
import functools
import http.server
import os
import socketserver
import webbrowser

HERE = os.path.dirname(os.path.abspath(__file__))


class Handler(http.server.SimpleHTTPRequestHandler):
    """Static handler that answers an unknown path with the site's 404 page.

    Without this a stale link inside the bundle lands on Python's raw error
    page, which tells the reader nothing and looks broken.
    """

    def send_error(self, code, message=None, explain=None):
        page = os.path.join(HERE, "404.html")
        if code != 404 or not os.path.isfile(page):
            return super().send_error(code, message, explain)
        body = open(page, "rb").read()
        self.send_response(404, message)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(body)


handler = functools.partial(Handler, directory=HERE)

for port in range(8080, 8100):
    try:
        httpd = socketserver.TCPServer(("127.0.0.1", port), handler)
    except OSError:
        continue
    url = f"http://localhost:{port}/"
    print(f"\nARN handover documentation is being served at {url}")
    print("Leave this window open while you read. Press Ctrl+C to stop.\n")
    try:
        webbrowser.open(url)
    except Exception:
        pass
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        print("\nStopped.")
    break
else:
    raise SystemExit("No free port between 8080 and 8099. Close something and retry.")
EOF

cat > "$STAGE/serve.sh" <<'EOF'
#!/usr/bin/env bash
exec python3 "$(dirname "$0")/serve.py"
EOF
chmod +x "$STAGE/serve.sh"

cat > "$STAGE/serve.cmd" <<'EOF'
@echo off
py "%~dp0serve.py"
EOF

cat > "$STAGE/README.txt" <<'EOF'
ARN Information Bulletin Platform - handover documentation (offline copy)
=========================================================================

This is a complete, self-contained copy of the handover documentation site.
It needs no network connection and no Azure access.

Reading it
----------
Open index.html in any browser. Every page, diagram and screenshot works.

Searching it
------------
Full-text search needs the pages to be served over HTTP, because the search
index is fetched as a file rather than embedded in the page. Opening
index.html directly will show an empty search box that returns nothing. That
is expected, not a fault.

To enable search, start the bundled server. It prints the address to open and
tries to open your browser for you:

  macOS or Linux:   ./serve.sh
  Windows:          serve.cmd

Both run a small local web server from this folder using Python 3, which is
pre-installed on macOS and most Linux systems and available from python.org or
the Microsoft Store on Windows. It picks the first free port from 8080 upwards
and prints the address, so it will not collide with anything already running.
Nothing leaves the machine. Stop the server with Ctrl+C.

Currency
--------
This bundle is a snapshot. The currency statement in the banner and footer of
every page states what it is current as at.

The same content is published, and kept current, at the hosted documentation
site named in the signed handover pack. Where the two disagree, the hosted
site is the later of the two.
EOF

rm -f "$OUT_ZIP"
( cd "$STAGE" && zip -qr "$OUT_ZIP" . )

echo ""
echo "wrote ${OUT_ZIP#"$REPO_ROOT/"}  ($(du -h "$OUT_ZIP" | cut -f1), $(unzip -l "$OUT_ZIP" | tail -1 | awk '{print $2}') files)"
