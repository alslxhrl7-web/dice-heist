# 캐릭터 GLB 다듬기 (Blender 5.x 백그라운드).
#   blender -b --factory-startup -P refine_character.py -- <입력.glb> <출력.glb> [--crop 0.15] [--feet R,G,B] [--target 10000]
#
# VARCO가 만든 모델을 6천 삼각형으로 줄이면서 생긴 각진 면을 부드럽게 펴고(Laplacian 스무딩 → 한 번 세분화 → 목표 면 수로 줄이기),
# 재질을 무광으로 맞춘다(금속성 0, 거칠기 0.7, 노멀맵은 절반 세기). --crop은 발밑 풀밭처럼 바닥에 붙은 덩어리를
# 키의 비율(0~1) 아래로 잘라 내고, --feet는 잘린 자리에 그 색의 둥근 발 두 개를 붙인다(토끼처럼 다리가 풀과 붙어 생성된 경우).
import bpy, bmesh, sys, math
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
src, dst = argv[0], argv[1]
opts = {}
i = 2
while i < len(argv):
    opts[argv[i].lstrip("-")] = argv[i + 1]
    i += 2
crop = float(opts.get("crop", 0))
target = int(opts.get("target", 10000))
feet = tuple(float(v) for v in opts["feet"].split(",")) if "feet" in opts else None

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
bpy.ops.object.select_all(action="DESELECT")
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active
# 부모(glTF 루트 노드)의 변환을 메시에 굽는다
bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)


def tri_count(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def zrange(o):
    zs = [(o.matrix_world @ v.co).z for v in o.data.vertices]
    return min(zs), max(zs)


before = tri_count(obj)
zmin, zmax = zrange(obj)
height = zmax - zmin

# 1. 틈 메우기
bm = bmesh.new()
bm.from_mesh(obj.data)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=height * 0.0008)

# 2. 바닥 덩어리 잘라 내기
if crop > 0:
    cut = zmin + height * crop
    bad = [v for v in bm.verts if v.co.z < cut]
    bmesh.ops.delete(bm, geom=bad, context="VERTS")
    # 잘라 낸 뒤 떨어져 나온 작은 조각(풀잎 등)은 버린다: 가장 큰 덩어리의 2% 미만
    bm.verts.ensure_lookup_table()
    seen, islands = set(), []
    for v in bm.verts:
        if v in seen:
            continue
        stack, isl = [v], []
        seen.add(v)
        while stack:
            x = stack.pop()
            isl.append(x)
            for e in x.link_edges:
                y = e.other_vert(x)
                if y not in seen:
                    seen.add(y)
                    stack.append(y)
        islands.append(isl)
    biggest = max(len(s) for s in islands)
    small = [v for s in islands if len(s) < biggest * 0.02 for v in s]
    if small:
        bmesh.ops.delete(bm, geom=small, context="VERTS")
bm.to_mesh(obj.data)
bm.free()

# 3. 각진 면 펴기 → 세분화 → 목표 면 수로 줄이기
def add_apply(mod_type, **kw):
    m = obj.modifiers.new(mod_type, mod_type)
    for k, v in kw.items():
        setattr(m, k, v)
    bpy.ops.object.modifier_apply(modifier=m.name)

add_apply("LAPLACIANSMOOTH", iterations=3, lambda_factor=0.35, use_volume_preserve=True, use_normalized=True)
add_apply("SUBSURF", levels=1, render_levels=1, uv_smooth="PRESERVE_BOUNDARIES")
now = tri_count(obj)
if now > target:
    add_apply("DECIMATE", decimate_type="COLLAPSE", ratio=target / now)
bpy.ops.object.shade_smooth()

# 4. 둥근 발
if feet:
    zmin2, _ = zrange(obj)
    xs = [(obj.matrix_world @ v.co).x for v in obj.data.vertices if (obj.matrix_world @ v.co).z < zmin2 + height * 0.06]
    cx = (min(xs) + max(xs)) / 2 if xs else 0
    ys = [(obj.matrix_world @ v.co).y for v in obj.data.vertices if (obj.matrix_world @ v.co).z < zmin2 + height * 0.06]
    cy = (min(ys) + max(ys)) / 2 if ys else 0
    mat = bpy.data.materials.new("Feet")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*feet, 1)
    bsdf.inputs["Roughness"].default_value = 0.75
    r = height * 0.075
    parts = []
    for sx in (-1, 1):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=20, ring_count=12, radius=r,
                                             location=(cx + sx * height * 0.085, cy - height * 0.02, zmin2 - r * 0.25))
        f = bpy.context.active_object
        f.scale = (1.0, 1.35, 0.72)  # 앞으로 긴 둥근 발
        f.data.materials.append(mat)
        bpy.ops.object.shade_smooth()
        parts.append(f)
    bpy.ops.object.select_all(action="DESELECT")
    for f in parts:
        f.select_set(True)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active

# 5. 무광 재질
for slot in obj.material_slots:
    m = slot.material
    if not m or not m.use_nodes:
        continue
    nodes = m.node_tree.nodes
    for n in nodes:
        if n.type == "BSDF_PRINCIPLED":
            for link in list(n.inputs["Metallic"].links) + list(n.inputs["Roughness"].links):
                m.node_tree.links.remove(link)
            n.inputs["Metallic"].default_value = 0.0
            n.inputs["Roughness"].default_value = 0.7
        if n.type == "NORMAL_MAP":
            n.inputs["Strength"].default_value = 0.5

# 6. 텍스처를 1024로(이미 작으면 그대로)
for img in bpy.data.images:
    if img.size[0] > 1024:
        img.scale(1024, 1024)

# 7. 덧칠: VARCO는 앞 그림만 보고 만들어 뒷머리·꼬리 텍스처가 얼룩진다(여우는 회녹색, 너구리는 검정).
#    --backhead R,G,B: 머리 높이(--headfrom~--headto, 키 비율)에서 뒤(+Y)를 보는 면
#    --box x0,x1,y0,y1,z0,z1:R,G,B: 몸 크기 비율 상자 안의 모든 면(꼬리처럼 방향과 상관없는 곳)
#    해당 면이 쓰는 텍스처 픽셀을 그 색으로 덧칠하고, 원래 명암은 조금(20%) 남긴다
if "backhead" in opts or "box" in opts:
    import numpy as np
    lo = float(opts.get("headfrom", 0.55))
    hi = float(opts.get("headto", 0.9))
    rules = []
    if "backhead" in opts:
        rules.append(("back", None, np.array([float(v) for v in opts["backhead"].split(",")], dtype=np.float32)))
    if "box" in opts:
        b, col = opts["box"].split(":")
        rules.append(("box", [float(v) for v in b.split(",")], np.array([float(v) for v in col.split(",")], dtype=np.float32)))
    me = obj.data
    me.calc_loop_triangles()
    uv = me.uv_layers.active.data
    zmin3, zmax3 = zrange(obj)
    h3 = zmax3 - zmin3
    # 기본색 텍스처 찾기
    base = None
    for slot in obj.material_slots:
        m = slot.material
        if m and m.use_nodes:
            for n in m.node_tree.nodes:
                if n.type == "BSDF_PRINCIPLED" and n.inputs["Base Color"].links:
                    node = n.inputs["Base Color"].links[0].from_node
                    if node.type == "TEX_IMAGE":
                        base = node.image
    if base is not None:
        w, hgt = base.size
        px = np.empty(w * hgt * 4, dtype=np.float32)
        base.pixels.foreach_get(px)
        px = px.reshape(hgt, w, 4)
        mw = obj.matrix_world
        ws = [mw @ v.co for v in me.vertices]
        bmin = Vector((min(v.x for v in ws), min(v.y for v in ws), min(v.z for v in ws)))
        bmax = Vector((max(v.x for v in ws), max(v.y for v in ws), max(v.z for v in ws)))
        span = bmax - bmin
        for kind, box, fur in rules:
            mask = np.zeros((hgt, w), dtype=bool)
            count = 0
            for tri in me.loop_triangles:
                n = (mw.to_3x3() @ tri.normal).normalized()
                c = mw @ tri.center
                r = Vector(((c.x - bmin.x) / span.x, (c.y - bmin.y) / span.y, (c.z - bmin.z) / span.z))
                if kind == "back":
                    if n.y < 0.35 or r.z < lo or r.z > hi:
                        continue
                elif not (box[0] <= r.x <= box[1] and box[2] <= r.y <= box[3] and box[4] <= r.z <= box[5]):
                    continue
                pts = [uv[l].uv for l in tri.loops]
                xs = [p.x * (w - 1) for p in pts]
                ys = [p.y * (hgt - 1) for p in pts]
                x0, x1 = max(int(min(xs)) - 2, 0), min(int(max(xs)) + 3, w)
                y0, y1 = max(int(min(ys)) - 2, 0), min(int(max(ys)) + 3, hgt)
                if x1 <= x0 or y1 <= y0:
                    continue
                gx, gy = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
                (ax, ay), (bx, by), (cx2, cy2) = zip(xs, ys)
                den = (by - cy2) * (ax - cx2) + (cx2 - bx) * (ay - cy2)
                if abs(den) < 1e-9:
                    continue
                l1 = ((by - cy2) * (gx - cx2) + (cx2 - bx) * (gy - cy2)) / den
                l2 = ((cy2 - ay) * (gx - cx2) + (ax - cx2) * (gy - cy2)) / den
                l3 = 1 - l1 - l2
                pad = -0.08  # 경계 이음새가 남지 않게 조금 넓게
                inside = (l1 >= pad) & (l2 >= pad) & (l3 >= pad)
                mask[y0:y1, x0:x1] |= inside
                count += 1
            sel = px[mask]
            lum = sel[:, :3].mean(axis=1, keepdims=True)
            shade = 0.85 + 0.3 * (lum - lum.mean())  # 원래 명암을 살짝 남긴다
            sel[:, :3] = 0.8 * fur * shade + 0.2 * sel[:, :3]
            px[mask] = sel
            base.pixels.foreach_set(px.ravel())
            base.update()
            print(f"[REFINE] {kind}: {count} faces, {int(mask.sum())} px")

print(f"[REFINE] {src} -> {dst}: tris {before} -> {tri_count(obj)}")
bpy.ops.export_scene.gltf(filepath=dst, export_format="GLB", use_selection=False,
                          export_image_format="JPEG", export_jpeg_quality=88, export_apply=True)
