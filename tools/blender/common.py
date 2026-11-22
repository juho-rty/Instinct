"""블렌더 그림 공장의 공통 부분: 장면, 빛, 사진기, 재질, 모양 만들기, 장 붙이기.

모든 그림은 같은 사진기 각도(위에서 40° 내려다본 직교 사진기)와 같은 빛(왼쪽 위 해, 하늘빛)으로 찍어
사람·나무·건물이 한 장면처럼 보이게 한다. 바닥에는 그림자만 받는 판(shadow catcher)을 깔아
진짜 드리운 그림자가 함께 찍힌다.
"""
import bpy, bmesh, math, os
import numpy as np
from mathutils import Vector

ELEV = 40.0          # 사진기가 내려다보는 각(도)
INK = (0.17, 0.14, 0.12)


def hexcol(h):
    h = h.lstrip('#')
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    # sRGB → 선형
    return tuple(((x + 0.055) / 1.055) ** 2.4 if x > 0.04045 else x / 12.92 for x in c) + (1.0,)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = 24
    sc.cycles.use_denoising = True
    try:
        sc.cycles.denoiser = 'OPENIMAGEDENOISE'
    except Exception:
        pass
    sc.cycles.max_bounces = 4
    sc.render.film_transparent = True
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGBA'
    sc.render.resolution_percentage = 100
    try:
        sc.view_settings.view_transform = 'AgX'
        sc.view_settings.look = 'AgX - Punchy'
    except Exception:
        try:
            sc.view_settings.view_transform = 'Standard'
        except Exception:
            pass
    sc.view_settings.exposure = 0.15
    return sc


def lights(sun_strength=4.0, sky=(0.62, 0.72, 0.86), sky_strength=0.9):
    sc = bpy.context.scene
    w = bpy.data.worlds.new("sky")
    w.use_nodes = True
    bg = next(n for n in w.node_tree.nodes if n.type == 'BACKGROUND')
    bg.inputs[0].default_value = sky + (1,)
    bg.inputs[1].default_value = sky_strength
    sc.world = w
    sun = bpy.data.lights.new("sun", 'SUN')
    sun.energy = sun_strength
    sun.angle = math.radians(6)
    sun.color = (1.0, 0.95, 0.86)
    o = bpy.data.objects.new("sun", sun)
    # 화면 왼쪽 위(뒤쪽)에서 비춘다 → 그림자는 오른쪽 아래로
    o.rotation_euler = (math.radians(16), math.radians(-16), math.radians(-30))
    sc.collection.objects.link(o)


def camera(px, ortho, ground_px):
    """정사각 px 크기, ortho 미터 폭. 땅의 원점이 위에서 ground_px 줄에 오도록 한다."""
    sc = bpy.context.scene
    sc.render.resolution_x = sc.render.resolution_y = px
    cam = bpy.data.cameras.new("cam")
    cam.type = 'ORTHO'
    cam.ortho_scale = ortho
    cam.shift_y = (ground_px - px / 2) / px
    cam.clip_end = 200
    o = bpy.data.objects.new("cam", cam)
    e = math.radians(ELEV)
    o.location = (0, -40 * math.cos(e), 40 * math.sin(e))
    o.rotation_euler = (math.radians(90 - ELEV), 0, 0)
    sc.collection.objects.link(o)
    sc.camera = o
    # 그림자만 받는 바닥
    bpy.ops.mesh.primitive_plane_add(size=40)
    g = bpy.context.active_object
    g.name = "catcher"
    g.is_shadow_catcher = True
    return o


_mats = {}


def mat(hexv, rough=0.62, sheen=0.0, metal=0.0, emit=0.0):
    key = (hexv, rough, sheen, metal, emit)
    if key in _mats:
        return _mats[key]
    m = bpy.data.materials.new(hexv)
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    b.inputs['Base Color'].default_value = hexcol(hexv)
    b.inputs['Roughness'].default_value = rough
    b.inputs['Metallic'].default_value = metal
    for name in ('Sheen Weight', 'Sheen'):
        if name in b.inputs:
            b.inputs[name].default_value = sheen
            break
    if emit > 0:
        b.inputs['Emission Color'].default_value = hexcol(hexv)
        b.inputs['Emission Strength'].default_value = emit
    _mats[key] = m
    return m


def empty(name, parent=None, loc=(0, 0, 0)):
    o = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(o)
    o.parent = parent
    o.location = loc
    o.rotation_mode = 'YXZ'
    return o


def _obj(name, bm, material, parent, loc, smooth=True, subsurf=0, rot=(0, 0, 0), scale=(1, 1, 1)):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    if smooth:
        for p in me.polygons:
            p.use_smooth = True
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    o.data.materials.append(material)
    o.parent = parent
    o.location = loc
    o.rotation_euler = rot
    o.scale = scale
    if subsurf:
        m = o.modifiers.new("ss", 'SUBSURF')
        m.levels = m.render_levels = subsurf
    return o


def cone(name, r1, r2, depth, material, parent=None, loc=(0, 0, 0), seg=12, down=True, subsurf=1, rot=(0, 0, 0), scale=(1, 1, 1), round_ends=True):
    """밑 반지름 r1, 위 반지름 r2. down이면 원점이 위 끝(관절)이고 아래로 뻗는다."""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r1, radius2=r2, depth=depth)
    dz = -depth / 2 if down else depth / 2
    bmesh.ops.translate(bm, verts=bm.verts, vec=(0, 0, dz))
    if round_ends:
        # 끝을 둥글게: 끝 고리를 한 번 더 넣고 안으로 오므린다
        caps = [f for f in bm.faces if len(f.verts) == seg]
        for f in caps:
            r = bmesh.ops.inset_region(bm, faces=[f], thickness=min(r1, r2) * 0.45, depth=min(r1, r2) * 0.35)
    return _obj(name, bm, material, parent, loc, True, subsurf, rot, scale)


def sphere(name, r, material, parent=None, loc=(0, 0, 0), scale=(1, 1, 1), seg=16, rings=10, rot=(0, 0, 0), keep=None, subsurf=0):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=r)
    if keep:
        gone = [v for v in bm.verts if not keep(v.co)]
        bmesh.ops.delete(bm, geom=gone, context='VERTS')
    return _obj(name, bm, material, parent, loc, True, subsurf, rot, scale)


def box(name, size, material, parent=None, loc=(0, 0, 0), rot=(0, 0, 0), bevel=0.0, smooth=False):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1)
    bmesh.ops.scale(bm, vec=size, verts=bm.verts)
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=2, affect='EDGES', profile=0.5)
    return _obj(name, bm, material, parent, loc, smooth, 0, rot)


def torus(name, R, r, material, parent=None, loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_torus_add(major_radius=R, minor_radius=r, major_segments=20, minor_segments=8)
    o = bpy.context.active_object
    o.name = name
    o.data.materials.append(material)
    o.parent = parent
    o.location = loc
    o.rotation_euler = rot
    o.scale = scale
    for p in o.data.polygons:
        p.use_smooth = True
    return o


def set_visible(objs, on):
    for o in objs:
        o.hide_render = not on
        for c in o.children_recursive:
            c.hide_render = not on


def render_to(path):
    sc = bpy.context.scene
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)


def load_rgba(path):
    img = bpy.data.images.load(path)
    w, h = img.size
    a = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)[::-1]  # 위가 0번 줄
    bpy.data.images.remove(img)
    return a


def outline(a, px=2, alpha=0.75, solid=0.85):
    """그림자(반투명)는 빼고 몸통(불투명)의 바깥에만 먹선을 두른다."""
    a = a.copy()
    body = a[..., 3] >= solid
    # 그림자만 받는 바닥이 남긴 밝은 기운을 지운다: 몸이 아닌 곳은 검은 그림자뿐
    a[~body, :3] = 0
    a[~body, 3] = np.minimum(a[~body, 3], 0.55)
    h, w = body.shape
    pad = np.pad(body, px)
    grown = body.copy()
    for dy in range(-px, px + 1):
        for dx in range(-px, px + 1):
            if dx * dx + dy * dy > px * px + 1:
                continue
            # 가장자리에서 반대편으로 넘어가지 않게 덧댄 판에서 잘라 온다
            grown |= pad[px + dy:px + dy + h, px + dx:px + dx + w]
    ring = grown & ~body
    out = a.copy()
    ink = np.array(INK, dtype=np.float32)
    # 선 아래에 있던 그림자와 섞는다(앞쪽에 선을 얹는다)
    k = alpha
    out[ring, :3] = ink * k + out[ring, :3] * out[ring, 3:4] * (1 - k)
    out[ring, 3] = k + out[ring, 3] * (1 - k)
    nz = out[ring, 3] > 0
    idx = np.where(ring)
    rgb = out[ring, :3]
    rgb[nz] = rgb[nz] / out[ring, 3][nz, None]
    out[idx[0], idx[1], :3] = rgb
    return out


def save_rgba(a, path):
    h, w = a.shape[:2]
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=True)
    img.pixels[:] = np.ascontiguousarray(a[::-1]).reshape(-1)
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()
    bpy.data.images.remove(img)
