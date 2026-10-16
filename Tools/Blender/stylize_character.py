# 캐릭터 GLB를 "동물의 숲" 느낌으로 (Blender 5.x 백그라운드). refine_character.py로 다듬은 모델에 이어서 쓴다.
#   blender -b --factory-startup -P stylize_character.py -- <입력.glb> <출력.glb> [--smooth 8] [--flat 0.06]
#
# 1. 형태: Laplacian 스무딩을 더 세게 걸어 울퉁불퉁한 면을 둥글고 매끈하게(부피는 지킨다)
# 2. 음영: 노멀맵·AO를 떼어 표면 잔무늬 없이 부드러운 그림자만 남긴다. 거칠기 0.85, 금속성 0
# 3. 텍스처: 붓질 얼룩은 흐려서 단색 면으로, 눈·입·무늬 경계처럼 대비가 큰 곳은 그대로 둔다(경계 보존 흐림).
#    채도를 조금 올려 맑은 색으로
import bpy, sys
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:]
src, dst = argv[0], argv[1]
opts = {}
i = 2
while i < len(argv):
    opts[argv[i].lstrip("-")] = argv[i + 1]
    i += 2
smooth = int(opts.get("smooth", 8))
flat = float(opts.get("flat", 0.16))  # 이보다 작은 밝기 차이는 얼룩으로 보고 흐린다

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
bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")

# 1. 형태
# glTF는 텍스처 이음새마다 꼭짓점을 갈라 둔다. 붙이지 않고 펴면 이음새가 벌어져 금 간 선이 생긴다
import bmesh
bm = bmesh.new()
bm.from_mesh(obj.data)
size = max(obj.dimensions)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=size * 0.0006)
bm.to_mesh(obj.data)
bm.free()
m = obj.modifiers.new("Smooth", "LAPLACIANSMOOTH")
m.iterations = smooth
m.lambda_factor = 0.45
m.use_volume_preserve = True
m.use_normalized = True
bpy.ops.object.modifier_apply(modifier=m.name)
bpy.ops.object.shade_smooth()


def box_blur(img, r):
    # 적분 영상으로 빠른 상자 흐림(두 번 하면 가우스에 가깝다)
    out = img
    for _ in range(2):
        pad = np.pad(out, ((r + 1, r), (r + 1, r), (0, 0)), mode="edge")
        c = pad.cumsum(0).cumsum(1)
        k = 2 * r + 1
        out = (c[k:, k:] - c[:-k, k:] - c[k:, :-k] + c[:-k, :-k]) / (k * k)
    return out


def uv_coverage(o, w, h):
    """UV 삼각형이 덮는 텍스처 픽셀 = 1, 나머지 = 0 (조금 넓게)."""
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
        inside = (l1 >= -0.05) & (l2 >= -0.05) & (1 - l1 - l2 >= -0.05)
        mask[y0:y1, x0:x1] = np.maximum(mask[y0:y1, x0:x1], inside.astype(np.float32))
    return mask


# 2·3. 재질과 텍스처
done = set()
for slot in obj.material_slots:
    mat = slot.material
    if not mat or not mat.use_nodes:
        continue
    nt = mat.node_tree
    for n in list(nt.nodes):
        if n.type == "BSDF_PRINCIPLED":
            for name in ("Metallic", "Roughness", "Normal"):
                for link in list(n.inputs[name].links):
                    nt.links.remove(link)
            n.inputs["Metallic"].default_value = 0.0
            n.inputs["Roughness"].default_value = 0.85
            if "Specular IOR Level" in n.inputs:
                n.inputs["Specular IOR Level"].default_value = 0.25
            if n.inputs["Base Color"].links:
                tex = n.inputs["Base Color"].links[0].from_node
                if tex.type == "TEX_IMAGE" and tex.image and tex.image.name not in done:
                    img = tex.image
                    done.add(img.name)
                    w, h = img.size
                    px = np.empty(w * h * 4, dtype=np.float32)
                    img.pixels.foreach_get(px)
                    px = px.reshape(h, w, 4)
                    rgb = px[:, :, :3]
                    # 모델이 실제로 쓰는 텍스처 영역(UV 삼각형)만 섞어 흐린다. 빈 영역(검정)이 섞이면 이음새가 검은 선으로 보인다
                    cover = uv_coverage(obj, w, h)
                    r = max(3, w // 160)
                    num = box_blur(rgb * cover[:, :, None], r)
                    den = box_blur(np.repeat(cover[:, :, None], 3, axis=2), r)
                    blur = np.where(den > 1e-4, num / np.maximum(den, 1e-4), rgb)
                    diff = np.abs(rgb - blur).mean(axis=2, keepdims=True)
                    keep = np.clip((diff - flat) / flat, 0, 1)  # 대비가 큰 경계는 원래대로
                    out = blur * (1 - keep) + rgb * keep
                    # 맑은 색: 채도 조금 올리기
                    grey = out.mean(axis=2, keepdims=True)
                    out = np.clip(grey + (out - grey) * 1.12, 0, 1)
                    px[:, :, :3] = out
                    img.pixels.foreach_set(px.ravel())
                    img.update()
        # 쓰지 않게 된 노멀맵·거칠기 텍스처 노드는 지워 GLB에 안 들어가게
        if n.type == "NORMAL_MAP":
            nt.nodes.remove(n)
    for n in list(nt.nodes):
        if n.type == "TEX_IMAGE" and not any(l.to_node.type == "BSDF_PRINCIPLED" and l.to_socket.name == "Base Color"
                                             for o in n.outputs for l in o.links):
            nt.nodes.remove(n)

tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
print(f"[STYLE] {src} -> {dst}: tris {tris}, smooth {smooth}")
bpy.ops.export_scene.gltf(filepath=dst, export_format="GLB", export_image_format="JPEG",
                          export_jpeg_quality=90, export_apply=True)
