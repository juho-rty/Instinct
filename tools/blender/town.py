"""마을·도성·지방 건물 그림 공장. 실행:
  blender -b --python tools/blender/town.py -- [preview] [이름...]

env.py와 같은 사진기·빛·256 정사각(땅의 원점 208번 줄). 게임에서는 Art.Sprite가
그림 높이의 1/EnvFill 크기 정사각으로 놓는다 → 사물이 그림 높이의 약 45%를 차지하도록 사진기 폭을 맞춘다.
"""
import bpy, math, os, sys, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import importlib, common, env
importlib.reload(common)
importlib.reload(env)
from common import *
from env import nmat, blob, cyl, log, thatch, gable, stakes, ground_patch, M, hut, tipi, PX, GROUND, TMP, ASSETS


def place(fn, loc=(0, 0, 0), yaw=0.0, s=1.0):
    """fn이 원점에 지은 것을 loc으로 옮기고 돌리고 키운다."""
    before = set(bpy.context.scene.objects)
    fn()
    piv = empty('grp', None, loc)
    for o in list(bpy.context.scene.objects):
        if o not in before and o.parent is None and o is not piv:
            o.parent = piv
    piv.rotation_euler = (0, 0, math.radians(yaw))
    piv.scale = (s, s, s)
    return piv


def windows(w, d, z, n, material, depth_y=None, size=(0.22, 0.3)):
    """앞면(−y)에 창 n개."""
    y = -(depth_y if depth_y is not None else d / 2) - 0.02
    for i in range(n):
        x = -w / 2 + w * (i + 0.5) / n
        box('win', (size[0], 0.05, size[1]), material, None, (x, y, z))


# ───────── 시대별 집 ─────────

def house(m, era, seed=0):
    rnd = random.Random(seed)
    glass = mat('2a3440', 0.15)
    if era == 0:
        cyl('wall', 0.85, 0.8, 0.7, nmat('8a6844', 'a88458', 30, 0.9, bump=0.5), seg=18)
        thatch('roof', 1.15, 1.3, (0, 0, 0.62), m['thatch'], seed=seed)
        box('door', (0.4, 0.1, 0.55), mat('2a1d14', 0.6), None, (0, -0.8, 0.28))
    elif era == 1:
        box('wall', (1.8, 1.2, 0.85), nmat('c9b48a', 'dccaa0', 12, 0.9, bump=0.3), None, (0, 0, 0.43), bevel=0.03)
        gable('roof', 1.8, 1.2, 0.95, (0, 0, 0.85), m['thatch'], over=0.22, thick=0.14)
        box('door', (0.4, 0.06, 0.6), m['darkwood'], None, (0.3, -0.6, 0.3))
        box('win', (0.28, 0.05, 0.24), mat('2a1d14', 0.6), None, (-0.45, -0.6, 0.5))
    elif era == 2:
        plaster, beam = nmat('e6dcc4', 'f2ead8', 10, 0.9, bump=0.1), m['darkwood']
        box('wall', (1.9, 1.3, 1.5), plaster, None, (0, 0, 0.75), bevel=0.02)
        for x in (-0.95, -0.3, 0.3, 0.95):
            box('beam', (0.1, 1.34, 1.5), beam, None, (x, 0, 0.75))
        box('beam', (1.94, 1.34, 0.1), beam, None, (0, 0, 0.78))
        box('beam', (1.94, 1.34, 0.1), beam, None, (0, 0, 1.48))
        gable('roof', 1.9, 1.3, 1.0, (0, 0, 1.5), nmat('7a4a32', '9a5e3e', 25, 0.85, bump=0.5), over=0.2, thick=0.12)
        box('door', (0.4, 0.06, 0.65), beam, None, (0, -0.66, 0.33))
        windows(1.9, 1.3, 1.15, 2, glass, size=(0.28, 0.28))
    elif era == 3:
        stone = nmat('9a948a', 'b8b2a6', 6, 0.85, bump=0.5)
        box('base', (2.0, 1.4, 0.5), stone, None, (0, 0, 0.25), bevel=0.03)
        box('wall', (1.9, 1.3, 1.1), nmat('e8d8b8', 'f2e6cc', 10, 0.9, bump=0.1), None, (0, 0, 1.05), bevel=0.02)
        gable('roof', 1.9, 1.3, 0.9, (0, 0, 1.6), nmat('a8452f', 'c25a3c', 30, 0.7, bump=0.6), over=0.22, thick=0.12)
        box('chim', (0.25, 0.25, 0.8), stone, None, (0.6, 0.3, 2.2))
        box('door', (0.4, 0.06, 0.7), m['darkwood'], None, (0, -0.71, 0.35))
        windows(1.9, 1.3, 1.1, 3, glass, size=(0.24, 0.32))
    elif era == 4:
        stone = nmat('b8b0a0', 'cfc8ba', 6, 0.85, bump=0.4)
        box('wall', (1.8, 1.4, 2.4), stone, None, (0, 0, 1.2), bevel=0.03)
        box('cornice', (1.9, 1.5, 0.12), mat('8a8478', 0.7), None, (0, 0, 2.4))
        gable('roof', 1.8, 1.4, 0.8, (0, 0, 2.45), nmat('4a5260', '5e6878', 20, 0.6, bump=0.3), over=0.12, thick=0.1)
        for z in (0.9, 1.75):
            windows(1.8, 1.4, z, 3, glass, size=(0.26, 0.42))
        box('door', (0.45, 0.06, 0.75), mat('3a2a20', 0.5), None, (0, -0.72, 0.38))
    else:
        conc = nmat('b8b8b2', 'cfcfc8', 8, 0.8, bump=0.15)
        box('block', (1.8, 1.5, 3.4), conc, None, (0, 0, 1.7), bevel=0.02)
        box('roofrim', (1.9, 1.6, 0.12), mat('8a8a86', 0.7), None, (0, 0, 3.4))
        box('hvac', (0.5, 0.4, 0.3), mat('9a9a96', 0.5, 0, 0.4), None, (0.4, 0.2, 3.6))
        for z in (0.8, 1.6, 2.4, 3.1):
            windows(1.8, 1.5, z, 4, mat('6a8aa0', 0.1, 0, 0.3), size=(0.28, 0.42))
        box('door', (0.5, 0.06, 0.8), glass, None, (0, -0.77, 0.4))


# ───────── 도성 단계 ─────────

def fire(m, loc):
    for a in range(0, 360, 45):
        r = math.radians(a)
        blob('ring', 0.08, m['darkrock'], (loc[0] + math.cos(r) * 0.28, loc[1] + math.sin(r) * 0.28, 0.05), sub=1, seed=a, smooth=False)
    blob('fire', 0.12, m['fire'], (loc[0], loc[1], 0.15), (1, 1, 1.6), sub=1, jit=0.1)


def flag(m, loc, h=3.0, color='3a6fa8'):
    cyl('pole', 0.04, 0.03, h, m['darkwood'], loc, seg=6)
    box('flag', (0.7, 0.03, 0.42), nmat(color, color, 3, 0.8, bump=0.0), None, (loc[0] + 0.37, loc[1], loc[2] + h - 0.25))


def wall_ring(m, r, h, n, stone, towers=0, gate=270, tower_r=0.45):
    """둥근 돌성벽(토막 n개)과 탑."""
    for i in range(n):
        a = 360 * (i + 0.5) / n
        if abs((a - gate + 180) % 360 - 180) < 360 / n * 0.8:
            continue
        rad = math.radians(a)
        seg_len = 2 * math.pi * r / n * 1.04
        box('wall', (seg_len, 0.35, h), stone, None, (math.cos(rad) * r, math.sin(rad) * r * 0.8, h / 2),
            rot=(0, 0, rad + math.pi / 2))
        for k in (-1, 1):
            box('merlon', (0.16, 0.38, 0.18), stone, None,
                (math.cos(rad) * r + k * math.cos(rad + math.pi / 2) * seg_len * 0.25,
                 math.sin(rad) * r * 0.8 + k * math.sin(rad + math.pi / 2) * seg_len * 0.25, h + 0.09), rot=(0, 0, rad + math.pi / 2))
    for i in range(towers):
        a = math.radians(gate + 360 * (i + 0.5) / towers) if towers > 2 else math.radians(gate + (i * 2 - 1) * 18)
        p = (math.cos(a) * r, math.sin(a) * r * 0.8, 0)
        cyl('tower', tower_r, tower_r * 0.95, h * 1.6, stone, p, seg=14)
        cone('troof', tower_r * 1.25, 0.0, tower_r * 2.2, nmat('4a5260', '5e6878', 20, 0.6, bump=0.3), None, (p[0], p[1], h * 1.6),
             seg=14, down=False, subsurf=0, round_ends=False)


def capital(m, stage):
    if stage == 0:
        ground_patch(2.4, nmat('8a7a58', 'a08e68', 4, 0.95, bump=0.2), (1, 0.85, 1), h=0.03)
        place(lambda: house(m, 0, 1), (-1.15, 0.35, 0), 10, 1.0)
        place(lambda: tipi(m, (0, 0, 0), 0.85, 2.1, 2), (1.15, 0.55, 0), -15, 1.0)
        place(lambda: house(m, 0, 2), (0.1, 1.3, 0), 0, 0.9)
        fire(m, (0.05, -0.55, 0))
        for i in range(3):
            log('seat', 0.09, 0.7, m['bark'], (0.05 + math.cos(i * 2.1) * 0.75, -0.55 + math.sin(i * 2.1) * 0.6, 0.09), yaw=i * 2.1 + 1.57)
        flag(m, (-0.4, 0.2, 0), 2.8)
        blob('sack', 0.2, nmat('c2a172', 'd8bb8a', 12, 0.95), (1.6, -0.6, 0.18), (0.9, 0.9, 1.1), sub=2, jit=0.06)
    elif stage == 1:
        ground_patch(2.9, nmat('8a7a58', 'a08e68', 4, 0.95, bump=0.2), (1, 0.85, 1), h=0.03)
        stakes(34, 2.7, 1.2, nmat('6a4a2e', '8a6440', 14, 0.85), r=0.08, gate=(255, 285))
        for o in bpy.context.scene.objects:
            if o.name.startswith(('stake', 'tip')):
                o.location.y *= 0.82
        place(lambda: house(m, 1, 1), (-1.1, 0.6, 0), 15, 0.8)
        place(lambda: house(m, 1, 2), (1.2, 0.5, 0), -20, 0.8)
        place(lambda: house(m, 1, 3), (0.1, 1.35, 0), 0, 0.95)
        place(lambda: house(m, 0, 4), (-1.3, -0.8, 0), 0, 0.75)
        fire(m, (0.3, -0.5, 0))
        flag(m, (0.0, 0.3, 0), 3.4)
    elif stage == 2:
        stone = nmat('a49c8c', 'bdb5a4', 6, 0.85, bump=0.5)
        ground_patch(3.1, nmat('8a8068', 'a49a80', 4, 0.95, bump=0.2), (1, 0.82, 1), h=0.03)
        wall_ring(m, 2.85, 1.0, 22, stone, towers=2)
        for (x, y, yaw, e) in ((-1.2, 0.6, 15, 2), (1.2, 0.5, -15, 2), (0.0, 1.4, 0, 2), (-1.4, -0.6, 5, 1), (1.3, -0.7, -10, 1)):
            place(lambda e=e: house(m, e, int(x * 10)), (x, y, 0), yaw, 0.7)
        flag(m, (0.0, 0.2, 0), 3.6)
    elif stage == 3:
        stone = nmat('a8a090', 'c4bcaa', 6, 0.85, bump=0.5)
        ground_patch(3.2, nmat('8a8068', 'a49a80', 4, 0.95, bump=0.2), (1, 0.82, 1), h=0.03)
        wall_ring(m, 3.0, 1.3, 26, stone, towers=5)
        # 가운데 성채
        box('keep', (1.4, 1.2, 2.6), stone, None, (0, 0.6, 1.3), bevel=0.03)
        for x in (-0.6, -0.2, 0.2, 0.6):
            box('merlon', (0.2, 1.24, 0.22), stone, None, (x, 0.6, 2.7))
        flag(m, (0.0, 0.6, 2.8), 1.4)
        for (x, y, yaw) in ((-1.6, 0.4, 20), (1.6, 0.4, -20), (-1.2, -1.0, 5), (1.2, -1.0, -5)):
            place(lambda: house(m, 3, int(x * 10)), (x, y, 0), yaw, 0.6)
    else:
        stone = nmat('c4bcaa', 'dcd4c2', 6, 0.8, bump=0.4)
        ground_patch(3.3, nmat('9a9480', 'b4ae98', 4, 0.9, bump=0.2), (1, 0.82, 1), h=0.03)
        wall_ring(m, 3.1, 1.5, 28, stone, towers=6)
        # 궁전: 몸채와 둥근 지붕
        box('palace', (2.4, 1.4, 1.8), stone, None, (0, 0.7, 0.9), bevel=0.03)
        for x in (-0.8, -0.3, 0.3, 0.8):
            cyl('col', 0.08, 0.08, 1.5, mat('e8e2d4', 0.6), (x, -0.05, 0), seg=10)
        box('porch', (2.0, 0.4, 0.15), stone, None, (0, -0.05, 1.55))
        sphere('dome', 0.75, nmat('3a7a8a', '4a96a6', 10, 0.35, bump=0.1), None, (0, 0.8, 1.8), keep=lambda c: c.z > -0.01)
        cyl('spire', 0.06, 0.0, 0.7, mat('d8b04a', 0.3, 0, 0.9), (0, 0.8, 2.5), seg=8)
        for x in (-1.0, 1.0):
            cyl('wing', 0.32, 0.3, 2.4, stone, (x, 0.9, 0), seg=14)
            sphere('wdome', 0.33, nmat('3a7a8a', '4a96a6', 10, 0.35, bump=0.1), None, (x, 0.9, 2.4), keep=lambda c: c.z > -0.01)
        for (x, y, yaw) in ((-1.8, -0.3, 20), (1.8, -0.3, -20), (-1.0, -1.4, 5), (1.0, -1.4, -5)):
            place(lambda: house(m, 4, int(x * 10)), (x, y, 0), yaw, 0.5)
        flag(m, (0, 0.8, 2.6), 1.0)


# ───────── 지방 건물 ─────────

def building(m, key):
    stone = nmat('b0a898', 'c8c0b0', 6, 0.85, bump=0.4)
    glass = mat('2a3440', 0.15)
    if key == 'granary':
        place(lambda: env.granary(m), (0, 0, 0), 0, 1.0)
    elif key == 'market':
        for i, (x, y, c) in enumerate(((-0.9, 0.2, 'b5442f'), (0.9, 0.3, '3a6fa8'), (0.0, -0.6, 'c9a540'))):
            box('stall', (1.1, 0.7, 0.55), m['wood'], None, (x, y, 0.28), bevel=0.02)
            for sx in (-0.5, 0.5):
                cyl('post', 0.04, 0.04, 1.3, m['darkwood'], (x + sx, y - 0.3, 0), seg=6)
            stripes = nmat(c, 'efe6d0', 3, 0.8, bump=0.0, mix=(0.49, 0.51))
            box('awning', (1.3, 0.9, 0.06), stripes, None, (x, y - 0.1, 1.3), rot=(math.radians(-15), 0, 0))
            for k in range(4):
                sphere('goods', 0.1, m['berry'] if k % 2 else m['wheat'], None, (x - 0.3 + k * 0.2, y - 0.2, 0.6), seg=8, rings=6)
        blob('sack', 0.22, nmat('c2a172', 'd8bb8a', 12, 0.95), (1.5, -0.7, 0.2), sub=2, jit=0.06)
        cyl('barrel', 0.2, 0.2, 0.45, m['wood'], (-1.6, -0.6, 0), seg=14)
    elif key == 'temple':
        for i, w in enumerate((3.0, 2.7, 2.4)):
            box('step', (w, w * 0.65, 0.18), stone, None, (0, 0.2, 0.09 + i * 0.18), bevel=0.02)
        for x in (-1.0, -0.33, 0.33, 1.0):
            cyl('col', 0.13, 0.11, 1.6, mat('e8e2d4', 0.6), (x, -0.55, 0.54), seg=12)
            cyl('col', 0.13, 0.11, 1.6, mat('e8e2d4', 0.6), (x, 0.95, 0.54), seg=12)
        box('cella', (1.6, 1.2, 1.5), stone, None, (0, 0.2, 1.3))
        box('beam', (2.5, 1.8, 0.2), stone, None, (0, 0.2, 2.24))
        gable('roof', 2.5, 1.8, 0.6, (0, 0.2, 2.34), nmat('c9b98a', 'd8caa0', 10, 0.8), over=0.08, thick=0.1)
        fire(m, (0, -1.15, 0))
    elif key == 'library':
        box('hall', (2.4, 1.5, 1.7), stone, None, (0, 0, 0.85), bevel=0.03)
        gable('roof', 2.4, 1.5, 0.8, (0, 0, 1.7), nmat('4a5a78', '5f6f8a', 20, 0.6, bump=0.3), over=0.12, thick=0.1)
        for i in range(3):
            x = -0.75 + i * 0.75
            cyl('arch', 0.22, 0.22, 0.06, glass, (x, -0.78, 1.0), rot=(math.radians(90), 0, 0), seg=16)
            box('archb', (0.44, 0.06, 0.35), glass, None, (x, -0.76, 0.8))
        box('door', (0.5, 0.06, 0.8), m['darkwood'], None, (0, -0.77, 0.4))
        box('scroll', (0.6, 0.05, 0.4), mat('efe6d0', 0.8), None, (0, -0.79, 1.45))
    elif key == 'barracks':
        place(lambda: env.warhut(m), (0, 0.2, 0), 0, 1.0)
        stakes(16, 2.0, 0.8, nmat('6a4a2e', '8a6440', 14, 0.85), arc=(200, 340), r=0.06)
    elif key == 'castle':
        box('keep', (1.8, 1.5, 2.2), stone, None, (0, 0.3, 1.1), bevel=0.03)
        for x in (-0.7, -0.25, 0.25, 0.7):
            box('merlon', (0.24, 1.54, 0.25), stone, None, (x, 0.3, 2.32))
        for x in (-1.2, 1.2):
            cyl('tower', 0.5, 0.48, 3.0, stone, (x, -0.35, 0), seg=16)
            cone('troof', 0.62, 0.0, 1.1, nmat('4a5260', '5e6878', 20, 0.6, bump=0.3), None, (x, -0.35, 3.0), seg=16, down=False,
                 subsurf=0, round_ends=False)
        box('gate', (0.6, 0.08, 0.9), mat('2a1d14', 0.6), None, (0, -0.47, 0.45))
        flag(m, (0, 0.3, 2.2), 1.2, 'a8452f')
    elif key == 'mine':
        blob('hill', 1.5, m['darkrock'], (0, 0.6, 0), (1.4, 1.0, 0.9), sub=3, seed=6, jit=0.1, smooth=False, flat_bottom=0)
        box('shaft', (0.9, 0.3, 0.9), mat('140f0c', 0.9), None, (0, -0.55, 0.45))
        for x in (-0.5, 0.5):
            box('prop', (0.14, 0.14, 1.05), m['darkwood'], None, (x, -0.7, 0.52))
        box('lintel', (1.2, 0.16, 0.16), m['darkwood'], None, (0, -0.7, 1.08))
        box('cart', (0.6, 0.4, 0.3), m['wood'], None, (1.0, -1.1, 0.3), bevel=0.02)
        for x in (0.8, 1.2):
            cyl('wheel', 0.12, 0.12, 0.05, m['darkwood'], (x, -1.32, 0.12), rot=(math.radians(90), 0, 0), seg=12)
        for k in range(3):
            blob('ore', 0.1, mat('b07a4a', 0.5, 0, 0.4), (0.9 + k * 0.12, -1.1, 0.5), sub=1, seed=k, smooth=False)
        log('rail', 0.03, 1.8, mat('6a6e73', 0.4, 0, 0.6), (0.4, -0.95, 0.03), yaw=math.radians(-30))
    elif key == 'harbor':
        cyl('water', 2.4, 2.35, 0.03, m['water'], (0, -0.4, 0), seg=40, scale=(1, 0.6, 1))
        box('pier', (0.7, 2.4, 0.1), m['wood'], None, (-0.8, -0.3, 0.45))
        for y in (-1.3, -0.5, 0.3):
            for x in (-1.1, -0.5):
                cyl('pile', 0.06, 0.06, 0.5, m['darkwood'], (x, y, 0), seg=6)
        # 배
        hull = blob('hull', 0.5, nmat('6a4a2e', '8a6440', 14, 0.8), (0.8, -0.5, 0.25), (2.4, 0.9, 0.55), sub=3, jit=0.0,
                    flat_bottom=-0.1)
        hull.scale = (1.8, 0.7, 0.6)
        cyl('mast', 0.04, 0.03, 2.4, m['darkwood'], (0.8, -0.5, 0.3), seg=6)
        box('sail', (1.0, 0.04, 1.2), mat('f0e8d4', 0.9), None, (0.8, -0.48, 1.6))
        box('shed', (1.0, 0.8, 0.8), m['wood'], None, (-1.4, 1.0, 0.4))
        gable('sroof', 1.0, 0.8, 0.5, (-1.4, 1.0, 0.8), nmat('7a4a32', '9a5e3e', 25, 0.85, bump=0.5), over=0.12, thick=0.08)
    elif key == 'university':
        box('hall', (3.0, 1.4, 1.5), nmat('e0d6c2', 'ece4d2', 8, 0.85, bump=0.2), None, (0, 0.3, 0.75), bevel=0.03)
        gable('roof', 3.0, 1.4, 0.7, (0, 0.3, 1.5), nmat('4a5a78', '5f6f8a', 20, 0.6, bump=0.3), over=0.1, thick=0.1)
        box('belltower', (0.7, 0.7, 2.9), nmat('e8dfcc', 'f2ead8', 8, 0.85, bump=0.2), None, (0, -0.3, 1.45), bevel=0.02)
        cone('spire', 0.55, 0.0, 1.0, nmat('4a5a78', '5f6f8a', 20, 0.6, bump=0.3), None, (0, -0.3, 2.9), seg=4, down=False,
             subsurf=0, round_ends=False, rot=(0, 0, math.radians(45)))
        cyl('clock', 0.18, 0.18, 0.04, mat('f4ecd6', 0.5), (0, -0.66, 2.2), rot=(math.radians(90), 0, 0), seg=16)
        windows(3.0, 1.4, 0.85, 6, glass, depth_y=0.4, size=(0.22, 0.4))
    elif key == 'factory':
        brick = nmat('8a4a38', 'a65e46', 18, 0.85, bump=0.4)
        box('hall', (3.0, 1.6, 1.2), brick, None, (0, 0.3, 0.6), bevel=0.02)
        for i in range(3):
            x = -1.0 + i * 1.0
            box('saw', (1.0, 1.6, 0.08), mat('5a6068', 0.5, 0, 0.3), None, (x, 0.3, 1.5), rot=(0, math.radians(-35), 0))
            box('glass', (0.06, 1.55, 0.55), mat('7a9ab0', 0.1), None, (x + 0.42, 0.3, 1.45))
        cyl('chimney', 0.25, 0.2, 3.4, brick, (1.2, 1.0, 0), seg=14)
        for k in range(3):
            blob('smoke', 0.28 + k * 0.08, mat('9a9a9a', 0.95), (1.25 + k * 0.25, 1.0 + k * 0.1, 3.6 + k * 0.45), sub=2, jit=0.1, seed=k)
        windows(3.0, 1.6, 0.6, 5, glass, depth_y=0.5, size=(0.3, 0.4))
    elif key == 'site':
        for x in (-0.8, 0.0, 0.8):
            for y in (-0.4, 0.5):
                cyl('pole', 0.05, 0.05, 2.0, m['wood'], (x, y, 0), seg=6)
        for z in (0.9, 1.8):
            log('plank', 0.06, 1.9, m['wood'], (0, -0.4, z))
            log('plank', 0.06, 1.9, m['wood'], (0, 0.5, z))
        box('wallpart', (1.6, 0.3, 0.7), stone, None, (0, 0.05, 0.35))
        for i in range(3):
            log('lumber', 0.08, 1.2, m['bark'], (1.4, -1.0 + i * 0.02, 0.08 + i * 0.15), yaw=math.radians(70))
        for i in range(4):
            box('stone', (0.35, 0.28, 0.25), stone, None, (-1.3 + (i % 2) * 0.38, -0.9, 0.13 + (i // 2) * 0.26), bevel=0.02)


JOBS = {}
for e in range(6):
    JOBS[f'house_{e}'] = (lambda m, e=e: place(lambda: house(m, e, e), yaw=0 if e == 0 else -30), [3.4, 3.6, 4.4, 5.1, 6.0, 7.6][e], f'town/house_{e}.png')
for st in range(5):
    JOBS[f'capital_{st}'] = (lambda m, st=st: capital(m, st), [6.2, 7.2, 7.6, 8.4, 8.7][st], f'town/capital_{st}.png')
for key, o in (('granary', 4.6), ('market', 5.4), ('temple', 6.6), ('library', 6.0), ('barracks', 6.0), ('castle', 7.2),
               ('mine', 5.4), ('harbor', 6.2), ('university', 7.4), ('factory', 8.6), ('site', 5.0)):
    turn = -28 if key in ('market', 'temple', 'library', 'castle', 'university', 'factory', 'site') else 0
    JOBS[key] = (lambda m, key=key, turn=turn: place(lambda: building(m, key), yaw=turn), o, f'buildings/{key}.png')


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
