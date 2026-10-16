# 캐릭터를 "동물의 숲"처럼 둥글고 깔끔하게 다시 뜬다 (Blender 5.x 백그라운드).
#   blender -b --factory-startup -P remesh_bake.py -- <입력.glb> <출력.glb> [--voxel 0.012] [--tris 9000] [--bright 1.15]
#
# 1. 형태: 원래 모델을 복셀로 다시 떠서(Voxel Remesh) 잔 굴곡을 없애고, 세게 펴서 통통한 덩어리로. 삼각형 수는 --tris로 줄인다
# 2. UV: 새 형태에 UV를 새로 편다(Smart UV Project). 원래 모델의 갈라진 이음새를 물려받지 않는다
# 3. 색: 원래 모델의 기본색 텍스처를 새 형태로 구워 옮긴다(Cycles, Diffuse 색만)
# 4. 다듬기: 붓질 얼룩은 흐려 단색 면으로(눈·입처럼 대비가 큰 곳은 보존), 밝고 맑게(--bright, 채도 +20%)
# 5. 재질: 무광(거칠기 0.85, 금속성 0), 노멀맵 없음
import bpy, bmesh, sys
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:]
src, dst = argv[0], argv[1]
opts = {}
i = 2
while i < len(argv):
    opts[argv[i].lstrip("-")] = argv[i + 1]
    i += 2
voxel_rel = float(opts.get("voxel", 0.012))
tris_target = int(opts.get("tris", 9000))
bright = float(opts.get("bright", 1.15))
TEX = 2048  # 2048로 구워 얼굴 선을 살리고, 내보낼 때 1024로 줄인다

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
bpy.ops.object.select_all(action="DESELECT")
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
source = bpy.context.view_layer.objects.active
bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
height = source.dimensions.z

# ---------- 0. 귀여운 비율 ----------
# 목(--neck, 키 비율) 위는 머리로 보고 머리 가운데를 기준으로 --head배 키우고, 목 아래 몸은 높이를 --body배로 줄이고
# 옆으로는 --chubby배 통통하게. 목 근처(키의 6%)에서 부드럽게 이어 붙인다. 원래 모델을 먼저 바꾸고 새 형태는 그걸 따라 뜬다
head_k = float(opts.get("head", 1.22))
body_k = float(opts.get("body", 0.88))
chubby = float(opts.get("chubby", 1.08))
neck_rel = float(opts.get("neck", 0.52))
co = np.empty(len(source.data.vertices) * 3, dtype=np.float64)
source.data.vertices.foreach_get("co", co)
co = co.reshape(-1, 3)
zmin = co[:, 2].min()
neck = zmin + height * neck_rel
band = height * 0.06
head = co[co[:, 2] > neck]
hc = head.mean(axis=0) if len(head) else co.mean(axis=0)
body_c = co[co[:, 2] <= neck].mean(axis=0) if (co[:, 2] <= neck).any() else hc
# 몸: 높이는 바닥 기준으로 줄이고, 옆은 몸 가운데 기준으로 통통하게
body = co.copy()
body[:, 2] = zmin + (co[:, 2] - zmin) * body_k
body[:, 0] = body_c[0] + (co[:, 0] - body_c[0]) * chubby
body[:, 1] = body_c[1] + (co[:, 1] - body_c[1]) * chubby
# 머리: 몸이 줄어든 만큼 내려오고, 가운데 기준으로 커진다
drop = (body_k - 1.0) * (neck - zmin)
headp = hc + (co - hc) * head_k
headp[:, 2] += drop
t = np.clip((co[:, 2] - (neck - band)) / (2 * band), 0, 1)
w = (t * t * (3 - 2 * t))[:, None]
co = body * (1 - w) + headp * w
source.data.vertices.foreach_set("co", co.ravel())
source.data.update()
height = source.dimensions.z

# ---------- 1. 새 형태 ----------
bpy.ops.object.duplicate()
target = bpy.context.view_layer.objects.active
target.name = "Character"
target.data.materials.clear()
# 복셀로 다시 뜨려면 몸이 닫혀 있어야 한다. 잘라 낸 바닥(토끼의 풀밭 자리) 같은 구멍과 갈라진 이음새를 먼저 막는다
bm = bmesh.new()
bm.from_mesh(target.data)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=height * 0.0008)
bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)
bm.to_mesh(target.data)
bm.free()
m = target.modifiers.new("Remesh", "REMESH")
m.mode = "VOXEL"
m.voxel_size = height * voxel_rel
m.use_smooth_shade = True
bpy.ops.object.modifier_apply(modifier=m.name)
m = target.modifiers.new("Smooth", "LAPLACIANSMOOTH")
m.iterations = 6
m.lambda_factor = 0.6
m.use_volume_preserve = True
m.use_normalized = True
bpy.ops.object.modifier_apply(modifier=m.name)
tris = sum(len(p.vertices) - 2 for p in target.data.polygons)
if tris > tris_target:
    m = target.modifiers.new("Decimate", "DECIMATE")
    m.ratio = tris_target / tris
    bpy.ops.object.modifier_apply(modifier=m.name)
bpy.ops.object.shade_smooth()

# --crop: 새 형태를 뜬 뒤 바닥(발밑 풀밭 등)을 키 비율만큼 평평하게 잘라 내고 단면을 막는다.
# 잘라 낸 원본은 구멍이 들쭉날쭉해 복셀로 다시 뜰 수 없어서, 닫힌 원본으로 뜬 다음에 자른다
crop = float(opts.get("crop", 0))
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
if crop > 0:
    zs = [v.co.z for v in target.data.vertices]
    cut = min(zs) + (max(zs) - min(zs)) * crop
    bpy.ops.mesh.bisect(plane_co=(0, 0, cut), plane_no=(0, 0, 1), clear_inner=True, use_fill=True)
    bpy.ops.mesh.select_all(action="SELECT")

# ---------- 2. UV ----------
bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.006)
bpy.ops.object.mode_set(mode="OBJECT")

# ---------- 3. 색 굽기 ----------
img = bpy.data.images.new("Albedo", TEX, TEX, alpha=False)
mat = bpy.data.materials.new("Character")
mat.use_nodes = True
nt = mat.node_tree
bsdf = nt.nodes["Principled BSDF"]
tex = nt.nodes.new("ShaderNodeTexImage")
tex.image = img
nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
nt.nodes.active = tex
target.data.materials.append(mat)

# 원래 재질은 금속성 0으로(금속이면 Diffuse 색이 검게 구워진다)
for slot in source.material_slots:
    if slot.material and slot.material.use_nodes:
        for n in slot.material.node_tree.nodes:
            if n.type == "BSDF_PRINCIPLED":
                for link in list(n.inputs["Metallic"].links):
                    slot.material.node_tree.links.remove(link)
                n.inputs["Metallic"].default_value = 0.0

sc = bpy.context.scene
sc.render.engine = "CYCLES"
sc.cycles.samples = 4
sc.cycles.device = "CPU"
bake = sc.render.bake
bake.use_selected_to_active = True
bake.cage_extrusion = height * 0.03
bake.max_ray_distance = height * 0.08
bake.margin = 6
bake.use_pass_direct = False
bake.use_pass_indirect = False
bake.use_pass_color = True
bpy.ops.object.select_all(action="DESELECT")
source.select_set(True)
target.select_set(True)
bpy.context.view_layer.objects.active = target
bpy.ops.object.bake(type="DIFFUSE")

# ---------- 4. 색 다듬기 ----------


def box_blur(a, r):
    out = a
    for _ in range(2):
        pad = np.pad(out, ((r + 1, r), (r + 1, r), (0, 0)), mode="edge")
        c = pad.cumsum(0).cumsum(1)
        k = 2 * r + 1
        out = (c[k:, k:] - c[:-k, k:] - c[k:, :-k] + c[:-k, :-k]) / (k * k)
    return out


def coverage(o, w, h):
    me = o.data
    me.calc_loop_triangles()
    uv = me.uv_layers.active.data
    mask = np.zeros((h, w), dtype=np.float32)
    for tri in me.loop_triangles:
        xs = [uv[l].uv.x * (w - 1) for l in tri.loops]
        ys = [uv[l].uv.y * (h - 1) for l in tri.loops]
        x0, x1 = max(int(min(xs)) - 1, 0), min(int(max(xs)) + 2, w)
        y0, y1 = max(int(min(ys)) - 1, 0), min(int(max(ys)) + 2, h)
        if x1 <= x0 or y1 <= y0:
            continue
        gx, gy = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
        (ax, ay), (bx, by), (cx, cy) = zip(xs, ys)
        den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
        if abs(den) < 1e-9:
            continue
        l1 = ((by - cy) * (gx - cx) + (cx - bx) * (gy - cy)) / den
        l2 = ((cy - ay) * (gx - cx) + (ax - cx) * (gy - cy)) / den
        inside = (l1 >= -0.1) & (l2 >= -0.1) & (1 - l1 - l2 >= -0.1)
        mask[y0:y1, x0:x1] = np.maximum(mask[y0:y1, x0:x1], inside.astype(np.float32))
    return mask


px = np.empty(TEX * TEX * 4, dtype=np.float32)
img.pixels.foreach_get(px)
px = px.reshape(TEX, TEX, 4)
rgb = px[:, :, :3]
cov = coverage(target, TEX, TEX)
r = TEX // 256
num = box_blur(rgb * cov[:, :, None], r)
den = box_blur(np.repeat(cov[:, :, None], 3, axis=2), r)
blur = np.where(den > 1e-4, num / np.maximum(den, 1e-4), rgb)
diff = np.abs(rgb - blur).mean(axis=2, keepdims=True)
keep = np.clip((diff - 0.08) / 0.1, 0, 1)  # 눈·코·입 선은 남기고 잔 붓질만 흐린다
out = blur * (1 - keep) + rgb * keep
# 밝고 맑게: 어두운 쪽을 들어 올리고(감마) 채도 +20%
out = np.clip(out * bright, 0, 1) ** 0.9
grey = out.mean(axis=2, keepdims=True)
out = np.clip(grey + (out - grey) * 1.2, 0, 1)
px[:, :, :3] = out
px[:, :, 3] = 1.0
img.pixels.foreach_set(px.ravel())
img.update()
img.scale(1024, 1024)

# ---------- 5. 재질, 내보내기 ----------
bsdf.inputs["Metallic"].default_value = 0.0
bsdf.inputs["Roughness"].default_value = 0.85
if "Specular IOR Level" in bsdf.inputs:
    bsdf.inputs["Specular IOR Level"].default_value = 0.2
bpy.data.objects.remove(source)

# --feet R,G,B: 잘라 낸 자리에 그 색의 둥근 발 두 개(토끼처럼 다리가 풀밭과 붙어 생성된 경우)
if "feet" in opts:
    col = tuple(float(v) for v in opts["feet"].split(","))
    vs = [target.matrix_world @ v.co for v in target.data.vertices]
    zmin2 = min(v.z for v in vs)
    h2 = max(v.z for v in vs) - zmin2
    low = [v for v in vs if v.z < zmin2 + h2 * 0.05]
    cx = sum(v.x for v in low) / len(low)
    cy = sum(v.y for v in low) / len(low)
    fm = bpy.data.materials.new("Feet")
    fm.use_nodes = True
    fb = fm.node_tree.nodes["Principled BSDF"]
    fb.inputs["Base Color"].default_value = (*col, 1)
    fb.inputs["Roughness"].default_value = 0.85
    r = h2 * 0.085
    parts = []
    for sx in (-1, 1):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=14, radius=r,
                                             location=(cx + sx * h2 * 0.1, cy - h2 * 0.07, zmin2 - r * 0.45))
        f = bpy.context.active_object
        f.scale = (1.0, 1.3, 0.75)
        f.data.materials.append(fm)
        bpy.ops.object.shade_smooth()
        parts.append(f)
    bpy.ops.object.select_all(action="DESELECT")
    for f in parts:
        f.select_set(True)
    target.select_set(True)
    bpy.context.view_layer.objects.active = target
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    bpy.ops.object.join()
    target = bpy.context.view_layer.objects.active

tris = sum(len(p.vertices) - 2 for p in target.data.polygons)
print(f"[REMESH] {src} -> {dst}: tris {tris}")
bpy.ops.object.select_all(action="DESELECT")
target.select_set(True)
bpy.ops.export_scene.gltf(filepath=dst, export_format="GLB", use_selection=True,
                          export_image_format="JPEG", export_jpeg_quality=92, export_apply=True)
