"""빠진 게임 자산 보완: 수레(cart.png), 군단(army_own/enemy.png), 결정 창 삽화(events/*.png), 창 아이콘(icon.png).
실행:
  blender -b --python tools/blender/extras.py -- [preview] [이름...]
"""
import bpy, bmesh, math, os, sys, random
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import importlib, common, env, town, people
importlib.reload(common)
importlib.reload(env)
importlib.reload(town)
importlib.reload(people)
from common import *
from env import nmat, blob, cyl, log, thatch, gable, stakes, ground_patch, M, TMP, ASSETS
from town import place, flag, wall_ring, house, fire


def cam_rect(w_px, h_px, ortho, ground_px):
    sc = bpy.context.scene
    sc.render.resolution_x = w_px
    sc.render.resolution_y = h_px
    cam = bpy.data.cameras.new("cam")
    cam.type = 'ORTHO'
    cam.ortho_scale = ortho
    cam.shift_y = (ground_px - h_px / 2) / max(w_px, h_px)
    cam.clip_end = 200
    o = bpy.data.objects.new("cam", cam)
    e = math.radians(ELEV)
    o.location = (0, -40 * math.cos(e), 40 * math.sin(e))
    o.rotation_euler = (math.radians(90 - ELEV), 0, 0)
    sc.collection.objects.link(o)
    sc.camera = o
    bpy.ops.mesh.primitive_plane_add(size=60)
    g = bpy.context.active_object
    g.name = "catcher"
    g.is_shadow_catcher = True
    return o


# ───────── 1. 소가 끄는 수레 4프레임 띠 (units/cart.png, 512×128, 한 틀 128×128) ─────────

def make_ox(m, phase):
    fur = nmat('8a6240', 'a47850', 10, 0.9, bump=0.15)
    dark = mat('2a1d14', 0.5)
    horn = m['bone']
    ox = empty('ox')
    blob('body', 0.42, fur, (0, 0, 0.72), (1.45, 0.85, 0.85), sub=2, jit=0.03, parent=ox)
    blob('hump', 0.22, fur, (0.35, 0, 0.95), (1.0, 0.8, 0.8), sub=2, jit=0.03, parent=ox)
    head = empty('head', ox, (0.72, 0, 0.82))
    blob('skull', 0.22, fur, (0, 0, 0), (1.15, 0.9, 0.9), sub=2, jit=0.02, parent=head)
    blob('muzzle', 0.14, mat('c9a88c', 0.8), (0.18, 0, -0.06), (0.9, 1.0, 0.75), sub=2, jit=0.01, parent=head)
    for s in (-1, 1):
        sphere('eye', 0.028, dark, head, (0.08, s * 0.15, 0.05))
        cone('horn', 0.035, 0.01, 0.34, horn, head, (-0.02, s * 0.16, 0.1), down=False, subsurf=0,
             rot=(-s * math.radians(55), math.radians(15), 0), seg=8)
    legs_spec = ((0.38, 0.18, 0), (0.38, -0.18, math.pi), (-0.38, 0.18, math.pi), (-0.38, -0.18, 0))
    for x, y, off in legs_spec:
        sw = math.sin(phase + off) * 18
        leg = empty('leg', ox, (x, y, 0.62))
        leg.rotation_euler = (0, math.radians(sw), 0)
        cone('upper', 0.09, 0.06, 0.34, fur, leg, (0, 0, 0), seg=8)
        cone('lower', 0.055, 0.045, 0.3, fur, leg, (0, 0, -0.32), seg=8)
        sphere('hoof', 0.05, dark, leg, (0.01, 0, -0.62))
    # 멍에
    box('yoke', (0.1, 0.55, 0.08), m['darkwood'], ox, (0.48, 0, 0.98), bevel=0.01)
    return ox


def make_cart_scene(m, frame):
    phase = frame * math.tau / 4
    root = empty('cart_all')
    ox = make_ox(m, phase)
    ox.parent = root
    ox.location = (0.72, 0, 0)
    # 끌채
    for s in (-1, 1):
        box('shaft', (1.28, 0.05, 0.05), m['darkwood'], root, (0.12, s * 0.22, 0.58), rot=(0, math.radians(-10), 0))
    # 수레 몸통
    bed = empty('bed', root, (-0.72, 0, 0.48))
    box('floor', (1.2, 0.82, 0.08), m['wood'], bed, (0, 0, 0), bevel=0.01)
    for s in (-1, 1):
        box('side', (1.2, 0.06, 0.32), m['wood'], bed, (0, s * 0.39, 0.18), bevel=0.01)
    for x in (-0.57, 0.57):
        box('end', (0.06, 0.82, 0.28), m['wood'], bed, (x, 0, 0.16), bevel=0.01)
    # 짐 (곡식 자루 둘 + 상자)
    sack = nmat('c2a172', 'd8bb8a', 12, 0.95)
    blob('s1', 0.23, sack, (-0.9, 0.12, 0.72), (1.0, 0.85, 0.95), sub=2, jit=0.05, seed=1, parent=root)
    blob('s2', 0.21, sack, (-0.52, -0.14, 0.7), (0.95, 0.9, 0.9), sub=2, jit=0.05, seed=2, parent=root)
    box('crate', (0.35, 0.33, 0.28), nmat('9a6e42', 'b48454', 10, 0.85), root, (-0.48, 0.16, 0.68), bevel=0.02)
    # 바퀴 둘 (앞쪽이 잘 보이게)
    for s in (-1, 1):
        wh = empty('wheel', root, (-0.72, s * 0.46, 0.38))
        wh.rotation_euler = (0, phase, 0)
        torus('rim', 0.36, 0.045, m['darkwood'], wh, (0, 0, 0), rot=(math.radians(90), 0, 0))
        cyl('hub', 0.08, 0.08, 0.1, m['darkwood'], (0, -0.05, 0), rot=(math.radians(90), 0, 0), parent=wh, seg=10)
        for k in range(6):
            a = k * math.pi / 3
            box('spoke', (0.68, 0.035, 0.045), m['wood'], wh, (0, 0, 0), rot=(0, a, 0))
    # 오른쪽 아래(SE~E)로 살짝 비스듬히 보여 입체감이 살게
    root.location = (-0.18, 0.05, 0)
    root.rotation_euler = (0, 0, math.radians(-12))


def render_cart(preview=False):
    W, H = 128, 128
    strip = np.zeros((H, W * 4, 4), dtype=np.float32)
    os.makedirs(TMP, exist_ok=True)
    for f in range(4):
        reset()
        common._mats.clear()
        lights()
        cam_rect(W, H, 3.85, 102)
        make_cart_scene(M(), f)
        raw = os.path.join(TMP, f'cart_{f}.png')
        render_to(raw)
        strip[:, f * W:(f + 1) * W] = outline(load_rgba(raw), px=2, alpha=0.75)
    out = os.path.join(TMP if preview else os.path.join(ASSETS, 'units'), 'cart.png')
    os.makedirs(os.path.dirname(out), exist_ok=True)
    save_rgba(strip, out)
    print('WROTE', out)


# ───────── 2. 군단 (units/army_own.png, army_enemy.png, 256×256, 땅 원점 208) ─────────

def mini_soldier(m, loc, tunic_hex, shield_hex, yaw=0.0, spear_tilt=-15, banner=None):
    skin = mat('c98d68', 0.55, 0.2)
    tunic = mat(tunic_hex, 0.8, 0.3)
    steel = mat('9aa2a8', 0.3, 0, 0.75)
    dark = mat('3a2c22', 0.8)
    p = empty('sol', None, loc)
    p.rotation_euler = (0, 0, math.radians(yaw))
    for s in (-1, 1):
        cyl('leg', 0.075, 0.06, 0.46, dark, (s * 0.09, 0, 0), seg=8, parent=p)
    cyl('body', 0.2, 0.22, 0.58, tunic, (0, 0, 0.42), seg=12, scale=(1, 0.8, 1), parent=p)
    torus('belt', 0.19, 0.025, mat('2a2018', 0.6), p, (0, 0, 0.52), scale=(1, 0.8, 1))
    sphere('head', 0.17, skin, p, (0, 0, 1.16))
    sphere('helm', 0.19, steel, p, (0, 0, 1.19), keep=lambda c: c.z > -0.02)
    torus('hrim', 0.185, 0.018, steel, p, (0, 0, 1.18))
    # 왼손 방패
    sh = cyl('shield', 0.27, 0.27, 0.05, nmat(shield_hex, shield_hex, 4, 0.7, bump=0.1), (0.24, -0.14, 0.68),
             rot=(math.radians(82), 0, math.radians(20)), seg=16, parent=p)
    sphere('boss', 0.065, steel, p, (0.26, -0.19, 0.68))
    if banner:
        pole = cyl('bpole', 0.03, 0.025, 2.35, m['darkwood'], (-0.22, -0.05, 0), seg=6, parent=p)
        box('bcloth', (0.72, 0.04, 0.48), nmat(banner, banner, 3, 0.75, bump=0.05), p, (-0.22 + 0.38, -0.05, 2.02))
        box('btail', (0.28, 0.04, 0.34), mat('e8dcc0', 0.8), p, (-0.22 + 0.76, -0.05, 2.02))
    else:
        sp = cyl('spear', 0.022, 0.02, 1.85, m['wood'], (-0.23, -0.06, 0.15),
                 rot=(math.radians(spear_tilt), 0, 0), seg=6, parent=p)
        cone('stip', 0.05, 0.0, 0.22, steel, sp, (0, 0, 1.85), down=False, subsurf=0, round_ends=False, seg=6)
    return p


def make_army(m, own):
    tunic = '345888' if own else '9e3b2c'
    shield = '2b4a74' if own else '862d20'
    banner = '3a6fa8' if own else 'b83e2c'
    yaw = 22 if own else -22
    spots = [
        (0.0, -0.55, True, 0),
        (-0.72, -0.25, False, -14),
        (0.72, -0.25, False, -18),
        (-0.42, 0.45, False, -10),
        (0.42, 0.45, False, -12),
    ]
    for x, y, is_ban, tilt in spots:
        mini_soldier(m, (x, y, 0), tunic, shield, yaw=yaw, spear_tilt=tilt, banner=banner if is_ban else None)


def render_army(own, preview=False):
    name = 'army_own' if own else 'army_enemy'
    reset()
    common._mats.clear()
    lights()
    camera(256, 4.3, 208)
    make_army(M(), own)
    os.makedirs(TMP, exist_ok=True)
    raw = os.path.join(TMP, f'{name}_raw.png')
    render_to(raw)
    img = outline(load_rgba(raw), px=2, alpha=0.75)
    out = os.path.join(TMP if preview else os.path.join(ASSETS, 'units'), f'{name}.png')
    os.makedirs(os.path.dirname(out), exist_ok=True)
    save_rgba(img, out)
    print('WROTE', out)


# ───────── 3. 결정 창 삽화 4종 (events/<kind>.png, 840×260) ─────────

def vignette_bg(a, b, floor_a, floor_b):
    """삽화 바탕: 부드러운 땅과 하늘빛."""
    sc = bpy.context.scene
    sc.render.film_transparent = False
    w = sc.world
    bg = next(n for n in w.node_tree.nodes if n.type == 'BACKGROUND')
    bg.inputs[0].default_value = hexcol(a)
    bg.inputs[1].default_value = 0.85
    # 앞은 지우지 않고 땅 판을 진짜로 깐다
    cat = bpy.data.objects.get('catcher')
    if cat:
        bpy.data.objects.remove(cat, do_unlink=True)
    bpy.ops.mesh.primitive_plane_add(size=80, location=(0, 0, 0))
    fl = bpy.context.active_object
    fl.data.materials.append(nmat(floor_a, floor_b, 3.0, 0.95, bump=0.25))
    # 먼 언덕
    hill_m = nmat(b, floor_a, 2.0, 0.95, bump=0.2)
    blob('bg1', 5.0, hill_m, (-6, 12, -1.5), (2.2, 1.0, 0.55), sub=3, seed=1, jit=0.08)
    blob('bg2', 5.0, hill_m, (6, 13, -1.5), (2.4, 1.0, 0.6), sub=3, seed=4, jit=0.08)


def event_adopt(m):
    vignette_bg('203842', '2c4a42', '5f7a4c', '79945c')
    ground_patch(3.4, nmat('8a7a58', 'a08e68', 4, 0.95, bump=0.2), (1.3, 0.85, 1), h=0.03)
    place(lambda: house(m, 1, 1), (-3.2, 2.2, 0), 25, 0.95)
    place(lambda: env.tipi(m, (0, 0, 0), 1.0, 2.3, 2), (3.3, 2.0, 0), -25, 0.95)
    place(lambda: env.tree(m, 0, 0.9), (-4.8, 0.8, 0), 0, 0.85)
    place(lambda: env.tree(m, 3, 0.85), (4.8, 0.6, 0), 0, 0.85)
    fire(m, (0, 0.1, 0))
    # 돌자리에 둘러앉은 어른들
    for a_deg, col in ((150, '345888'), (30, '8a5a36'), (210, '7f9a6a'), (330, 'b0603e'), (90, 'c9a35f')):
        a = math.radians(a_deg)
        x, y = math.cos(a) * 1.45, 0.1 + math.sin(a) * 1.0
        log('seat', 0.1, 0.65, m['bark'], (x, y, 0.1), yaw=a + math.pi / 2)
        p = empty('elder', None, (x * 0.92, y * 0.92, 0.12))
        p.rotation_euler = (0, 0, a - math.pi / 2)
        cyl('body', 0.18, 0.2, 0.55, mat(col, 0.85), (0, 0, 0.18), seg=10, parent=p)
        sphere('head', 0.15, mat('c98d68', 0.6), p, (0, 0, 0.88))
    # 법 두루마리/비석
    blob('stele', 0.32, m['rock'], (-1.5, -0.6, 0.55), (0.85, 0.5, 1.9), sub=2, seed=9, jit=0.05, smooth=False)


def event_relief(m):
    vignette_bg('362c26', '4a3c30', '8a7654', 'a48e68')
    place(lambda: env.granary(m), (-1.8, 0.9, 0), 18, 1.05)
    place(lambda: house(m, 1, 2), (2.9, 1.8, 0), -20, 0.9)
    place(lambda: env.pine(m, 1, 0.85), (-4.6, 1.6, 0), 0, 0.85)
    # 빈 수레와 흩어진 빈 그릇, 기다리는 사람들(뒷모습)
    cyl('pot1', 0.18, 0.14, 0.32, nmat('9a5e3a', 'b47448', 8, 0.75), (0.2, -0.5, 0), seg=14)
    cyl('pot2', 0.16, 0.12, 0.28, nmat('8a5232', 'a46840', 8, 0.75), (0.65, -0.7, 0.12), rot=(math.radians(78), 0, 0.5), seg=14)
    for (x, y, col, yaw) in ((1.1, -0.3, '7a6a58', 135), (1.75, -0.55, '5f6d74', 145), (2.3, -0.15, '8a5a44', 125), (-0.4, -0.9, '6b5a48', 60)):
        p = empty('folk', None, (x, y, 0))
        p.rotation_euler = (0, 0, math.radians(yaw))
        cyl('legs', 0.12, 0.1, 0.42, mat('4a3a2e', 0.85), (0, 0, 0), seg=8, parent=p)
        cyl('body', 0.18, 0.2, 0.55, mat(col, 0.85), (0, 0, 0.4), seg=10, parent=p)
        sphere('head', 0.15, mat('c98d68', 0.6), p, (0, 0, 1.08))


def event_conquest(m):
    vignette_bg('2b3038', '3e3c38', '6d7452', '868a62')
    stone = nmat('9e9688', 'b8b0a0', 6, 0.85, bump=0.5)
    wall_ring(m, 2.6, 1.15, 18, stone, towers=2, gate=250)
    place(lambda: house(m, 2, 1), (0.3, 1.6, 0), -10, 0.78)
    place(lambda: house(m, 1, 3), (-1.2, 1.2, 0), 15, 0.75)
    # 피어오르는 연기
    for k in range(4):
        blob('smoke', 0.28 + k * 0.1, mat('6e6a66', 0.95), (-0.9 + k * 0.22, 1.1, 1.8 + k * 0.48), sub=2, jit=0.12, seed=k)
    # 성문 앞의 우리 군단과 깃발
    for (x, y, ban) in ((-1.8, -1.25, True), (-1.1, -1.45, False), (-2.35, -1.5, False), (-0.5, -1.3, False)):
        mini_soldier(m, (x, y, 0), '345888', '2b4a74', yaw=155, spear_tilt=-8, banner='3a6fa8' if ban else None)


def event_tribute(m):
    vignette_bg('24363e', '344a44', '6c8454', '889e68')
    # 국경 길과 사절단, 공물 궤짝들
    box('road', (9.0, 1.1, 0.03), nmat('b8a67e', 'cfc09a', 6, 0.95, bump=0.2), None, (0, -0.3, 0.01), rot=(0, 0, math.radians(-8)))
    place(lambda: env.tree(m, 2, 0.95), (-4.2, 1.5, 0), 0, 0.9)
    place(lambda: env.pine(m, 3, 0.9), (4.4, 1.6, 0), 0, 0.9)
    gold = mat('e2b44c', 0.3, 0, 0.85)
    for (x, y, yaw) in ((-0.5, -0.35, 10), (0.35, -0.5, -12)):
        box('chest', (0.62, 0.42, 0.38), nmat('7a4e2d', '96623a', 10, 0.8), None, (x, y, 0.19), rot=(0, 0, math.radians(yaw)), bevel=0.02)
        box('trim', (0.64, 0.44, 0.06), gold, None, (x, y, 0.34), rot=(0, 0, math.radians(yaw)))
    sack = nmat('c2a172', 'd8bb8a', 12, 0.95)
    blob('s1', 0.24, sack, (0.95, -0.3, 0.22), (1, 0.9, 1.1), sub=2, jit=0.05, seed=3)
    # 우리 사신(왼쪽)과 상대 군사(오른쪽)
    mini_soldier(m, (2.2, -0.1, 0), '9e3b2c', '862d20', yaw=-110, banner='b83e2c')
    mini_soldier(m, (2.85, 0.15, 0), '9e3b2c', '862d20', yaw=-115)
    mini_soldier(m, (-2.1, -0.45, 0), '345888', '2b4a74', yaw=75, banner='3a6fa8')


EVENTS = {
    'event_adopt': ('adopt', event_adopt),
    'event_relief': ('relief', event_relief),
    'event_conquest': ('conquest', event_conquest),
    'event_tribute': ('tribute', event_tribute),
}


def render_event(key, fn, preview=False):
    reset()
    common._mats.clear()
    lights(sun_strength=5.2, sky=(0.72, 0.80, 0.88), sky_strength=1.45)
    bpy.context.scene.view_settings.exposure = 0.35
    cam_rect(840, 260, 9.6, 168)
    fn(M())
    os.makedirs(TMP, exist_ok=True)
    out = os.path.join(TMP if preview else os.path.join(ASSETS, 'events'), f'{key}.png')
    os.makedirs(os.path.dirname(out), exist_ok=True)
    render_to(out)
    # 위아래 가장자리를 살짝 어둡게(비네트) 눌러 창과 자연스럽게 이어지게 한다
    a = load_rgba(out)
    h, w = a.shape[:2]
    yy = np.linspace(0, 1, h)[:, None]
    xx = np.linspace(-1, 1, w)[None, :]
    vig = np.clip(1.0 - 0.22 * (xx ** 2) - 0.30 * np.maximum(0, yy - 0.72) / 0.28 - 0.16 * np.maximum(0, 0.18 - yy) / 0.18, 0.52, 1.0)
    a[..., :3] *= vig[..., None]
    a[..., 3] = 1.0
    save_rgba(a, out)
    print('WROTE', out)


# ───────── 4. 창 아이콘 (game/icon.png, 256×256) ─────────

def render_app_icon(preview=False):
    reset()
    common._mats.clear()
    lights(sun_strength=4.2, sky=(0.62, 0.74, 0.86), sky_strength=1.0)
    camera(256, 5.2, 184)
    m = M()
    # 둥근 청록+황동 메달 바닥 위에 모닥불·움막·깃발·나무가 어우러진 작은 세상
    cyl('rim', 2.25, 2.3, 0.22, mat('82724d', 0.4, 0, 0.7), (0, 0.35, -0.22), seg=48, scale=(1, 0.92, 1))
    cyl('island', 2.08, 2.12, 0.16, nmat('5f8a48', '82aa5c', 4, 0.9, bump=0.25), (0, 0.35, -0.08), seg=48, scale=(1, 0.92, 1))
    place(lambda: house(m, 0, 1), (-0.75, 0.75, 0.06), 18, 0.95)
    place(lambda: env.tree(m, 0, 0.8), (0.95, 1.05, 0.06), 0, 0.78)
    fire(m, (0.15, -0.2, 0.08))
    flag(m, (-0.05, 0.55, 0.06), 2.55, '3a6fa8')
    os.makedirs(TMP, exist_ok=True)
    raw = os.path.join(TMP, 'icon_raw.png')
    render_to(raw)
    img = outline(load_rgba(raw), px=3, alpha=0.85)
    root_game = os.path.abspath(os.path.join(ASSETS, '..'))
    out = os.path.join(TMP if preview else root_game, 'icon.png')
    save_rgba(img, out)
    print('WROTE', out)


if __name__ == '__main__':
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    preview = bool(argv) and argv[0] == 'preview'
    want = set(a for a in argv if a != 'preview')
    def go(k): return not want or k in want
    if go('cart'):
        render_cart(preview)
    if go('army_own'):
        render_army(True, preview)
    if go('army_enemy'):
        render_army(False, preview)
    for k, (ek, fn) in EVENTS.items():
        if go(k) or go('events'):
            render_event(ek, fn, preview)
    if go('icon'):
        render_app_icon(preview)
