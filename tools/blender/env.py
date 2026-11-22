"""땅 위 사물 그림 공장: 나무·산·자원·부족 건물. 실행:
  blender -b --python tools/blender/env.py -- [preview] [이름...]

사람(people.py)과 같은 사진기 각도·빛으로 찍는다. 그림은 모두 256×256 정사각, 땅의 원점이
위에서 GROUND(208)번 줄 가운데. 게임은 정사각 하나를 그 자리에 맞춰 그린다(Art.Env).
"""
import bpy, bmesh, math, os, sys, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import importlib, common
importlib.reload(common)
from common import *
from mathutils import Vector, noise

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
ASSETS = os.path.join(ROOT, 'game', 'assets')
TMP = os.path.join(os.environ.get('TEMP', '/tmp'), 'instinct-env')
PX, GROUND = 256, 208


# ───────── 재질 ─────────

def nmat(a, b, scale=6.0, rough=0.75, detail=4.0, bump=0.25, mix=(0.35, 0.65)):
    """두 색을 잡음으로 섞은 재질 + 살짝 우툴두툴한 표면."""
    key = ('n', a, b, scale, rough, bump)
    if key in common._mats:
        return common._mats[key]
    m = bpy.data.materials.new(f'{a}-{b}')
    m.use_nodes = True
    nt = m.node_tree
    bs = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    bs.inputs['Roughness'].default_value = rough
    tc = nt.nodes.new('ShaderNodeTexCoord')
    nz = nt.nodes.new('ShaderNodeTexNoise')
    nz.inputs['Scale'].default_value = scale
    nz.inputs['Detail'].default_value = detail
    nt.links.new(tc.outputs['Object'], nz.inputs['Vector'])
    ramp = nt.nodes.new('ShaderNodeValToRGB')
    ramp.color_ramp.elements[0].position = mix[0]
    ramp.color_ramp.elements[1].position = mix[1]
    ramp.color_ramp.elements[0].color = hexcol(a)
    ramp.color_ramp.elements[1].color = hexcol(b)
    nt.links.new(nz.outputs['Fac'], ramp.inputs['Fac'])
    nt.links.new(ramp.outputs['Color'], bs.inputs['Base Color'])
    if bump > 0:
        bp = nt.nodes.new('ShaderNodeBump')
        bp.inputs['Strength'].default_value = bump
        nt.links.new(nz.outputs['Fac'], bp.inputs['Height'])
        nt.links.new(bp.outputs['Normal'], bs.inputs['Normal'])
    common._mats[key] = m
    return m


def snowmat(rock_a, rock_b, snow, line):
    """높이가 line(물체 좌표 z)을 넘으면 눈."""
    m = bpy.data.materials.new('snowrock')
    m.use_nodes = True
    nt = m.node_tree
    bs = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    bs.inputs['Roughness'].default_value = 0.8
    tc = nt.nodes.new('ShaderNodeTexCoord')
    nz = nt.nodes.new('ShaderNodeTexNoise')
    nz.inputs['Scale'].default_value = 3.0
    nz.inputs['Detail'].default_value = 6.0
    nt.links.new(tc.outputs['Object'], nz.inputs['Vector'])
    rr = nt.nodes.new('ShaderNodeValToRGB')
    rr.color_ramp.elements[0].color = hexcol(rock_a)
    rr.color_ramp.elements[1].color = hexcol(rock_b)
    nt.links.new(nz.outputs['Fac'], rr.inputs['Fac'])
    sep = nt.nodes.new('ShaderNodeSeparateXYZ')
    nt.links.new(tc.outputs['Object'], sep.inputs['Vector'])
    add = nt.nodes.new('ShaderNodeMath')
    add.operation = 'MULTIPLY_ADD'
    nt.links.new(nz.outputs['Fac'], add.inputs[0])
    add.inputs[1].default_value = 0.9
    nt.links.new(sep.outputs['Z'], add.inputs[2])
    st = nt.nodes.new('ShaderNodeMath')
    st.operation = 'GREATER_THAN'
    st.inputs[1].default_value = line + 0.45
    nt.links.new(add.outputs[0], st.inputs[0])
    mix = nt.nodes.new('ShaderNodeMix')
    mix.data_type = 'RGBA'
    nt.links.new(st.outputs[0], mix.inputs['Factor'])
    nt.links.new(rr.outputs['Color'], mix.inputs['A'])
    mix.inputs['B'].default_value = hexcol(snow)
    nt.links.new(mix.outputs['Result'], bs.inputs['Base Color'])
    bp = nt.nodes.new('ShaderNodeBump')
    bp.inputs['Strength'].default_value = 0.35
    nt.links.new(nz.outputs['Fac'], bp.inputs['Height'])
    nt.links.new(bp.outputs['Normal'], bs.inputs['Normal'])
    return m


# ───────── 모양 ─────────

def blob(name, r, material, loc=(0, 0, 0), scale=(1, 1, 1), sub=3, jit=0.18, seed=0, smooth=True, flat_bottom=None, parent=None, rot=(0, 0, 0)):
    """잡음으로 일그러뜨린 공(잎 뭉치·바위)."""
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=sub, radius=r)
    off = Vector((seed * 7.13, seed * 3.71, seed * 1.37))
    for v in bm.verts:
        n = noise.noise(v.co * (2.2 / r) + off)
        v.co += v.co.normalized() * n * jit * r * 2
        if flat_bottom is not None and v.co.z < flat_bottom:
            v.co.z = flat_bottom
    return common._obj(name, bm, material, parent, loc, smooth, 0, rot, scale)


def cyl(name, r1, r2, h, material, loc=(0, 0, 0), rot=(0, 0, 0), seg=10, parent=None, scale=(1, 1, 1), smooth=True):
    """밑이 loc인 원기둥(위로 h)."""
    return cone(name, r1, r2, h, material, parent, loc, seg=seg, down=False, subsurf=0, rot=rot, scale=scale, round_ends=False)


def log(name, r, length, material, loc, yaw=0.0, tilt=0.0):
    """누운 통나무(길이 방향 = yaw 방향 수평). loc은 통나무 가운데."""
    o = cone(name, r, r * 0.92, length, material, None, loc, seg=10, down=False, subsurf=0, round_ends=False,
             rot=(0, math.radians(90) + tilt, yaw))
    # 가운데 정렬: 원기둥은 밑에서 위로 자라므로 반 길이만큼 뒤로
    d = Vector((math.cos(yaw), math.sin(yaw), 0)) * (length / 2)
    o.location = Vector(loc) - d
    return o


def thatch(name, r, h, loc, material, seg=18, rough=0.06, seed=1):
    """우툴두툴한 원뿔 지붕."""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r, radius2=0.02, depth=h)
    bmesh.ops.translate(bm, verts=bm.verts, vec=(0, 0, h / 2))
    bmesh.ops.subdivide_edges(bm, edges=[e for e in bm.edges if abs(e.verts[0].co.z - e.verts[1].co.z) > 0.01], cuts=4)
    off = Vector((seed, seed * 2, 0))
    for v in bm.verts:
        if v.co.z > 0.01:
            v.co.x += noise.noise(v.co * 4 + off) * rough
            v.co.y += noise.noise(v.co * 4 + off + Vector((5, 0, 0))) * rough
    o = common._obj(name, bm, material, None, loc, True, 1, (0, 0, 0), (1, 1, 1))
    return o


def gable(name, w, d, h, loc, material, over=0.15, thick=0.08):
    """박공지붕: 앞뒤(y)로 기운 두 판. w=가로(x), d=깊이(y)."""
    half = d / 2 + over
    slope = math.atan2(h, half)
    ln = math.hypot(h, half)
    for s in (-1, 1):
        box(name, (w + over * 2, ln, thick), material, None,
            (loc[0], loc[1] + s * half / 2, loc[2] + h / 2), rot=(-s * slope, 0, 0), bevel=0.02)
    # 양옆 박공벽
    bm = bmesh.new()
    vs = [bm.verts.new(c) for c in ((0, -d / 2, 0), (0, d / 2, 0), (0, 0, h * 0.92))]
    bm.faces.new(vs)
    sol = bmesh.ops.extrude_face_region(bm, geom=list(bm.faces))
    bmesh.ops.translate(bm, verts=[e for e in sol['geom'] if isinstance(e, bmesh.types.BMVert)], vec=(w, 0, 0))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return common._obj(name + 'end', bm, mat('8a6a48', 0.85), None, (loc[0] - w / 2, loc[1], loc[2]), False)


def stakes(n, radius, h, material, arc=(0, 360), cx=0.0, cy=0.0, r=0.07, seed=3, gate=None):
    rnd = random.Random(seed)
    out = []
    for i in range(n):
        a = math.radians(arc[0] + (arc[1] - arc[0]) * (i + 0.5) / n)
        if gate and gate[0] <= math.degrees(a) % 360 <= gate[1]:
            continue
        hh = h * rnd.uniform(0.85, 1.1)
        p = (cx + math.cos(a) * radius, cy + math.sin(a) * radius, 0)
        o = cyl('stake', r, r * 0.9, hh, material, p, seg=8)
        cone('tip', r * 0.9, 0.0, r * 2.6, material, None, (p[0], p[1], hh), seg=8, down=False, subsurf=0, round_ends=False)
        o.rotation_euler = (rnd.uniform(-0.05, 0.05), rnd.uniform(-0.05, 0.05), 0)
        out.append(o)
    return out


def ground_patch(r, material, scale=(1, 1, 1), loc=(0, 0, 0), h=0.04):
    return cyl('patch', r, r * 0.97, h, material, loc, seg=32, scale=scale)


# 공통 재질
def M():
    return dict(
        bark=nmat('5a3e28', '7a5636', 18, 0.9),
        wood=nmat('8a6440', 'a07a50', 10, 0.8),
        darkwood=nmat('4e3624', '6a4a30', 10, 0.8),
        thatch=nmat('9a7638', 'd9b86a', 22, 0.95, bump=0.8, mix=(0.3, 0.75)),
        hide=nmat('b08a62', 'c9a77c', 5, 0.9, bump=0.15),
        leaf=nmat('3f6b33', '6e9a45', 5, 0.8, bump=0.4),
        leaf2=nmat('4a7a3a', '8aae55', 5, 0.8, bump=0.4),
        pine=nmat('1f4a32', '3b6b45', 7, 0.85, bump=0.4),
        rock=nmat('7d7a74', 'a9a49a', 4, 0.85, bump=0.5),
        darkrock=nmat('5e5b57', '86827a', 4, 0.85, bump=0.5),
        soil=nmat('5c4028', '7a5636', 9, 0.95, bump=0.3),
        grass=nmat('6f9a48', '93b860', 6, 0.9, bump=0.3),
        wheat=nmat('c9a540', 'e8cc6a', 20, 0.8),
        water=mat('3f8fa3', 0.08),
        berry=mat('c2243c', 0.25),
        rope=mat('9a7a52', 0.9),
        red=nmat('8a2f24', 'a8452f', 8, 0.85),
        fire=mat('ffa040', 0.5, emit=6.0),
        bone=mat('e8dcc0', 0.6),
    )


# ───────── 사물 ─────────

def tree(m, seed=0, tall=1.0):
    rnd = random.Random(seed)
    cyl('trunk', 0.2, 0.12, 2.0 * tall, m['bark'], seg=9)
    for a in (40, 160, 280):
        cone('root', 0.1, 0.02, 0.5, m['bark'], None, (0, 0, 0.15), seg=6, down=False, subsurf=0,
             rot=(math.radians(70), 0, math.radians(a)))
    leaf = m['leaf'] if seed % 2 == 0 else m['leaf2']
    blob('crown', 1.05, leaf, (0, 0, 2.6 * tall), (1, 1, 0.85), seed=seed, jit=0.12)
    for i in range(4):
        a = rnd.uniform(0, math.tau)
        blob('crown', rnd.uniform(0.55, 0.75), leaf, (math.cos(a) * 0.75, math.sin(a) * 0.75, 2.35 * tall + rnd.uniform(-0.2, 0.4)),
             seed=seed + i + 1, jit=0.14)
    blob('crown', 0.6, leaf, (0.1, 0.1, 3.3 * tall), seed=seed + 9, jit=0.14)


def pine(m, seed=0, tall=1.0):
    rnd = random.Random(seed)
    cyl('trunk', 0.16, 0.1, 1.2, m['bark'], seg=8)
    layers = 4
    for i in range(layers):
        r = 1.2 * (1 - i / (layers + 0.6))
        z = (0.7 + i * 0.75) * tall
        thatch('needles', r, 1.25 * tall, (0, 0, z), m['pine'], seg=14, rough=0.09, seed=seed * 10 + i)


def peak(m, seed=0):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=11, radius1=2.6, radius2=0.12, depth=4.2)
    bmesh.ops.translate(bm, verts=bm.verts, vec=(0, 0, 2.1))
    bmesh.ops.subdivide_edges(bm, edges=list(bm.edges), cuts=3)
    off = Vector((seed, 0, 0))
    for v in bm.verts:
        if v.co.z > 0.05:
            k = v.co.z / 4.2
            v.co.x += noise.noise(v.co * 0.9 + off) * 0.7 * (1 - k * 0.5)
            v.co.y += noise.noise(v.co * 0.9 + off + Vector((9, 0, 0))) * 0.7 * (1 - k * 0.5)
            v.co.z += noise.noise(v.co * 1.3 + off + Vector((0, 9, 0))) * 0.35
    common._obj('peak', bm, snowmat('6f6a64', '9a948a', 'f2f4f6', 2.4), None, (0, 0, 0), False)
    # 곁봉우리
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=9, radius1=1.5, radius2=0.1, depth=2.4)
    bmesh.ops.translate(bm, verts=bm.verts, vec=(0, 0, 1.2))
    bmesh.ops.subdivide_edges(bm, edges=list(bm.edges), cuts=2)
    for v in bm.verts:
        if v.co.z > 0.05:
            v.co.x += noise.noise(v.co * 1.2 + off + Vector((3, 3, 0))) * 0.4
            v.co.y += noise.noise(v.co * 1.2 + off + Vector((6, 1, 0))) * 0.4
    common._obj('peak2', bm, snowmat('6f6a64', '9a948a', 'f2f4f6', 1.5), None, (1.9, -0.6, 0), False)
    for i in range(3):
        blob('scree', 0.35, m['darkrock'], (-1.8 + i * 0.5, -1.4 + i * 0.2, 0.1), (1, 1, 0.6), sub=1, seed=seed + i, smooth=False)


def hill(m, seed=0):
    """둥근 풀 언덕 두셋이 겹치고 바위가 드러난 언덕."""
    g = nmat('4a6e30', '6f8c42', 2.5, 0.95, bump=0.7)
    blob('hill', 1.0, g, (0, 0.2, 0), (2.0, 1.5, 1.25), sub=4, seed=seed, jit=0.08, flat_bottom=0)
    blob('hill', 0.8, g, (1.2, -0.3, 0), (1.3, 1.1, 0.9), sub=4, seed=seed + 3, jit=0.08, flat_bottom=0)
    blob('hill', 0.7, g, (-1.3, -0.2, 0), (1.3, 1.0, 0.75), sub=4, seed=seed + 5, jit=0.08, flat_bottom=0)
    blob('rock', 0.32, m['rock'], (0.45, -0.95, 0.55), (1, 0.8, 0.8), sub=1, seed=seed, smooth=False)
    blob('rock', 0.22, m['darkrock'], (0.85, -0.85, 0.35), (1, 0.8, 0.8), sub=1, seed=seed + 2, smooth=False)
    blob('rock', 0.25, m['rock'], (-0.6, 0.4, 1.15), (1, 0.8, 0.7), sub=1, seed=seed + 4, smooth=False)
    for i, (x, y) in enumerate(((-1.6, -0.8), (-0.9, -1.0), (1.9, -0.7), (0.0, -1.25))):
        blob('tuft', 0.16, m['grass'], (x, y, 0.05), (1, 1, 1.2), sub=1, seed=i, jit=0.3)


def bush(m, seed=0, berries=True):
    rnd = random.Random(seed)
    for i in range(5):
        a = i * 1.3
        blob('bush', rnd.uniform(0.38, 0.5), m['leaf'], (math.cos(a) * 0.35, math.sin(a) * 0.3, 0.42 + rnd.uniform(-0.05, 0.1)), seed=seed + i, jit=0.12)
    if berries:
        for i in range(26):
            a = rnd.uniform(0, math.tau)
            z = rnd.uniform(0.2, 0.95)
            rr = 0.7 - abs(z - 0.55) * 0.4
            sphere('berry', 0.085, m['berry'], None, (math.cos(a) * rr, math.sin(a) * rr - 0.05, z), seg=10, rings=6)


def deer(m):
    fur, belly, dark = nmat('8a5a32', 'a8723f', 12, 0.9, bump=0.1), mat('d8c3a0', 0.9), mat('2a1d14', 0.5)
    root = empty('deer')
    blob('body', 0.42, fur, (0, 0, 0.95), (1.55, 0.8, 0.85), sub=2, jit=0.03, parent=root)
    blob('rump', 0.2, belly, (-0.62, 0, 1.0), (0.6, 0.9, 0.8), sub=2, jit=0.02, parent=root)
    cone('neck', 0.17, 0.12, 0.55, fur, root, (0.5, 0, 1.08), down=False, rot=(0, math.radians(35), 0), seg=10, subsurf=1)
    head = empty('head', root, (0.85, 0, 1.6))
    blob('skull', 0.2, fur, (0, 0, 0), (1.3, 0.85, 0.9), sub=2, jit=0.02, parent=head)
    blob('snout', 0.1, fur, (0.2, 0, -0.05), (1.2, 0.8, 0.8), sub=2, jit=0.02, parent=head)
    sphere('nose', 0.04, dark, head, (0.31, 0, -0.04))
    for s in (-1, 1):
        sphere('eye', 0.028, dark, head, (0.1, s * 0.13, 0.04))
        sphere('ear', 0.08, fur, head, (-0.08, s * 0.15, 0.12), scale=(0.5, 1.2, 0.5), rot=(s * 0.6, 0, 0))
        # 뿔
        a = cone('antler', 0.025, 0.015, 0.45, m['bone'], head, (-0.02, s * 0.07, 0.12), down=False, subsurf=0,
                 rot=(-s * math.radians(25), math.radians(-20), 0), seg=6)
        cone('tine', 0.018, 0.008, 0.22, m['bone'], a, (0, 0, 0.22), down=False, subsurf=0, rot=(0, math.radians(40), 0), seg=6)
        cone('tine', 0.018, 0.008, 0.2, m['bone'], a, (0, 0, 0.38), down=False, subsurf=0, rot=(-s * 0.5, math.radians(-35), 0), seg=6)
    for x, y, sw in ((0.42, 0.17, 10), (0.42, -0.17, -14), (-0.48, 0.17, -8), (-0.48, -0.17, 12)):
        leg = empty('leg', root, (x, y, 0.85))
        leg.rotation_euler = (0, math.radians(sw), 0)
        cone('thigh', 0.1, 0.05, 0.45, fur, leg, (0, 0, 0), seg=8)
        cone('shin', 0.045, 0.035, 0.42, fur, leg, (0, 0, -0.43), seg=8)
        sphere('hoof', 0.045, dark, leg, (0.02, 0, -0.85))
    cone('tail', 0.06, 0.02, 0.18, belly, root, (-0.72, 0, 1.12), down=False, rot=(0, math.radians(-60), 0), seg=6)
    root.rotation_euler = (0, 0, math.radians(-150))
    root.scale = (0.9, 0.9, 0.9)


def fish(m):
    patch = cyl('pond', 1.05, 1.0, 0.03, m['water'], seg=40, scale=(1, 0.8, 1))
    torus('ripple', 0.5, 0.025, mat('bfe6ee', 0.2), None, (0.15, -0.05, 0.04), scale=(1, 0.75, 0.3))
    torus('ripple', 0.8, 0.02, mat('9fd6e2', 0.2), None, (0.15, -0.05, 0.04), scale=(1, 0.75, 0.3))
    scale = nmat('6f8f9a', 'b8c9cc', 14, 0.3, bump=0.1)
    for (x, z, ry, s) in ((0.1, 0.65, -40, 1.0), (-0.45, 0.35, 30, 0.7)):
        f = empty('fish', None, (x, 0, z))
        f.rotation_euler = (0, math.radians(ry), math.radians(-10))
        f.scale = (s, s, s)
        blob('fbody', 0.22, scale, (0, 0, 0), (1.7, 0.55, 0.75), sub=2, jit=0.02, parent=f)
        cone('ftail', 0.0, 0.17, 0.25, scale, f, (-0.32, 0, 0), down=False, rot=(0, math.radians(-90), 0), seg=4, subsurf=0,
             scale=(0.3, 1, 1), round_ends=False)
        sphere('feye', 0.03, mat('101010', 0.2), f, (0.25, 0.08, 0.04))
        cone('fin', 0.0, 0.08, 0.12, scale, f, (0, 0, 0.12), down=False, seg=4, subsurf=0, scale=(1, 0.2, 1), round_ends=False)
    for i in range(6):
        sphere('drop', 0.035, mat('cfeef4', 0.1), None, (0.4 + i * 0.07, 0.0, 0.3 + (i % 3) * 0.15))


def stones(m, seed=0, n=5):
    rnd = random.Random(seed)
    spots = [(0, 0, 0.55), (0.6, -0.3, 0.38), (-0.55, -0.2, 0.42), (0.25, 0.45, 0.35), (-0.2, -0.55, 0.28), (0.75, 0.35, 0.25)]
    for i, (x, y, r) in enumerate(spots[:n]):
        blob('rock', r, m['rock'] if i % 2 == 0 else m['darkrock'], (x, y, r * 0.55), (1, 0.9, 0.75), sub=1, seed=seed + i,
             jit=0.25, smooth=False, flat_bottom=-r * 0.4)


def sheaves(m):
    ground_patch(1.1, m['soil'], (1, 0.85, 1))
    rnd = random.Random(5)
    for i in range(60):
        a = rnd.uniform(0, math.tau)
        rr = math.sqrt(rnd.random()) * 0.95
        x, y = math.cos(a) * rr, math.sin(a) * rr * 0.85
        h = rnd.uniform(0.55, 0.8)
        cyl('stalk', 0.015, 0.012, h, mat('a88a3a', 0.8), (x, y, 0.04), seg=4,
            rot=(rnd.uniform(-0.15, 0.15), rnd.uniform(-0.15, 0.15), 0))
        blob('ear', 0.06, m['wheat'], (x, y, h + 0.06), (0.7, 0.7, 1.8), sub=1, jit=0.05, seed=i)


def field(m):
    ground_patch(1.8, m['soil'], (1, 0.82, 1))
    rnd = random.Random(2)
    for row in range(-4, 5):
        y = row * 0.32
        cyl('ridge', 0.08, 0.06, 0.08, m['soil'], (0, y, 0.03), scale=(1.55 * math.sqrt(max(0.05, 1 - (y / 1.5) ** 2)) / 0.08, 1, 1), seg=12)
        span = 1.6 * math.sqrt(max(0.0, 1 - (y / 1.5) ** 2))
        x = -span
        while x < span:
            h = rnd.uniform(0.35, 0.55)
            cyl('stalk', 0.012, 0.01, h, mat('9a8a3a', 0.8), (x, y, 0.08), seg=4, rot=(rnd.uniform(-0.2, 0.2), rnd.uniform(-0.2, 0.2), 0))
            blob('ear', 0.05, m['wheat'], (x, y, h + 0.1), (0.7, 0.7, 1.7), sub=1, jit=0.05, seed=int(x * 100) + row)
            x += rnd.uniform(0.11, 0.16)
    # 귀퉁이 말뚝
    for x, y in ((-1.7, -0.2), (1.7, 0.2)):
        cyl('post', 0.05, 0.045, 0.6, m['wood'], (x, y, 0), seg=6)


def hut(m):
    cyl('wall', 1.0, 0.95, 0.85, nmat('8a6844', 'a88458', 30, 0.9, bump=0.5), seg=20)
    thatch('roof', 1.35, 1.5, (0, 0, 0.75), m['thatch'], seed=2)
    for a in (60, 100, 140):
        cone('pole', 0.03, 0.02, 0.45, m['darkwood'], None, (0, 0, 2.05), down=False, subsurf=0, round_ends=False,
             rot=(math.radians(25), 0, math.radians(a)), seg=5)
    box('door', (0.45, 0.1, 0.65), mat('2a1d14', 0.6), None, (0, -0.95, 0.33), bevel=0.04)
    box('lintel', (0.6, 0.12, 0.08), m['darkwood'], None, (0, -0.98, 0.68))
    # 장작과 항아리
    for i in range(3):
        log('fw', 0.07, 0.6, m['bark'], (0.95, -0.6 + i * 0.02, 0.07 + i * 0.12), yaw=math.radians(60))
    cyl('pot', 0.15, 0.12, 0.3, nmat('a0603a', 'b87a4a', 8, 0.7), (-0.85, -0.75, 0), seg=14)


def tipi(m, loc=(0, 0, 0), r=0.95, h=2.3, seed=0):
    thatch('tent', r, h, loc, m['hide'], seg=12, rough=0.03, seed=seed)
    for a in range(0, 360, 60):
        cone('pole', 0.025, 0.02, 0.5, m['darkwood'], None, (loc[0], loc[1], loc[2] + h - 0.15), down=False, subsurf=0,
             round_ends=False, rot=(math.radians(25), 0, math.radians(a)), seg=5)
    box('flap', (0.4, 0.08, 0.7), mat('3a2a1e', 0.7), None, (loc[0], loc[1] - r * 0.62, 0.35), rot=(math.radians(-22), 0, 0))


def lodge(m):
    tipi(m, (0.2, 0.2, 0), 1.0, 2.4, 1)
    # 가죽 말리는 틀
    for x in (-1.6, -0.6):
        cyl('post', 0.05, 0.045, 1.3, m['darkwood'], (x, -0.7, 0), seg=6)
    log('bar', 0.04, 1.1, m['darkwood'], (-1.1, -0.7, 1.25))
    box('pelt', (0.75, 0.04, 0.8), nmat('7a5234', '9a6a44', 6, 0.95, bump=0.3), None, (-1.1, -0.72, 0.8))
    # 모닥불
    for a in range(0, 360, 45):
        blob('ring', 0.09, m['darkrock'], (1.2 + math.cos(math.radians(a)) * 0.3, -0.7 + math.sin(math.radians(a)) * 0.3, 0.05), sub=1, seed=a, smooth=False)
    blob('fire', 0.12, m['fire'], (1.2, -0.7, 0.15), (1, 1, 1.6), sub=1, jit=0.1)


def lumber(m):
    # 통나무 더미
    rows = [(4, 0.0), (3, 0.2), (2, 0.39)]
    for n, z in rows:
        for i in range(n):
            log('log', 0.13, 1.9, m['bark'], (-0.4, -0.3 + (i - (n - 1) / 2) * 0.27, 0.13 + z), yaw=math.radians(10))
    # 그루터기와 도끼
    cyl('stump', 0.32, 0.28, 0.45, m['bark'], (1.15, 0.3, 0), seg=14)
    cyl('top', 0.27, 0.27, 0.02, nmat('c9a070', 'e0bc88', 30, 0.7), (1.15, 0.3, 0.45), seg=14)
    ax = cone('axe', 0.025, 0.025, 0.7, m['wood'], None, (1.05, 0.3, 0.42), down=False, subsurf=0, round_ends=False,
              rot=(0, math.radians(-50), 0), seg=6)
    box('axeh', (0.04, 0.2, 0.14), mat('8a8f94', 0.3, 0, 0.8), ax, (0, -0.0, 0.66), bevel=0.01)
    for i in range(5):
        a = i * 1.2
        box('chip', (0.12, 0.05, 0.03), nmat('c9a070', 'e0bc88', 30, 0.7), None,
            (1.15 + math.cos(a) * 0.55, 0.3 + math.sin(a) * 0.45, 0.02), rot=(0, 0, a))


def quarry(m):
    # 깎인 바위 벽
    blob('cliff', 1.0, m['darkrock'], (0.2, 0.8, 0.3), (1.4, 0.7, 0.9), sub=2, seed=4, jit=0.12, smooth=False, flat_bottom=-0.4)
    blob('cliff', 0.65, m['rock'], (-1.0, 0.7, 0.2), (1, 0.8, 1.0), sub=2, seed=7, jit=0.12, smooth=False, flat_bottom=-0.35)
    # 다듬은 돌덩이
    cut = nmat('b0aa9e', 'c9c2b4', 6, 0.8, bump=0.4)
    for (x, y, z, r) in ((0.7, -0.6, 0.18, 15), (1.25, -0.5, 0.18, -10), (0.95, -0.55, 0.53, 5), (-0.6, -0.7, 0.18, 30)):
        box('block', (0.5, 0.38, 0.36), cut, None, (x, y, z), rot=(0, 0, math.radians(r)), bevel=0.03)
    for i in range(6):
        blob('chip', 0.1, m['rock'], (-0.2 + i * 0.25, -0.2 - (i % 2) * 0.25, 0.04), sub=1, seed=i, smooth=False)
    pk = cone('pick', 0.025, 0.025, 0.8, m['wood'], None, (-0.3, -0.25, 0.06), down=False, subsurf=0, round_ends=False,
              rot=(math.radians(80), 0, math.radians(20)), seg=6)


def granary(m):
    for x, y in ((-0.55, -0.55), (0.55, -0.55), (-0.55, 0.55), (0.55, 0.55)):
        cyl('stilt', 0.08, 0.07, 0.6, m['darkwood'], (x, y, 0), seg=8)
        cyl('cap', 0.14, 0.14, 0.06, m['rock'], (x, y, 0.58), seg=10)
    cyl('floor', 0.95, 0.95, 0.1, m['wood'], (0, 0, 0.64), seg=20)
    cyl('body', 0.82, 0.88, 0.95, nmat('a88458', 'c4a070', 25, 0.9, bump=0.5), (0, 0, 0.74), seg=20)
    thatch('roof', 1.18, 1.25, (0, 0, 1.6), m['thatch'], seed=5)
    # 사다리
    for s in (-1, 1):
        cyl('rail', 0.03, 0.03, 1.15, m['wood'], (s * 0.2, -1.15, 0), rot=(math.radians(-22), 0, 0), seg=6)
    for i in range(4):
        box('rung', (0.42, 0.04, 0.04), m['wood'], None, (0, -1.13 + i * 0.1, 0.2 + i * 0.25))
    for i, (x, y) in enumerate(((0.9, -0.8), (1.15, -0.5))):
        blob('sack', 0.22, nmat('c2a172', 'd8bb8a', 12, 0.95), (x, y, 0.2), (0.9, 0.9, 1.1), sub=2, jit=0.06, seed=i)


def storehouse(m):
    box('wall', (2.2, 1.4, 0.95), nmat('7a5636', '9a7048', 18, 0.9, bump=0.4), None, (0, 0.2, 0.48), bevel=0.03)
    for x in (-1.1, -0.36, 0.36, 1.1):
        box('post', (0.12, 0.12, 1.0), m['darkwood'], None, (x, -0.52, 0.5))
    gable('roof', 2.2, 1.4, 1.15, (0, 0.2, 0.95), m['thatch'], over=0.25, thick=0.14)
    log('ridge', 0.08, 2.75, m['darkwood'], (0, 0.2, 2.12))
    box('door', (0.55, 0.06, 0.75), mat('2a1d14', 0.6), None, (0, -0.52, 0.38))
    sack = nmat('c2a172', 'd8bb8a', 12, 0.95)
    for i, (x, y) in enumerate(((-1.45, -0.8), (-1.05, -0.95), (-1.25, -0.55))):
        blob('sack', 0.24, sack, (x, y, 0.22), (0.9, 0.9, 1.1), sub=2, jit=0.06, seed=i)
    basket = cyl('basket', 0.22, 0.28, 0.3, nmat('a07840', 'c49a58', 30, 0.9, bump=0.5), (1.3, -0.85, 0), seg=16)
    for i in range(7):
        a = i * 0.9
        sphere('berry', 0.07, m['berry'], None, (1.3 + math.cos(a) * 0.12, -0.85 + math.sin(a) * 0.12, 0.32), seg=8, rings=6)
    for i in range(3):
        log('fw', 0.08, 0.7, m['bark'], (0.9, -1.15, 0.08 + i * 0.13), yaw=math.radians(-15))


def palisade(m):
    stakes(22, 1.55, 1.25, nmat('6a4a2e', '8a6440', 14, 0.85), arc=(0, 360), r=0.09, gate=(255, 285))
    for s in (-1, 1):
        cyl('gatepost', 0.12, 0.11, 1.7, m['darkwood'], (s * 0.42, -1.55, 0), seg=8)
    log('gatebar', 0.07, 1.1, m['darkwood'], (0, -1.55, 1.6))


def shrine(m):
    ground_patch(1.3, nmat('8a8072', 'a59a88', 5, 0.95), (1, 1, 1), h=0.05)
    for a in range(0, 360, 45):
        r = math.radians(a + 22)
        blob('menhir', 0.22, m['rock'], (math.cos(r) * 1.05, math.sin(r) * 1.05, 0.35), (0.8, 0.6, 1.9), sub=1, seed=a, smooth=False)
    # 가운데 기둥과 불
    blob('stele', 0.3, m['darkrock'], (0, 0.35, 0.8), (0.9, 0.55, 2.8), sub=2, seed=11, jit=0.06, smooth=False)
    torus('band', 0.27, 0.035, m['red'], None, (0, 0.35, 1.2), scale=(1, 0.65, 1))
    cyl('bowl', 0.22, 0.3, 0.2, m['darkrock'], (0, -0.35, 0.05), seg=14)
    blob('flame', 0.16, m['fire'], (0, -0.35, 0.35), (1, 1, 1.7), sub=1, jit=0.12)
    for i in range(3):
        sphere('offer', 0.07, m['berry'] if i != 1 else m['wheat'], None, (-0.35 + i * 0.35, -0.75, 0.08), seg=8, rings=6)


def tower(m):
    for x, y in ((-0.5, -0.5), (0.5, -0.5), (-0.5, 0.5), (0.5, 0.5)):
        cyl('leg', 0.09, 0.07, 2.6, m['darkwood'], (x, y, 0), rot=(y * -0.06, x * 0.06, 0), seg=8)
    for z in (0.8, 1.6):
        for (a, b) in (((-0.5, -0.5), (0.5, -0.5)), ((-0.5, 0.5), (0.5, 0.5))):
            log('brace', 0.04, 1.05, m['wood'], ((a[0] + b[0]) / 2, a[1], z))
    box('deck', (1.5, 1.5, 0.12), m['wood'], None, (0, 0, 2.6), bevel=0.02)
    stakes(14, 0.68, 0.5, m['wood'], cx=0, cy=0, r=0.05)
    for o in bpy.context.scene.objects:
        if o.name.startswith('stake') or o.name.startswith('tip'):
            if o.location.z < 1.0:
                o.location.z += 2.66
    for x, y in ((-0.6, -0.6), (0.6, -0.6), (-0.6, 0.6), (0.6, 0.6)):
        cyl('roofpost', 0.04, 0.04, 0.9, m['darkwood'], (x, y, 2.66), seg=6)
    thatch('roof', 1.15, 0.8, (0, 0, 3.5), m['thatch'], seg=4, rough=0.03, seed=3)
    for o in bpy.context.scene.objects:
        if o.name.startswith('roof') and o.type == 'MESH' and o.location.z > 3.4:
            o.rotation_euler = (0, 0, math.radians(45))
    cyl('flagpole', 0.025, 0.02, 0.8, m['darkwood'], (0, 0, 4.2), seg=5)
    box('flag', (0.45, 0.02, 0.28), m['red'], None, (0.24, 0, 4.85))


def warhut(m):
    box('wall', (2.3, 1.3, 0.9), nmat('6a4a2e', '8a6440', 14, 0.85, bump=0.4), None, (0, 0.2, 0.45), bevel=0.03)
    gable('roof', 2.3, 1.3, 1.1, (0, 0.2, 0.9), nmat('6a5a3a', '8a7448', 22, 0.95, bump=0.6), over=0.25, thick=0.12)
    box('door', (0.5, 0.06, 0.7), mat('1e1610', 0.6), None, (0, -0.47, 0.35))
    shield = nmat('8a2f24', 'b04a35', 8, 0.7)
    for x in (-0.75, 0.75):
        cyl('shield', 0.28, 0.28, 0.06, shield, (x, -0.47, 0.5), rot=(math.radians(90), 0, 0), seg=18)
        sphere('boss', 0.07, mat('8a8f94', 0.3, 0, 0.8), None, (x, -0.55, 0.5))
    # 창 걸이
    for i in range(3):
        sp = cone('spear', 0.02, 0.02, 1.9, m['wood'], None, (1.45 + i * 0.12, -0.5, 0), down=False, subsurf=0, round_ends=False,
                  rot=(0, math.radians(-12 + i * 6), 0), seg=6)
        cone('tip', 0.045, 0.0, 0.2, mat('5d6266', 0.4, 0, 0.5), sp, (0, 0, 1.9), down=False, subsurf=0, round_ends=False, seg=6)
    cyl('banner', 0.03, 0.03, 2.4, m['darkwood'], (-1.4, -0.4, 0), seg=6)
    box('cloth', (0.05, 0.5, 0.75), m['red'], None, (-1.4, -0.66, 1.9))
    for i in range(6):
        a = math.radians(i * 60)
        blob('fs', 0.08, m['darkrock'], (-0.6 + math.cos(a) * 0.25, -1.1 + math.sin(a) * 0.25, 0.04), sub=1, seed=i, smooth=False)


def weir(m):
    cyl('stream', 1.7, 1.65, 0.03, m['water'], seg=40, scale=(1, 0.75, 1))
    for i, (x, y) in enumerate([(-1.3 + k * 0.2, -0.3 + abs(k - 6) * 0.05) for k in range(13)]):
        cyl('stake', 0.04, 0.035, 0.7 + (i % 2) * 0.12, m['darkwood'], (x, y, 0), seg=6)
    for z in (0.2, 0.42):
        for k in range(12):
            x = -1.3 + k * 0.2 + 0.1
            y = -0.3 + abs(k - 5.5) * 0.05
            log('weave', 0.025, 0.22, m['wood'], (x, y, z), yaw=math.radians(-14 if k < 6 else 14))
    # 통발
    trap = cone('trap', 0.25, 0.1, 0.8, nmat('a07840', 'c49a58', 30, 0.9, bump=0.5), None, (0.4, -0.75, 0.25), down=False,
                rot=(0, math.radians(90), math.radians(20)), seg=14, subsurf=0, round_ends=False)
    blob('fishin', 0.1, mat('8fa3a8', 0.3), (0.3, 0.6, 0.25), (1.6, 0.6, 0.6), sub=2, jit=0.02)
    torus('ripple', 0.35, 0.02, mat('bfe6ee', 0.2), None, (-0.6, 0.45, 0.04), scale=(1, 0.75, 0.3))


def spin(fn, deg):
    """다 지은 뒤 통째로 돌려 옆면(박공)이 보이게 한다."""
    def run(m):
        fn(m)
        piv = empty('pivot')
        for o in list(bpy.context.scene.objects):
            if o.parent is None and o is not piv and o.name not in ('catcher', 'cam', 'sun'):
                o.parent = piv
        piv.rotation_euler = (0, 0, math.radians(deg))
    return run


# 이름 → (만드는 함수, 사진기 폭(m), 저장 경로)
JOBS = {
    'tree':  (lambda m: tree(m, 0), 6.2, 'decor/tree.png'),
    'tree2': (lambda m: tree(m, 3, 0.9), 6.2, 'decor/tree2.png'),
    'pine':  (lambda m: pine(m, 1), 6.2, 'decor/pine.png'),
    'pine2': (lambda m: pine(m, 4, 0.85), 6.2, 'decor/pine2.png'),
    'peak':  (lambda m: peak(m, 2), 9.6, 'decor/peak.png'),
    'hill':  (lambda m: hill(m, 1), 6.4, 'decor/hill.png'),
    'Game':     (deer, 3.4, 'features/Game.png'),
    'Berries':  (lambda m: bush(m, 2), 2.6, 'features/Berries.png'),
    'Fish':     (fish, 3.0, 'features/Fish.png'),
    'Stone':    (lambda m: stones(m, 3), 2.8, 'features/Stone.png'),
    'Fertile':  (sheaves, 3.0, 'features/Fertile.png'),
    'Hut':        (hut, 4.4, 'structures/Hut.png'),
    'Lodge':      (lodge, 4.6, 'structures/Lodge.png'),
    'Lumber':     (spin(lumber, -20), 4.4, 'structures/Lumber.png'),
    'Quarry':     (quarry, 4.4, 'structures/Quarry.png'),
    'Field':      (field, 4.4, 'structures/Field.png'),
    'Granary':    (granary, 4.4, 'structures/Granary.png'),
    'Storehouse': (spin(storehouse, -28), 4.4, 'structures/Storehouse.png'),
    'Palisade':   (palisade, 4.4, 'structures/Palisade.png'),
    'Shrine':     (shrine, 4.4, 'structures/Shrine.png'),
    'Tower':      (tower, 5.6, 'structures/Tower.png'),
    'WarHut':     (spin(warhut, 28), 4.6, 'structures/WarHut.png'),
    'Weir':       (weir, 4.4, 'structures/Weir.png'),
}


def make(name, preview=False):
    fn, ortho, rel = JOBS[name]
    reset()
    common._mats.clear()
    lights()
    camera(PX, ortho, GROUND)
    fn(M())
    os.makedirs(TMP, exist_ok=True)
    raw = os.path.join(TMP, name + '_raw.png')
    render_to(raw)
    img = outline(load_rgba(raw), px=2, alpha=0.7)
    out = os.path.join(TMP, name + '.png') if preview else os.path.join(ASSETS, rel)
    os.makedirs(os.path.dirname(out), exist_ok=True)
    save_rgba(img, out)
    print('WROTE', out)


if __name__ == '__main__':
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    preview = bool(argv) and argv[0] == 'preview'
    names = [a for a in argv if a != 'preview'] or list(JOBS)
    for n in names:
        make(n, preview)
