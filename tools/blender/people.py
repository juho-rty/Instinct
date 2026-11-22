"""사람 그림 공장. 실행:
  blender -b --python tools/blender/people.py -- [preview|body|merge] [몸 이름...]

낮은 다각형의 사람을 관절(빈 물체) 사다리로 만들고, 걷기·짐 지기·일하기를 자세 함수로 움직여
Cycles로 한 틀씩 찍은 뒤 몸마다 한 장(game/assets/units/people_<몸>.png)과 people.json으로 붙인다.
틀은 128×128, 발밑(땅의 원점)이 위에서 108번 줄. 줄마다 최대 8틀.
방향은 idle/walk/carry가 S·SE·E·NE·N 다섯(서쪽은 화면에서 좌우 뒤집기), 일 동작은 SE·E·NE 세 방향.
"""
import bpy, math, os, sys, json, glob
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import importlib, common
importlib.reload(common)
from common import *

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
OUT = os.path.join(ROOT, 'game', 'assets', 'units')
TMP = os.path.join(os.environ.get('TEMP', '/tmp'), 'instinct-people')
PX, GROUND, ORTHO = 128, 108, 2.56
FRAMES = 8
IDLE_FRAMES = 4
DIRS = [('S', 0), ('SE', 45), ('E', 90), ('NE', 135), ('N', 180)]
WORK_DIRS = [('SE', 45), ('E', 90), ('NE', 135)]

BODIES = {
    'man':     dict(skin='c98d68', top='d89648', legs='4a3525', hair='3b2516', belt='5c3d28', kind='man', vest='5c3d28'),
    'hunter':  dict(skin='b57a55', top='8f4c2c', legs='4a3525', hair='1f1611', belt='3a2618', kind='man', fur=True, beard=True, band='a8382b', quiver=True),
    'woman':   dict(skin='d49a74', top='4b8577', legs='4a3525', hair='522f1c', belt='5c442a', kind='woman', sash='e6d5b0'),
    'woman2':  dict(skin='b98260', top='b85040', legs='4a3525', hair='2a1c14', belt='4a3020', kind='woman', bun=True, apron='dcc08e'),
    'chief':   dict(skin='c98d68', top='31527d', legs='3a2c24', hair='2a1c14', belt='d4a644', kind='man', cape='9e3328', crown=True, beard=True),
    'soldier': dict(skin='c98d68', top='31588a', legs='3c332c', hair='2a1c14', belt='2a2420', kind='man', helmet=True, pauldron=True, shield=True, guard_spear=True),
    'child':   dict(skin='d49a74', top='e3c87d', legs='6b5239', hair='6a4426', belt='5e8a4a', kind='child'),
}

S = {}  # 관절 이름 → 빈 물체


def build(spec):
    """몸 하나를 짓는다. 남자 키 약 1.74m. 앞은 −Y, 왼쪽은 +X, 위는 +Z."""
    kind = spec['kind']
    k = 0.68 if kind == 'child' else (0.95 if kind == 'woman' else 1.0)
    head_k = 1.24 if kind == 'child' else 1.0
    skin = mat(spec['skin'], 0.55, 0.2)
    top = mat(spec['top'], 0.8, 0.35)
    legs = mat(spec['legs'], 0.82)
    hair = mat(spec['hair'], 0.72, 0.25)
    belt = mat(spec['belt'], 0.55)
    dark = mat('201611', 0.4)
    gaiter = mat('7a5c40', 0.85)
    boot = mat('3a281c', 0.7)

    root = empty('root')
    root.scale = (k, k, k)
    S['root'] = root
    hips = S['hips'] = empty('hips', root, (0, 0, 0.92))

    # 치마·웃옷 자락
    skirt_len = 0.52 if kind == 'woman' else 0.32
    cone('skirt', 0.22 if kind != 'woman' else 0.25, 0.17, skirt_len, top, hips, (0, 0, 0.06), seg=14, scale=(1.04, 0.84, 1))
    torus('belt', 0.168, 0.028, belt, hips, (0, 0, 0.05), scale=(1.04, 0.84, 1))
    if spec.get('apron'):
        box('apron', (0.24, 0.04, 0.38), mat(spec['apron'], 0.85, 0.2), hips, (0, -0.17, -0.14), rot=(math.radians(-6), 0, 0), bevel=0.015)

    spine = S['spine'] = empty('spine', hips, (0, 0, 0.05))
    cone('chest', 0.19, 0.23, 0.45, top, spine, (0, 0, 0), down=False, seg=14, scale=(1.05, 0.82, 1))
    if spec.get('vest'):
        vm = mat(spec['vest'], 0.78, 0.15)
        for sx in (-1, 1):
            box('vstrap', (0.075, 0.32, 0.36), vm, spine, (sx * 0.11, -0.01, 0.23), bevel=0.015)
    if spec.get('sash'):
        sm = mat(spec['sash'], 0.8, 0.3)
        box('sash', (0.07, 0.31, 0.44), sm, spine, (0.04, -0.01, 0.22), rot=(0, math.radians(-24), 0), bevel=0.01)
    if spec.get('fur'):
        torus('furcollar', 0.17, 0.068, mat('cbb596', 0.95, 0.75), spine, (0, 0, 0.42), scale=(1.08, 0.88, 0.82))
    if spec.get('cape'):
        cm = mat(spec['cape'], 0.82, 0.4)
        cone('cape', 0.30, 0.18, 0.72, cm, spine, (0, 0.10, -0.22), down=False, seg=12, scale=(1.1, 0.45, 1), rot=(math.radians(8), 0, 0))
        torus('clasp', 0.04, 0.014, mat('e5b954', 0.3, 0, 0.85), spine, (0, -0.17, 0.38), rot=(math.radians(90), 0, 0))
    if spec.get('quiver'):
        qm = mat('6a4428', 0.8)
        qv = cone('quiver', 0.055, 0.04, 0.42, qm, spine, (0.12, 0.17, 0.20), down=False, seg=10, rot=(math.radians(15), math.radians(18), 0), subsurf=0)
        for fx in (-0.015, 0.015):
            cone('fletch', 0.025, 0.008, 0.10, mat('b83a2c', 0.8), qv, (fx, 0, 0.44), down=False, seg=6, subsurf=0)

    neck = S['neck'] = empty('neck', spine, (0, 0, 0.45))
    cone('neckm', 0.058, 0.052, 0.09, skin, neck, (0, 0, 0), down=False, subsurf=0)
    head = S['head'] = empty('head', neck, (0, 0, 0.19))
    head.scale = (head_k,) * 3
    hr = 0.18
    sphere('headm', hr, skin, head, (0, 0, 0), scale=(0.95, 0.97, 1.04))
    sphere('nose', 0.028, skin, head, (0, -hr * 0.99, -0.012), scale=(0.8, 1.1, 1))
    for sx in (-1, 1):
        sphere('eye', 0.023, dark, head, (sx * 0.054, -hr * 0.90, 0.026))
        box('brow', (0.042, 0.02, 0.014), hair, head, (sx * 0.054, -hr * 0.92, 0.058), rot=(0, 0, sx * math.radians(-6)))
        sphere('ear', 0.032, skin, head, (sx * hr * 0.96, 0.0, 0.0), scale=(0.5, 0.8, 1))

    # 머리카락
    sphere('hair', hr * 1.08, hair, head, (0, 0.01, 0.012),
           keep=lambda c: c.z > 0.035 or (c.y > 0.015 and c.z > -0.115), scale=(0.99, 1.01, 1.05))
    if kind == 'woman':
        cone('hairlong', 0.12, 0.14, 0.34, hair, head, (0, 0.08, -0.02), seg=12, scale=(1, 0.62, 1))
    if spec.get('bun'):
        sphere('bun', 0.07, hair, head, (0, 0.12, 0.10))
        cone('pin', 0.01, 0.006, 0.22, mat('e5b954', 0.35, 0, 0.8), head, (-0.11, 0.12, 0.11), down=False, seg=6, rot=(0, math.radians(78), 0), subsurf=0)
    if spec.get('beard'):
        sphere('beard', hr * 0.84, hair, head, (0, -0.06, -0.08), keep=lambda c: c.y < 0.025 and c.z < 0.02, scale=(0.95, 0.92, 1.05))
    if spec.get('band'):
        torus('band', hr * 1.03, 0.022, mat(spec['band'], 0.75), head, (0, 0, 0.055), scale=(0.98, 1.0, 0.9))
    if spec.get('crown'):
        cm = mat('e5b954', 0.28, 0, 0.88)
        torus('circlet', hr * 1.04, 0.024, cm, head, (0, 0, 0.065), scale=(0.98, 1.0, 0.95))
        box('crest', (0.05, 0.03, 0.065), mat('b83a2c', 0.4), head, (0, -hr * 1.04, 0.09))
    if spec.get('helmet'):
        steel = mat('9aa2a8', 0.32, 0, 0.75)
        sphere('helmet', hr * 1.14, steel, head, (0, 0, 0.022), keep=lambda c: c.z > -0.01)
        torus('rim', hr * 1.13, 0.02, mat('6f767c', 0.32, 0, 0.75), head, (0, 0, 0.022))

    # 팔: 어깨 → 팔꿈치 → 손
    for side, sx in (('R', -1), ('L', 1)):
        sh = S['sh' + side] = empty('sh' + side, spine, (sx * 0.245, 0, 0.38))
        sphere('shm', 0.088, mat('9aa2a8', 0.32, 0, 0.75) if spec.get('pauldron') else top, sh, (0, 0, 0))
        cone('upper', 0.082, 0.072, 0.30, top, sh, (0, 0, 0), seg=10)
        el = S['el' + side] = empty('el' + side, sh, (0, 0, -0.29))
        cone('fore', 0.064, 0.068, 0.26, skin, el, (0, 0, 0), seg=10)
        hd = S['hd' + side] = empty('hd' + side, el, (0, 0, -0.28))
        sphere('hand', 0.065, skin, hd, (0, 0, 0), scale=(0.88, 1, 1.12))

    # 병사 기본 방패·창
    if spec.get('shield'):
        steel = mat('9aa2a8', 0.3, 0, 0.75)
        sh_obj = cone('shield', 0.26, 0.26, 0.045, mat('2b4a74', 0.7), S['elL'], (0.10, -0.05, -0.18),
                      rot=(0, math.radians(-85), 0), seg=16, subsurf=0, round_ends=False)
        sphere('boss', 0.06, steel, sh_obj, (0, 0, -0.03))
    if spec.get('guard_spear'):
        sp = cone('gspear', 0.022, 0.02, 1.48, mat('6a4428', 0.75), S['hdR'], (0, 0, 0.72), seg=8, subsurf=0)
        cone('gtip', 0.045, 0.0, 0.18, mat('9aa2a8', 0.3, 0, 0.75), sp, (0, 0, -1.48), seg=6, subsurf=0)

    # 다리: 엉덩이 → 무릎 → 발목
    for side, sx in (('R', -1), ('L', 1)):
        hp = S['hp' + side] = empty('hp' + side, hips, (sx * 0.095, 0, -0.02))
        cone('thigh', 0.095, 0.082, 0.42, legs, hp, (0, 0, 0), seg=10)
        kn = S['kn' + side] = empty('kn' + side, hp, (0, 0, -0.41))
        cone('shin', 0.076, 0.066, 0.41, gaiter, kn, (0, 0, 0), seg=10)
        an = S['an' + side] = empty('an' + side, kn, (0, 0, -0.41))
        sphere('foot', 0.072, boot, an, (0, -0.045, -0.035), scale=(0.95, 1.72, 0.76))
    return root


def props():
    """일 도구와 짐. 오른손·왼손 또는 몸통에 붙어 있고 줄마다 켜고 끈다."""
    wood = mat('7a5534', 0.72)
    darkwood = mat('4e3624', 0.75)
    iron = mat('8a8f94', 0.3, 0, 0.8)
    stone = mat('9a958c', 0.85)
    brass = mat('d8aa48', 0.3, 0, 0.85)
    berry_m = mat('c4243c', 0.32)
    wheat_m = mat('e8c85a', 0.8)
    P = {}
    hd = S['hdR']
    hdL = S['hdL']

    def handle(name, length, parent=hd):
        return cone(name, 0.024, 0.022, length, wood, parent, (0, 0, 0.08), seg=8, subsurf=0)

    # 벌목 도끼 (+Y 방향이 내려찍는 날)
    axe = handle('axe', 0.60)
    box('axehead', (0.035, 0.18, 0.13), iron, axe, (0, 0.045, -0.50), bevel=0.01)
    P['chop'] = [axe]

    # 채석 곡괭이
    pick = handle('pick', 0.62)
    cone('pickhead', 0.032, 0.006, 0.38, iron, pick, (0, 0, -0.52), seg=8, rot=(math.radians(90), 0, 0), down=False, subsurf=0)
    cone('pickhead2', 0.032, 0.006, 0.22, iron, pick, (0, 0, -0.52), seg=8, rot=(math.radians(-90), 0, 0), down=False, subsurf=0)
    P['pick'] = [pick]

    # 농사 괭이 (자루 0.78로 프레임 안 유지, 날이 +Y 땅 쪽으로)
    hoe = handle('hoe', 0.78)
    box('hoehead', (0.16, 0.035, 0.15), iron, hoe, (0, 0.06, -0.70), rot=(math.radians(-18), 0, 0), bevel=0.008)
    P['hoe'] = [hoe]

    # 건설 망치
    ham = handle('hammer', 0.40)
    box('hamhead', (0.09, 0.18, 0.09), stone, ham, (0, 0.02, -0.35), bevel=0.015)
    P['hammer'] = [ham]

    # 사냥 활(왼손 hdL)과 화살(활에 얹어 항상 -Y 표적을 겨냥)
    bow = empty('bow_grp', hdL, (0, 0, -0.04))
    box('bgrip', (0.04, 0.04, 0.16), darkwood, bow, (0, 0, 0), rot=(math.radians(90), 0, 0), bevel=0.01)
    cone('blimb_u', 0.022, 0.014, 0.42, wood, bow, (0, -0.07, 0.04), down=False, seg=8, rot=(math.radians(74), 0, 0), subsurf=0)
    cone('blimb_d', 0.022, 0.014, 0.42, wood, bow, (0, 0.07, 0.04), down=False, seg=8, rot=(math.radians(-74), 0, 0), subsurf=0)
    cone('bstring', 0.010, 0.010, 0.80, mat('e8dcc0', 0.85), bow, (0, -0.40, 0.15), down=False, seg=6, rot=(math.radians(-90), 0, 0), subsurf=0)
    arrow = cone('bow_arrow', 0.016, 0.016, 0.66, wood, bow, (0, 0, 0.22), seg=6, subsurf=0)
    cone('atip', 0.036, 0.0, 0.12, iron, arrow, (0, 0, -0.66), seg=6, subsurf=0)
    cone('afletch', 0.028, 0.01, 0.10, mat('c83a2c', 0.8), arrow, (0, 0, 0.02), down=False, seg=6, subsurf=0)
    P['bow'] = [bow, arrow]
    P['bow_arrow'] = [arrow]
    S['arrow'] = arrow

    # 고기잡이 작살
    harpoon = cone('harpoon', 0.022, 0.02, 1.18, wood, hd, (0, 0, 0.42), seg=8, subsurf=0)
    cone('htip', 0.045, 0.0, 0.16, iron, harpoon, (0, 0, -1.18), seg=6, subsurf=0)
    cone('hbarb', 0.022, 0.0, 0.10, iron, harpoon, (0, 0.02, -1.04), down=False, seg=5, rot=(math.radians(150), 0, 0), subsurf=0)
    P['fish'] = [harpoon]

    # 채집: 땅 바구니 + 손의 열매
    basket = cone('basket', 0.18, 0.23, 0.22, mat('b58b52', 0.9, 0.25), S['root'], (0.26, -0.30, 0), seg=14, down=False, subsurf=1)
    for x, y in ((0.0, 0.0), (0.07, 0.05), (-0.06, 0.04), (0.02, -0.06)):
        sphere('berry', 0.055, berry_m, basket, (x, y, 0.22))
    held_b = sphere('held_berry', 0.05, berry_m, hd, (0, 0, -0.05))
    P['gather'] = [basket, held_b]

    # 정찰: 망원경 (총 rx=-90일 때 hdR의 -Z가 정면 -Y로 곧게 뻗는다)
    spy = empty('spyglass', hd, (0, 0, 0))
    cone('spyb', 0.040, 0.034, 0.20, wood, spy, (0, 0, 0.06), seg=10, subsurf=0)
    cone('spyt', 0.034, 0.028, 0.18, brass, spy, (0, 0, -0.14), seg=10, subsurf=0)
    P['scout'] = [spy]

    # 짐(모두 앞이나 어깨에 들어 S/SE/E/NE 모든 각도에서 잘 보이게)
    sp = S['spine']
    # 1) 식량 바구니 (앞쪽에 안기)
    fb = cone('food_basket', 0.16, 0.21, 0.22, mat('b58b52', 0.9, 0.25), sp, (0, -0.27, 0.12), seg=14, down=False, subsurf=1)
    for x, y, m_c in ((-0.05, -0.03, berry_m), (0.06, -0.02, berry_m), (0.0, 0.05, wheat_m), (-0.04, 0.04, wheat_m), (0.05, 0.04, wheat_m)):
        sphere('fitem', 0.06, m_c, fb, (x, y, 0.22))
    P['food'] = [fb]

    # 2) 통나무 묶음 (오른쪽 어깨 위에 앞뒤로 메기)
    logs = empty('logs', sp, (-0.18, -0.04, 0.44))
    for lx, lz in ((0.0, 0.0), (-0.07, 0.08), (0.07, 0.08)):
        lg = cone('log', 0.068, 0.064, 0.68, mat('8a5e3a', 0.85), logs, (lx, 0.34, lz), seg=10, rot=(math.radians(90), 0, 0), subsurf=0)
        cone('logend', 0.052, 0.052, 0.015, mat('dfc08c', 0.7), lg, (0, 0, 0.005), down=False, seg=10, subsurf=0)
    P['wood'] = [logs]

    # 3) 석재 덩어리 (앞쪽 가슴에 안기)
    rock = sphere('carry_rock', 0.18, mat('8e9ba0', 0.82), sp, (0, -0.26, 0.22), scale=(1.15, 0.88, 0.92), seg=7, rings=5)
    P['stone'] = [rock]
    return P


# ───────── 자세 ─────────

def R(o, x=0, y=0, z=0):
    S[o].rotation_euler = (math.radians(x), math.radians(y), math.radians(z))


def rest():
    for n, o in S.items():
        if n != 'root':
            o.rotation_euler = (0, 0, 0)
    S['hips'].location = (0, 0, 0.92)


def legs_walk(p, amp=30):
    s = math.sin(p)
    R('hpL', -amp * s)
    R('hpR', amp * s)
    kl = 8 + 42 * max(0.0, math.cos(p)) ** 2 if s > -0.2 else 10
    kr = 8 + 42 * max(0.0, -math.cos(p)) ** 2 if s < 0.2 else 10
    R('knL', kl)
    R('knR', kr)
    R('anL', amp * s - kl * 0.58)
    R('anR', -amp * s - kr * 0.58)
    S['hips'].location = (0, 0, 0.90 + 0.035 * abs(math.sin(p)))


def pose(anim, t, body='', prop=''):
    """anim의 t(0~1) 순간 자세."""
    rest()
    p = 2 * math.pi * t
    is_soldier = body == 'soldier'

    if anim == 'idle':
        s = math.sin(p)
        S['hips'].location = (0, 0, 0.92 + 0.012 * s)
        R('spine', 2 + 1.5 * s, 0, 2 * math.cos(p))
        R('head', -1 * s, 0, 5 * math.sin(p))
        R('hpL', 0, 0, -5)
        R('hpR', 0, 0, 5)
        if is_soldier:
            R('shR', -18 + 2 * s, 5)
            R('elR', -42)
            R('hdR', 55)
            R('shL', -12 - 2 * s, -8)
            R('elL', -34)
        else:
            R('shR', 4 + 2.5 * s, 7)
            R('shL', 4 - 2.5 * s, -7)
            R('elR', -14)
            R('elL', -14)
        return

    if anim == 'walk':
        legs_walk(p, 30)
        s = math.sin(p)
        R('hips', 0, 0, -7 * s)
        R('spine', 4, 0, 9 * s)
        R('head', -2, 0, -4 * s)
        if is_soldier:
            R('shR', -16 + 8 * s, 6)
            R('elR', -38)
            R('hdR', 50)
            R('shL', -20 - 8 * s, -6)
            R('elL', -36)
        else:
            R('shL', 28 * s, -6)
            R('shR', -28 * s, 6)
            R('elL', -14 - 12 * max(0, -s))
            R('elR', -14 - 12 * max(0, s))
        return

    if anim == 'carry':
        legs_walk(p, 22)
        s = math.sin(p)
        R('hips', 0, 0, -4 * s)
        R('spine', 10, 0, 4 * s)
        R('head', -7, 0, -2 * s)
        if prop == 'wood':
            # 오른손은 어깨 위 통나무를 받치고, 왼팔은 자연스럽게 흔든다
            R('shR', -64, 10, -8)
            R('elR', -92)
            R('shL', 20 * s, -6)
            R('elL', -16 - 8 * max(0, -s))
        else:
            # 식량 바구니 / 석재를 두 팔로 몸 앞에 안는다
            R('shL', -52, -14, 4 * s)
            R('shR', -52, 14, -4 * s)
            R('elL', -58)
            R('elR', -58)
        return

    def curve(keys):
        for (t0, v0), (t1, v1) in zip(keys, keys[1:]):
            if t0 <= t <= t1:
                u = (t - t0) / (t1 - t0)
                u = u * u * (3 - 2 * u)
                return v0 + (v1 - v0) * u
        return keys[-1][1]

    stance = lambda: (R('hpL', -16, 0, -5), R('hpR', 14, 0, 5), R('knL', 14), R('knR', 10), R('anL', 4), R('anR', -20))

    if anim in ('chop', 'pick'):
        stance()
        a = curve([(0, -68), (0.38, -148), (0.52, -36), (0.64, -32), (0.82, -76), (1, -68)])
        e = curve([(0, -22), (0.38, -22), (0.52, -10), (0.64, -12), (1, -22)])
        lean = curve([(0, 8), (0.38, -6), (0.52, 26), (0.64, 26), (1, 8)])
        R('shR', a, 22)
        R('shL', a, -22)
        R('elR', e)
        R('elL', e)
        R('spine', lean)
        R('head', -lean * 0.4)
        S['hips'].location = (0, 0, 0.90 - 0.06 * max(0.0, lean) / 26)
        return

    if anim == 'hoe':
        stance()
        a = curve([(0, -45), (0.34, -98), (0.54, -34), (0.76, -18), (1, -45)])
        e = curve([(0, -24), (0.34, -14), (0.54, -14), (0.76, -54), (1, -24)])
        lean = curve([(0, 12), (0.34, 4), (0.54, 24), (0.76, 20), (1, 12)])
        R('shR', a, 20)
        R('shL', a, -20)
        R('elR', e)
        R('elL', e)
        R('spine', lean)
        R('head', -lean * 0.4)
        S['hips'].location = (0, 0, 0.90 - 0.04 * max(0.0, lean) / 24)
        return

    if anim == 'hammer':
        R('hpL', -42, 0, -6); R('knL', 76); R('anL', -34)
        R('hpR', 24, 0, 6); R('knR', 92); R('anR', -25)
        S['hips'].location = (0, 0, 0.72)
        R('spine', 22)
        R('head', -12)
        R('shL', -52, -12)
        R('elL', -36)
        a = curve([(0, -58), (0.18, -110), (0.30, -44), (0.48, -105), (0.62, -44), (0.80, -68), (1, -58)])
        e = curve([(0, -35), (0.18, -32), (0.30, -14), (0.48, -30), (0.62, -14), (1, -35)])
        R('shR', a, 8)
        R('elR', e)
        return

    if anim == 'bow':
        R('hpL', -14, 0, -6); R('hpR', 18, 0, 6); R('knL', 12); R('knR', 10)
        R('hips', 0, 0, -35)
        R('spine', 6, 0, -15)
        R('head', 0, 0, 46)
        # 왼팔은 표적(-Y) 쪽으로 활을 곧게 뻗는다
        R('shL', -86, 0, 44)
        R('elL', -6)
        # 오른팔은 화살을 당겼다가 발사
        draw = curve([(0, 0.15), (0.28, 0.85), (0.52, 1.0), (0.58, 0.10), (0.74, 0.05), (1, 0.15)])
        S['arrow'].location = (0, 0, 0.14 + 0.30 * draw)
        R('shR', -84, 16, 26 - 52 * draw)
        R('elR', -26 - 78 * draw)
        R('hdR', 0, 0, -40 + 35 * draw)
        return

    if anim == 'fish':
        stance()
        a = curve([(0, -105), (0.40, -118), (0.55, -56), (0.72, -62), (1, -105)])
        e = curve([(0, -50), (0.40, -56), (0.55, -6), (0.72, -14), (1, -50)])
        lean = curve([(0, 14), (0.40, 8), (0.55, 28), (0.72, 24), (1, 14)])
        R('shR', a, 8); R('elR', e)
        R('shL', a + 8, -18); R('elL', e * 0.65)
        R('spine', lean)
        R('head', -lean * 0.5)
        S['hdR'].rotation_euler = (math.radians(-46 - (a + e)), 0, 0)
        return

    if anim == 'gather':
        R('hpL', -65, 0, -8); R('knL', 95); R('anL', -28)
        R('hpR', -45, 0, 8); R('knR', 105); R('anR', -45)
        S['hips'].location = (0, 0, 0.64)
        R('spine', 30)
        R('head', -16)
        R('shR', -50 + 22 * math.sin(p), 16); R('elR', -22 + 14 * math.cos(p))
        R('shL', -45 - 18 * math.sin(p), -10); R('elL', -30)
        return

    if anim == 'scout':
        R('hpL', -10, 0, -6); R('hpR', 12, 0, 6); R('knL', 10); R('knR', 8)
        scan = math.sin(p)
        R('spine', 6, 0, 20 * scan)
        R('head', -6, 0, 14 * scan)
        R('shL', -20, -18); R('elL', -45)
        R('shR', -84, 14, -8); R('elR', -106); R('hdR', 100, 0, 0)
        return

    if anim == 'guard':
        R('hpL', -8, 0, -6); R('hpR', 8, 0, 6); R('knL', 8); R('knR', 8)
        scan = math.sin(p)
        R('spine', 3, 0, 14 * scan)
        R('head', -2, 0, 18 * scan)
        R('shL', -48, -14, -10); R('elL', -48)
        R('shR', -32 + 8 * math.cos(p), 6); R('elR', -52); R('hdR', 84)
        return

    if anim == 'play':
        s = math.sin(p)
        S['hips'].location = (0, 0, 0.92 + 0.12 * abs(s))
        R('hpL', -28 * s, 0, -6); R('hpR', 28 * s, 0, 6)
        R('knL', 20 + 28 * max(0.0, s)); R('knR', 20 + 28 * max(0.0, -s))
        R('spine', 6, 0, 10 * s)
        R('head', -4, 0, -6 * s)
        R('shL', -110 + 35 * s, -12); R('shR', -110 - 35 * s, 12)
        R('elL', -24); R('elR', -24)
        return

    raise ValueError(anim)


WORK = ['chop', 'pick', 'hoe', 'hammer', 'bow', 'fish', 'gather', 'scout']
CARRY = ['food', 'wood', 'stone']


def rows_for(body):
    rows = []
    for d, _ in DIRS:
        rows.append(('idle', d, None, IDLE_FRAMES))
    for d, _ in DIRS:
        rows.append(('walk', d, None, FRAMES))
    if body == 'child':
        for d, _ in [('S', 0), ('SE', 45), ('E', 90)]:
            rows.append(('play', d, None, FRAMES))
        return rows
    if body == 'soldier':
        for d, _ in WORK_DIRS:
            rows.append(('guard', d, None, FRAMES))
        return rows
    for c in CARRY:
        for d, _ in DIRS:
            rows.append(('carry', d, c, FRAMES))
    for w in WORK:
        for d, _ in WORK_DIRS:
            rows.append((w, d, w, FRAMES))
    return rows


def setup_scene():
    sc = reset()
    sc.cycles.samples = 18
    sc.render.use_persistent_data = True
    lights(sun_strength=4.1, sky=(0.64, 0.74, 0.86), sky_strength=0.95)
    sun = bpy.data.objects['sun']
    sun.rotation_euler = (math.radians(18), math.radians(-7), math.radians(-12))
    bpy.data.lights['sun'].angle = math.radians(11)
    camera(PX, ORTHO, GROUND)
    return sc


def make(body, preview=False, split_meta=False):
    setup_scene()
    common._mats.clear()
    S.clear()
    build(BODIES[body])
    P = props()
    allprops = {o for v in P.values() for o in v}
    rows = rows_for(body)
    if preview:
        pick = [('idle', 'S', None, IDLE_FRAMES), ('walk', 'SE', None, 8),
                ('carry', 'SE', 'food', 8), ('carry', 'E', 'wood', 8), ('carry', 'S', 'stone', 8),
                ('chop', 'SE', 'chop', 8), ('bow', 'SE', 'bow', 8), ('hammer', 'SE', 'hammer', 8),
                ('scout', 'E', 'scout', 8), ('guard', 'SE', None, 8), ('play', 'SE', None, 8)]
        rows = [r for r in pick if r in rows] or rows[:4]
    os.makedirs(TMP, exist_ok=True)
    sheet = np.zeros((PX * len(rows), PX * FRAMES, 4), dtype=np.float32)
    meta = {}
    dir_map = dict(DIRS)
    for ri, (anim, d, prop, n) in enumerate(rows):
        set_visible(allprops, False)
        if prop:
            set_visible(P[prop], True)
        S['root'].rotation_euler = (0, 0, math.radians(dir_map[d]))
        name = f'{anim}_{prop}' if anim == 'carry' else anim
        meta[f'{name}_{d}'] = [ri, n]
        frames = [0, 2, 4, 6][:n] if preview and n > 1 else range(n)
        for f in frames:
            if prop == 'bow':
                # 발사 직후 손 안의 화살 숨기기
                t = f / n
                set_visible(P['bow_arrow'], not (0.58 <= t <= 0.84))
            pose(anim, f / n, body=body, prop=prop or '')
            path = os.path.join(TMP, f'{body}_{ri}_{f}.png')
            render_to(path)
            sheet[ri * PX:(ri + 1) * PX, f * PX:(f + 1) * PX] = outline(load_rgba(path))
    os.makedirs(OUT, exist_ok=True)
    out = os.path.join(TMP if preview else OUT, f'people_{body}.png')
    save_rgba(sheet, out)
    if not preview and split_meta:
        mpath = os.path.join(OUT, f'people_{body}.meta.json')
        with open(mpath, 'w', encoding='utf-8') as fp:
            json.dump(meta, fp, ensure_ascii=False, indent=1)
    print('WROTE', out, len(rows), 'rows')
    return meta


def merge_meta():
    path = os.path.join(OUT, 'people.json')
    bodies = {}
    if os.path.exists(path):
        bodies = json.load(open(path, encoding='utf-8')).get('bodies', {})
    for b in BODIES:
        mpath = os.path.join(OUT, f'people_{b}.meta.json')
        if os.path.exists(mpath):
            bodies[b] = json.load(open(mpath, encoding='utf-8'))
            os.remove(mpath)
    info = dict(frame=PX, ground=GROUND, frames=FRAMES, dirs=[d for d, _ in DIRS], bodies=bodies)
    with open(path, 'w', encoding='utf-8') as fp:
        json.dump(info, fp, ensure_ascii=False, indent=1)
    print('WROTE', path, 'bodies:', list(bodies.keys()))


if __name__ == '__main__':
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    mode = argv[0] if argv else 'all'
    if mode == 'merge':
        merge_meta()
    elif mode == 'preview':
        names = [a for a in argv[1:] if a in BODIES] or ['man', 'hunter', 'chief', 'soldier', 'child']
        for b in names:
            make(b, preview=True)
    elif mode == 'body':
        names = [a for a in argv[1:] if a in BODIES]
        for b in names:
            make(b, preview=False, split_meta=True)
    else:
        names = [a for a in argv if a in BODIES] or list(BODIES)
        for b in names:
            make(b, preview=False, split_meta=True)
        merge_meta()
