#!/usr/bin/env python3
"""
Fail if a committed file looks like it carries a secret (08 §4).

    python3 tools/codegen/check_secrets.py            # files tracked by git (CI)
    python3 tools/codegen/check_secrets.py --all      # every file under the repo

Firebase client configs (google-services.json, GoogleService-Info.plist) are
public by design and allowed; their API key is restricted in the console.
"""
import os
import re
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

PATTERNS = {
    "private key": re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----"),
    "service account key": re.compile(r'"type"\s*:\s*"service_account"'),
    "OpenAI key": re.compile(r"\bsk-(?:proj-)?[A-Za-z0-9_-]{20,}"),
    "AWS access key": re.compile(r"\bAKIA[0-9A-Z]{16}\b"),
    "GitHub token": re.compile(r"\bgh[pousr]_[A-Za-z0-9]{36,}\b"),
    "Slack token": re.compile(r"\bxox[abprs]-[A-Za-z0-9-]{10,}"),
    "Stripe secret": re.compile(r"\bsk_live_[A-Za-z0-9]{20,}"),
}
FORBIDDEN_NAMES = re.compile(r"(^|/)(\.env(\..*)?|.*service-?account.*\.json|.*-firebase-adminsdk-.*\.json|.*\.(keystore|jks|p12|pfx|p8|mobileprovision))$", re.I)
ALLOWED_NAMES = re.compile(r"(^|/)(\.env\.example|google-services\.json|GoogleService-Info\.plist)$")
SKIP_DIRS = {".git", "node_modules", "Library", "Temp", "Logs", "UserSettings", "obj", "bin", "lib"}
BINARY = re.compile(r"\.(png|jpg|jpeg|psd|ttf|otf|dll|so|a|aar|jar|fbx|blend|wav|mp3|ogg|zip|unitypackage|asset|exr|tga)$", re.I)


def tracked_files(all_files: bool):
    if not all_files:
        try:
            out = subprocess.run(["git", "ls-files"], cwd=ROOT, capture_output=True, text=True, check=True).stdout
            return [f for f in out.splitlines() if f]
        except (OSError, subprocess.CalledProcessError):
            pass
    files = []
    for base, dirs, names in os.walk(ROOT):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for n in names:
            files.append(os.path.relpath(os.path.join(base, n), ROOT).replace(os.sep, "/"))
    return files


def main() -> int:
    problems = []
    for rel in tracked_files("--all" in sys.argv):
        if FORBIDDEN_NAMES.search(rel) and not ALLOWED_NAMES.search(rel):
            problems.append(f"{rel}: secret file type must not be committed")
            continue
        if BINARY.search(rel):
            continue
        path = os.path.join(ROOT, rel)
        try:
            if os.path.getsize(path) > 2_000_000:
                continue
            text = open(path, encoding="utf-8", errors="ignore").read()
        except OSError:
            continue
        for name, pattern in PATTERNS.items():
            if pattern.search(text):
                problems.append(f"{rel}: looks like a {name}")
    if problems:
        print("possible secrets:\n  - " + "\n  - ".join(problems))
        return 1
    print("no secrets found")
    return 0


if __name__ == "__main__":
    sys.exit(main())
