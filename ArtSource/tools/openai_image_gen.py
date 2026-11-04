"""Farmverse AI asset generator (OpenAI Images API), manifest driven.

Runs on your PC inside Blender's Python (or any Python 3). Reads OPEN_AI from <root>/.env;
the key is never printed or written anywhere.

    exec(open(r"C:\\Users\\devar\\Documents\\Farmverse\\ArtSource\\tools\\openai_image_gen.py").read())
    start_batch()                    # everything missing, 4 workers, in the background
    start_batch(packs=["P02"])       # one pack
    start_batch(ids=["crop_onion"])  # one asset (delete the file first to redo it)
    status()                         # progress
"""
import base64, json, os, threading, time, uuid, urllib.request, urllib.error, queue

ROOT = r"C:\Users\devar\Documents\Farmverse"
ART = os.path.join(ROOT, "Assets", "Farmverse", "Content", "UI", "Art", "Painted")
TOOLS = os.path.join(ROOT, "ArtSource", "tools")
MANIFEST = os.path.join(TOOLS, "asset_manifest.json")
STATUS = os.path.join(TOOLS, "gen_status.json")
REFS = [os.path.join(ART, "Icons", "_AI", n) for n in ("ico_coin.png", "ico_tomato.png", "ico_chest.png", "ico_gift.png")]

ICON = ("A single 3D mobile game icon of {s}. Polished casual-game style: chunky rounded proportions, glossy "
        "candy-like materials, saturated warm colours, soft warm key light from the upper left, bright rim light, "
        "subtle ambient occlusion, a thin dark outline tinted to the object's own colour. Three-quarter view, centred, "
        "filling about 80% of the frame, isolated on a transparent background, no text, no currency symbols, no ground shadow, no border. "
        "Original design, no existing game characters or brands.")
ILLUS = ("A 3D casual mobile game illustration of {s}, same rendering style as a polished match-3 game: chunky rounded "
         "shapes, glossy materials, saturated warm colours, warm key light upper left, bright rim light, thin dark tinted "
         "outline, centred, isolated on a transparent background, no text unless it is part of the subject. Original design.")
BG = ("Portrait 9:16 mobile game background for a cozy farming game: {s}. Stylized 3D, warm afternoon sun from the "
      "upper left, saturated but soft colours, painterly depth, clean simple shapes, no text, no UI, no characters in the "
      "centre. Keep the middle third calm so UI panels can sit on top.")
HDR = ("Landscape banner art for the top of a cozy farming mobile game screen: {s}. Stylized 3D, warm light, saturated "
       "soft colours, focal interest in the lower half, top quarter calm for a screen title, no text, no UI.")
CARD = ("Landscape illustration for an in-game dialog art window in a cozy farming mobile game: {s}. Stylized 3D casual "
        "game art, warm light from the upper left, saturated soft colours, one clear focal point in the centre, simple "
        "background, no text, no UI, no currency symbols, keep a 6% margin on every edge for rounded corners.")
STYLE_PREFIX = ("Use the attached images only as a style guide for rendering, lighting, outline and materials. Do not "
                "copy their subjects. Create: ")
KINDS = {"icon": (ICON, "1024x1024", True), "illus": (ILLUS, "1024x1024", True),
         "bg": (BG, "1024x1536", False), "header": (HDR, "1536x1024", False), "card": (CARD, "1536x1024", False)}

_lock = threading.Lock()


def _key():
    for line in open(os.path.join(ROOT, ".env"), encoding="utf-8", errors="ignore"):
        if "=" in line:
            k, v = line.split("=", 1)
            if k.strip() in ("OPEN_AI", "OPENAI_API_KEY", "OPEN_AI_KEY"):
                return v.strip().strip('"').strip("'")
    raise RuntimeError("OPEN_AI not found in .env")


def _post_json(url, body):
    req = urllib.request.Request(url, data=json.dumps(body).encode(),
                                 headers={"Authorization": "Bearer " + _key(), "Content-Type": "application/json"})
    return json.loads(urllib.request.urlopen(req, timeout=240).read())


def _post_multipart(url, fields, files):
    b = "----fv" + uuid.uuid4().hex; parts = []
    for k, v in fields.items():
        parts.append(("--%s\r\nContent-Disposition: form-data; name=\"%s\"\r\n\r\n%s\r\n" % (b, k, v)).encode())
    for k, path in files:
        parts.append(("--%s\r\nContent-Disposition: form-data; name=\"%s\"; filename=\"%s\"\r\nContent-Type: image/png\r\n\r\n"
                      % (b, k, os.path.basename(path))).encode() + open(path, "rb").read() + b"\r\n")
    parts.append(("--%s--\r\n" % b).encode())
    req = urllib.request.Request(url, data=b"".join(parts), headers={
        "Authorization": "Bearer " + _key(), "Content-Type": "multipart/form-data; boundary=" + b})
    return json.loads(urllib.request.urlopen(req, timeout=240).read())


def _err(e):
    if isinstance(e, urllib.error.HTTPError):
        return "HTTP %s %s" % (e.code, e.read().decode(errors="ignore")[:300])
    return str(e)[:300]


def generate_asset(a):
    tpl, size, transparent = KINDS[a["kind"]]
    prompt = tpl.format(s=a["subject"])
    out = os.path.join(ART, a["folder"].replace("/", os.sep), a["id"] + ".png")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    attempts, errors = [], []
    if transparent:
        refs = [r for r in REFS if os.path.exists(r)]
        if refs:
            attempts.append(("edit:gpt-image-1", lambda: _post_multipart("https://api.openai.com/v1/images/edits",
                {"model": "gpt-image-1", "prompt": STYLE_PREFIX + prompt, "size": size, "quality": "high",
                 "background": "transparent", "output_format": "png"}, [("image[]", r) for r in refs])))
        attempts.append(("gen:gpt-image-1", lambda: _post_json("https://api.openai.com/v1/images/generations",
            {"model": "gpt-image-1", "prompt": prompt, "size": size, "quality": "high", "background": "transparent",
             "output_format": "png"})))
    else:
        for m in ("gpt-image-2", "gpt-image-1"):
            attempts.append(("gen:" + m, lambda m=m: _post_json("https://api.openai.com/v1/images/generations",
                {"model": m, "prompt": prompt, "size": size, "quality": "high"})))
    for name, call in attempts:
        try:
            data = call()
            with open(out, "wb") as f:
                f.write(base64.b64decode(data["data"][0]["b64_json"]))
            return {"ok": True, "via": name, "path": out}
        except Exception as e:
            errors.append("%s: %s" % (name, _err(e)))
    return {"ok": False, "errors": errors}


def _out_path(a):
    return os.path.join(ART, a["folder"].replace("/", os.sep), a["id"] + ".png")


def _write_status(st):
    with _lock:
        json.dump(st, open(STATUS, "w"), indent=1)


def start_batch(packs=None, ids=None, workers=4):
    m = json.load(open(MANIFEST, encoding="utf-8"))
    todo = [a for a in m["assets"] if (not packs or a["pack"] in packs) and (not ids or a["id"] in ids)
            and not os.path.exists(_out_path(a))]
    st = {"started": time.time(), "total": len(todo), "done": [], "failed": [], "running": [], "finished": False}
    _write_status(st)
    q = queue.Queue()
    for a in todo: q.put(a)

    def worker():
        while True:
            try: a = q.get_nowait()
            except queue.Empty: return
            with _lock: st["running"].append(a["id"])
            t = time.time(); r = generate_asset(a); r["id"] = a["id"]; r["sec"] = round(time.time() - t)
            with _lock:
                st["running"].remove(a["id"]); (st["done"] if r["ok"] else st["failed"]).append(r)
            _write_status(st)

    def run():
        ts = [threading.Thread(target=worker, daemon=True) for _ in range(workers)]
        for t in ts: t.start()
        for t in ts: t.join()
        st["finished"] = True; st["ended"] = time.time(); _write_status(st)

    threading.Thread(target=run, daemon=True).start()
    return {"queued": len(todo)}


def status():
    st = json.load(open(STATUS))
    return {"total": st["total"], "done": len(st["done"]), "failed": len(st["failed"]), "running": st["running"],
            "finished": st["finished"], "last_errors": [f["id"] + " | " + " || ".join(f.get("errors", []))[:400] for f in st["failed"][-3:]]}
