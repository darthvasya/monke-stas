"""Minimal client for a running SolidCraft (control port, default 7878).

    from solidcraft import SolidCraft
    sc = SolidCraft()
    sc("ui.documents")                       # raw method call
    sc.run("solid.box", {...})               # one engine command
    sc("engine.batch", {"commands": [...]})  # many engine commands
"""
import json
import os
import socket


class SolidCraft:
    def __init__(self, port=7878):
        token_path = os.path.join(os.environ["LOCALAPPDATA"], "SolidCraft", "control", f"{port}.token")
        with open(token_path) as f:
            self._token = f.read().strip()
        self._f = socket.create_connection(("127.0.0.1", port)).makefile("rwb")
        self._n = 0

    def __call__(self, method, params=None):
        self._n += 1
        req = {"id": self._n, "method": method, "params": params or {}}
        if self._n == 1:
            req["token"] = self._token
        self._f.write((json.dumps(req, ensure_ascii=False) + "\n").encode())
        self._f.flush()
        r = json.loads(self._f.readline())
        if not r["ok"]:
            raise RuntimeError(f"{method}: {r['error']}")
        return r["result"]

    def run(self, command, params=None):
        return self("engine.execute", {"command": command, "params": params or {}})


if __name__ == "__main__":
    import sys
    sc = SolidCraft()
    method = sys.argv[1] if len(sys.argv) > 1 else "ui.documents"
    params = json.loads(sys.argv[2]) if len(sys.argv) > 2 else None
    print(json.dumps(sc(method, params), ensure_ascii=False, indent=1))
