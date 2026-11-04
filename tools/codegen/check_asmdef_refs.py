#!/usr/bin/env python3
"""
Check that every assembly definition which overrides its references lists the
precompiled DLL behind each `using` it needs.

    python3 tools/codegen/check_asmdef_refs.py

UnityCheck compiles against a flat folder of every DLL, so it cannot see a DLL
missing from an asmdef's `precompiledReferences`; Unity can. This closes that gap
for the Firebase and Google namespaces (one DLL per namespace root).
"""
from __future__ import annotations

import glob
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
USING = re.compile(r"^\s*using\s+(?:static\s+)?((?:Firebase|Google)(?:\.[A-Za-z]+)?)\b", re.M)
# namespaces that live in a DLL of another name (or in an asmdef reference, not a DLL)
DLL_FOR = {"Firebase": "Firebase.App", "Firebase.Extensions": "Firebase.TaskExtension", "Firebase.Functions": None}


def main() -> int:
    problems = []
    for asmdef in glob.glob(os.path.join(ROOT, "Assets/Farmverse/**/*.asmdef"), recursive=True):
        spec = json.load(open(asmdef, encoding="utf-8"))
        if not spec.get("overrideReferences"):
            continue
        listed = {r[:-4] for r in spec.get("precompiledReferences", []) if r.endswith(".dll")}
        folder = os.path.dirname(asmdef)
        for source in glob.glob(os.path.join(folder, "**/*.cs"), recursive=True):
            for namespace in USING.findall(open(source, encoding="utf-8").read()):
                dll = DLL_FOR.get(namespace, namespace)
                if dll and dll not in listed:
                    problems.append(f"{os.path.relpath(asmdef, ROOT)}: needs {dll}.dll (using {namespace} in {os.path.relpath(source, ROOT)})")
    for p in sorted(set(problems)):
        print("  -", p)
    if problems:
        return 1
    print("asmdef references ok")
    return 0


if __name__ == "__main__":
    sys.exit(main())
