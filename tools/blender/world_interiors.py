"""Enterable buildings: each opens into a cut-away room diorama (two walls, floor, open
front, roof beams) like a dollhouse, entered by tapping the building's door. This module
lists the rooms and their furniture as blocks, and builds them in Blender as one scene each.

Room sizes are metres; furniture positions are from the room's back-left corner (x right, y forward)."""
import math

ROOMS = {
    "home.farmhouse": {
        "name": "Farmhouse", "size": (9.0, 7.0, 4.2), "walls": "plaster + timber", "floor": "wood planks",
        "use": "home: decorate, collect daily gift, see your stats",
        "props": [("Kitchen counter", 0.5, 0.4, 3.2, 0.7, 1.0), ("Stove", 3.9, 0.4, 0.9, 0.7, 1.0), ("Dining table", 2.0, 3.0, 1.8, 1.0, 0.8),
                  ("Chairs", 2.0, 4.1, 1.8, 0.5, 0.9), ("Fireplace", 6.0, 0.3, 1.8, 0.6, 1.6), ("Sofa", 6.0, 2.6, 2.2, 0.9, 0.8),
                  ("Rug", 5.6, 2.2, 2.8, 2.0, 0.02), ("Bookshelf", 8.3, 1.2, 0.5, 1.6, 2.0), ("Window", 8.9, 3.6, 0.1, 1.4, 1.2),
                  ("Plant", 8.2, 5.6, 0.5, 0.5, 0.9), ("Ladder to loft", 0.2, 5.0, 0.6, 0.3, 3.2), ("Lamp", 7.4, 4.2, 0.3, 0.3, 1.5)]},
    "home.warehouse": {
        "name": "Warehouse", "size": (10.0, 8.0, 5.0), "walls": "red planks", "floor": "concrete",
        "use": "storage: see stock, upgrade capacity",
        "props": [("Shelving A", 0.3, 0.3, 4.0, 0.8, 3.0), ("Shelving B", 5.0, 0.3, 4.5, 0.8, 3.0), ("Crate stack", 0.4, 3.0, 1.5, 1.5, 1.6),
                  ("Sacks", 0.4, 5.2, 1.6, 1.2, 0.8), ("Barrels", 8.4, 2.8, 1.2, 2.0, 1.0), ("Hand cart", 4.0, 4.0, 1.2, 0.8, 0.8),
                  ("Desk + ledger", 7.4, 5.8, 1.6, 0.8, 0.8), ("Hanging lamp", 4.8, 3.6, 0.4, 0.4, 0.6)]},
    "ranch.barn": {
        "name": "Cattle barn", "size": (12.0, 8.0, 5.5), "walls": "red planks", "floor": "straw",
        "use": "animals: feed, collect milk",
        "props": [("Stalls x4", 0.3, 0.3, 11.0, 2.4, 1.4), ("Feed trough", 0.3, 2.8, 11.0, 0.6, 0.6), ("Hay loft", 0.0, 0.0, 12.0, 2.6, 0.3),
                  ("Milk churns", 10.6, 6.0, 1.0, 1.0, 0.9), ("Pitchfork rack", 0.3, 6.5, 1.2, 0.3, 1.8), ("Water tank", 8.0, 6.4, 1.8, 0.9, 1.0)]},
    "ranch.coop": {
        "name": "Chicken coop", "size": (6.0, 5.0, 3.2), "walls": "planks", "floor": "straw",
        "use": "animals: feed, collect eggs",
        "props": [("Nesting boxes", 0.3, 0.3, 5.4, 0.7, 1.4), ("Perches", 0.4, 1.4, 0.4, 3.0, 1.2), ("Feeder", 2.5, 2.5, 0.6, 0.6, 0.5),
                  ("Egg basket", 4.6, 4.0, 0.5, 0.5, 0.3)]},
    "factory": {
        "name": "Factory (one per machine kind)", "size": (12.0, 9.0, 6.0), "walls": "brick + steel", "floor": "concrete",
        "use": "production: queue jobs, collect goods",
        "props": [("Main machine", 3.5, 2.5, 4.0, 3.0, 3.0), ("Conveyor", 0.5, 6.0, 11.0, 0.8, 0.9), ("Input hopper", 1.0, 1.0, 1.6, 1.6, 2.2),
                  ("Output crates", 9.8, 6.8, 1.6, 1.6, 1.0), ("Control panel", 9.5, 0.4, 1.2, 0.6, 1.4), ("Pipes", 0.0, 0.0, 12.0, 0.4, 0.4),
                  ("Worker", 8.0, 3.5, 0.5, 0.5, 1.6)]},
    "shop": {
        "name": "Shop (bakery, grocer, ...)", "size": (8.0, 6.0, 3.8), "walls": "plaster + tiles", "floor": "checker tiles",
        "use": "market: your listings, buy from others",
        "props": [("Counter + till", 1.0, 3.4, 4.0, 0.8, 1.0), ("Display shelves", 0.3, 0.3, 7.4, 0.6, 2.2), ("Goods baskets", 5.6, 3.2, 1.8, 1.2, 0.6),
                  ("Shopkeeper", 3.0, 2.6, 0.5, 0.5, 1.6), ("Chalk menu board", 6.2, 0.2, 1.4, 0.1, 1.0), ("Door bell", 7.6, 5.6, 0.2, 0.2, 0.2)]},
    "civic.town_hall": {
        "name": "Town hall", "size": (12.0, 8.0, 5.0), "walls": "panelled wood", "floor": "marble",
        "use": "quests and goals board, leaderboard",
        "props": [("Mayor's desk", 4.5, 1.0, 3.0, 1.2, 1.0), ("Quest board", 0.2, 2.0, 0.2, 3.0, 2.0), ("Leaderboard trophy shelf", 9.0, 0.3, 2.6, 0.6, 2.2),
                  ("Benches", 3.0, 5.0, 6.0, 1.2, 0.8), ("Flags", 5.5, 0.2, 1.0, 0.2, 3.0)]},
    "civic.community": {
        "name": "Community hall (teams)", "size": (11.0, 8.0, 4.5), "walls": "painted wood", "floor": "wood",
        "use": "teams: chat, help requests, team events",
        "props": [("Long table", 3.0, 3.0, 5.0, 1.4, 0.8), ("Notice board", 0.2, 2.0, 0.2, 3.0, 1.8), ("Banners", 2.0, 0.2, 7.0, 0.2, 1.5),
                  ("Trophy cabinet", 9.6, 0.3, 1.2, 0.6, 2.0), ("Tea corner", 9.0, 5.8, 1.8, 1.0, 1.0)]},
    "port.office": {
        "name": "Harbour office", "size": (8.0, 6.0, 3.8), "walls": "blue planks", "floor": "wood",
        "use": "orders: deliveries by boat",
        "props": [("Order desk", 2.0, 1.0, 3.0, 1.0, 1.0), ("Map of routes", 0.2, 1.0, 0.1, 3.0, 1.6), ("Parcel shelves", 6.6, 0.3, 1.2, 4.0, 2.2),
                  ("Ship's wheel", 7.6, 4.8, 0.2, 0.8, 0.8), ("Lantern", 4.0, 0.4, 0.3, 0.3, 0.5)]},
    "leisure.cafe": {
        "name": "Cafe", "size": (8.0, 6.0, 3.6), "walls": "pastel plaster", "floor": "tiles",
        "use": "social: visit friends, daily treats",
        "props": [("Bar counter", 0.3, 0.4, 4.0, 0.8, 1.0), ("Coffee machine", 1.0, 0.4, 0.8, 0.6, 0.6), ("Tables x3", 3.0, 3.0, 4.5, 2.2, 0.8),
                  ("Cake display", 4.8, 0.4, 1.4, 0.7, 1.2), ("Plants", 7.3, 0.4, 0.5, 0.5, 1.0)]},
    "mine.entrance": {
        "name": "Mine", "size": (10.0, 8.0, 4.5), "walls": "rock + timber props", "floor": "dirt",
        "use": "mining: dig for stone and ore",
        "props": [("Rail track", 0.5, 3.5, 9.0, 1.2, 0.2), ("Mine cart", 6.0, 3.5, 1.4, 1.0, 1.0), ("Timber props", 0.0, 0.0, 10.0, 0.4, 4.0),
                  ("Ore pile", 1.0, 0.8, 2.0, 1.6, 1.0), ("Lanterns", 4.0, 0.2, 0.3, 0.3, 0.5), ("Tool rack", 8.5, 0.3, 1.2, 0.4, 1.6)]},
}


def build(scene_name_prefix="Interior: "):
    import bpy
    import bmesh
    made = []
    mats = {}

    def mat(name, rgb):
        if name not in mats:
            m = bpy.data.materials.new("Room " + name)
            m.use_nodes = True
            m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*rgb, 1)
            m.diffuse_color = (*rgb, 1)
            mats[name] = m
        return mats[name]

    def box(col, name, x, y, z, w, d, h, m):
        bm = bmesh.new()
        r = bmesh.ops.create_cube(bm, size=1.0)
        bmesh.ops.scale(bm, vec=(w, d, h), verts=r["verts"])
        bmesh.ops.translate(bm, vec=(x + w / 2, y + d / 2, z + h / 2), verts=bm.verts)
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        me.materials.append(m)
        o = bpy.data.objects.new(name, me)
        col.objects.link(o)
        return o

    for rid, room in ROOMS.items():
        sc = bpy.data.scenes.new(scene_name_prefix + room["name"])
        col = sc.collection
        W, D, H = room["size"]
        # the room opens towards the viewer: floor, back wall (y = 0) and left wall (x = 0), roof beams
        box(col, "Floor", 0, 0, -0.2, W, D, 0.2, mat("floor", (0.62, 0.45, 0.30)))
        box(col, "Wall back", 0, -0.3, 0, W, 0.3, H, mat("wall", (0.80, 0.74, 0.66)))
        box(col, "Wall left", -0.3, -0.3, 0, 0.3, D + 0.3, H, mat("wall", (0.80, 0.74, 0.66)))
        for k in range(5):
            box(col, f"Roof beam {k}", -0.3 + k * W / 4, -0.3, H, 0.3, D + 0.3, 0.3, mat("beam", (0.45, 0.30, 0.20)))
        box(col, "Roof", -0.3, -0.3, H + 0.3, W + 0.3, 0.25, 1.2, mat("roof", (0.35, 0.30, 0.55)))
        for name, x, y, w, d, h in room["props"]:
            box(col, name, x, y, 0.0, w, d, h, mat("prop", (0.95, 0.75, 0.40)))
            cu = bpy.data.curves.new(name + ".label", "FONT")
            cu.body = name
            cu.size = 0.28
            cu.align_x = "CENTER"
            t = bpy.data.objects.new(name + ".label", cu)
            t.location = (x + w / 2, y + d / 2, h + 0.15)
            col.objects.link(t)
        cam = bpy.data.cameras.new("Cam " + room["name"])
        cam.type = "ORTHO"
        cam.ortho_scale = max(W, D) * 1.9
        co = bpy.data.objects.new("Cam " + room["name"], cam)
        co.location = (W / 2 + 12, D / 2 + 12 * 0.0 - 12 + D + 12, 0)
        # a 3/4 view from the open front corner, looking at the room centre
        from mathutils import Vector
        target = Vector((W / 2, D / 2, H / 3))
        co.location = target + Vector((1.0, 1.25, 0.95)).normalized() * 30
        co.rotation_euler = (target - co.location).to_track_quat("-Z", "Y").to_euler()
        col.objects.link(co)
        sc.camera = co
        sun = bpy.data.lights.new("Sun " + room["name"], "SUN")
        sun.energy = 3.0
        so = bpy.data.objects.new("Sun " + room["name"], sun)
        so.rotation_euler = (math.radians(50), 0, math.radians(140))
        col.objects.link(so)
        sc.world = bpy.data.worlds.get("Sky")
        sc.render.engine = "BLENDER_EEVEE"
        sc.view_settings.view_transform = "Standard"
        made.append(sc)
    return made
