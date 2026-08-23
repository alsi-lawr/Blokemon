from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from sys import argv
from urllib.parse import urlparse


class StaticApplicationHandler(SimpleHTTPRequestHandler):
    def log_message(self, format, *args):
        pass

    def do_GET(self):
        request_path = urlparse(self.path).path
        if request_path.startswith("/api/"):
            self.send_error(404)
            return

        requested_file = Path(self.directory, request_path.lstrip("/"))
        if request_path != "/" and not requested_file.is_file():
            self.path = "/index.html"
        super().do_GET()


def static_server(root, port=0):
    root = str(Path(root).resolve())
    handler = lambda *args, **kwargs: StaticApplicationHandler(*args, directory=root, **kwargs)
    return ThreadingHTTPServer(("127.0.0.1", port), handler)


if __name__ == "__main__":
    if len(argv) != 3:
        raise SystemExit("usage: static_host.py <wwwroot> <port>")

    try:
        static_server(argv[1], int(argv[2])).serve_forever()
    except KeyboardInterrupt:
        pass
