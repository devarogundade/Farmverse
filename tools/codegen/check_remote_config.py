#!/usr/bin/env python3
"""
Check that backend/remoteconfig.template.json and the client's RemoteKeys.cs
name the same keys with the same defaults (11 §3).

    python3 tools/codegen/check_remote_config.py
"""
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
KEYS = os.path.join(ROOT, "Assets/Farmverse/Runtime/Application/Config/RemoteKeys.cs")
TEMPLATE = os.path.join(ROOT, "backend/remoteconfig.template.json")


def main() -> int:
    source = open(KEYS, encoding="utf-8").read()
    names = dict(re.findall(r'const string (\w+) = "([a-z_]+)";', source))
    defaults = {m[0]: m[1] for m in re.findall(r"const \w+ (\w+)Default = (.+?);", source)}
    template = json.load(open(TEMPLATE, encoding="utf-8"))["parameters"]
    problems = []
    for const, key in names.items():
        if key not in template:
            problems.append(f"{key} ({const}) missing from the template")
            continue
        want = defaults.get(const, "").strip('"')
        got = template[key]["defaultValue"]["value"]
        if want.lower() != got.lower():
            problems.append(f"{key}: client default {want!r}, template {got!r}")
    for key in template:
        if key not in names.values():
            problems.append(f"{key} is in the template but the client never reads it")
    if problems:
        print("remote config drift:\n  - " + "\n  - ".join(problems))
        return 1
    print(f"remote config ok: {len(names)} keys")
    return 0


if __name__ == "__main__":
    sys.exit(main())
