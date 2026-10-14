# Paints the King Cobra's textures (albedo+smoothness, normal, emission) into
# UnityProject/Assets/Resources/Cobra.  python tools/cobra/paint_cobra.py
#   Body  - u around the body (0 = spine, 0.5 = belly), v along it (tiles): overlapping stone-plate
#           scales in staggered rows, king-cobra chevron bands, cream ventral scutes, ember seams
#   Hood  - u around the hood tube (warped like CobraMesh.HoodWarp), v from the head down: the
#           spectacle mark on its back, a banded cream throat in front, stretched scales
#   Head  - left half the skull (u = lip -> crown -> lip, v = back -> snout), right half the jaw:
#           the head shields (occipitals, parietals, frontal, supraoculars, prefrontals,
#           internasals, rostral), temporals, pale lip scales, nostrils, chin shields
#   Mouth - flesh with palate ridges, glowing like a furnace toward the throat
#   Eye   - a fiery iris with a round black pupil at the front pole
import os, sys
import numpy as np
from PIL import Image

OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), '..', '..', 'UnityProject', 'Assets', 'Resources', 'Cobra')
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(7)


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def pnoise(h, w, cells, seed, octaves=4, wrap_v=True):
    """periodic fractal noise, roughly unit variance; cells = feature count across the tile"""
    r = np.random.default_rng(seed)
    total = np.zeros((h, w))
    amp, norm = 1.0, 0.0
    ky = np.fft.fftfreq(h)[:, None] * h
    kx = np.fft.fftfreq(w)[None, :] * w
    k = np.sqrt(kx ** 2 + ky ** 2)
    for o in range(octaves):
        f = cells * 2 ** o
        n = np.real(np.fft.ifft2(np.fft.fft2(r.standard_normal((h, w))) * np.exp(-(k / f) ** 2)))
        n /= n.std() + 1e-9
        total += amp * n
        norm += amp
        amp *= 0.5
    return total / norm


def scale_field(H, W, nu, nv, jit=0.18, back=0.8, fwd=1.25, ax=0.55, ay=0.62, seed=1, u_scale=None, p=2.0):
    """staggered, jittered shingle scales. returns id, d1, edge(d2-d1), du, dv (cell units)"""
    r = np.random.default_rng(seed)
    ju = r.uniform(-jit, jit, nu * nv)
    jv = r.uniform(-jit, jit, nu * nv)
    U = (np.arange(W) + 0.5) / W
    V = (np.arange(H) + 0.5) / H
    uu, vv = np.meshgrid(U, V)
    cu, cv = uu * nu, vv * nv
    j0 = np.floor(cv).astype(int)
    best = np.full((H, W), 1e9); second = np.full((H, W), 1e9)
    bid = np.zeros((H, W), int); bdu = np.zeros((H, W)); bdv = np.zeros((H, W))
    for dj in (-1, 0, 1):
        j = j0 + dj
        off = 0.5 * (np.mod(j, 2))
        i0 = np.floor(cu - off).astype(int)
        for di in (-1, 0, 1):
            i = i0 + di
            sid = np.mod(j, nv) * nu + np.mod(i, nu)
            cx = i + off + 0.5 + ju[sid]
            cy = j + 0.5 + jv[sid]
            du = cu - cx
            dv = cv - cy
            dvm = np.where(dv > 0, dv * back, dv * fwd)
            d = (np.abs(du / ax) ** p + np.abs(dvm / ay) ** p) ** (1 / p)
            closer = d < best
            second = np.where(closer, best, np.minimum(second, d))
            bid = np.where(closer, sid, bid)
            bdu = np.where(closer, du, bdu)
            bdv = np.where(closer, dv, bdv)
            best = np.where(closer, d, best)
    return bid, best, second - best, bdu, bdv


def normal_from_height(h, strength, wrap_u=True, wrap_v=True):
    gy, gx = np.gradient(h)
    if wrap_u:
        gx[:, 0] = (h[:, 1] - h[:, -1]) * 0.5
        gx[:, -1] = (h[:, 0] - h[:, -2]) * 0.5
    if wrap_v:
        gy[0, :] = (h[1, :] - h[-1, :]) * 0.5
        gy[-1, :] = (h[0, :] - h[-2, :]) * 0.5
    n = np.dstack([-gx * strength, -gy * strength, np.ones_like(h)])
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    return n


def save(name, albedo, smooth_map, nrm, emis):
    """arrays are indexed [v, u] with v = 0 at the bottom; flipped for the image"""
    a = np.dstack([np.clip(albedo, 0, 1), np.clip(smooth_map, 0, 1)[..., None]])
    Image.fromarray((np.flipud(a) * 255 + 0.5).astype(np.uint8), 'RGBA').save(os.path.join(OUT, name + '_A.png'))
    if nrm is not None:
        Image.fromarray((np.flipud(nrm * 0.5 + 0.5) * 255 + 0.5).astype(np.uint8), 'RGB').save(os.path.join(OUT, name + '_N.png'))
    if emis is not None:
        Image.fromarray((np.flipud(np.clip(emis, 0, 1)) * 255 + 0.5).astype(np.uint8), 'RGB').save(os.path.join(OUT, name + '_E.png'))
    print('saved', name)


EMBER = np.array([1.0, 0.36, 0.07])
BASALT = np.array([0.075, 0.08, 0.09])
SAND = np.array([0.56, 0.5, 0.4])
CREAM = np.array([0.6, 0.55, 0.44])


# =============================================================================== BODY
def body():
    H = W = 1024
    nu, nv = 26, 20
    sid, d1, edge, du, dv = scale_field(H, W, nu, nv, seed=3, jit=0.1, back=0.85, fwd=1.2, ax=0.5, ay=0.58, p=1.3)
    U = (np.arange(W) + 0.5) / W
    V = (np.arange(H) + 0.5) / H
    uu, vv = np.meshgrid(U, V)
    grain = pnoise(H, W, 90, 11, 3)
    mott = pnoise(H, W, 6, 12, 3)
    r = np.random.default_rng(5)
    tone = r.uniform(0.75, 1.3, nu * nv)[sid]
    # the scale's centre, for the chevron bands (whole scales are banded, like a real snake)
    cu = ((sid % nu) + 0.5 + 0.5 * ((sid // nu) % 2)) / nu
    cv = ((sid // nu) + 0.5) / nv
    us = np.minimum(cu % 1.0, 1 - cu % 1.0)
    band = (np.mod((cv - 0.9 * us) * 2, 1.0) < 0.17) & (us < 0.36)
    # height: each scale rises from where it tucks under the one in front to its free rear lip
    groove = smooth(0.0, 0.07, edge)
    rise = np.clip(dv + 0.55, 0, 1.1)
    h = (np.sqrt(np.clip(1 - (d1 / 1.2) ** 2, 0, 1)) * 0.45 + 0.55 * rise) * groove + 0.02 * grain
    tuck = 0.62 + 0.38 * smooth(-0.6, 0.05, dv)      # shadowed where it slides under the scale ahead
    lip = smooth(0.1, 0.02, edge) * smooth(0.0, 0.35, dv)   # the bright worn rear edge
    col = BASALT[None, None, :] * tone[..., None] * (1 + 0.12 * mott[..., None])
    col = np.where(band[..., None], SAND[None, None, :] * (0.85 + 0.25 * (tone[..., None] - 1)), col)
    col = col * tuck[..., None] + lip[..., None] * 0.05
    col = col * (0.92 + 0.08 * grain[..., None])
    col = col * (0.3 + 0.7 * groove[..., None])
    sm = 0.66 * groove + 0.1 + 0.04 * grain
    sm = np.where(band, sm * 0.8, sm)
    # the belly: broad cream ventral scutes
    bel = np.abs(uu - 0.5)
    inbelly = bel < 0.118
    fv = np.mod(vv * nv + 0.35, 1.0)
    plate = np.floor(vv * nv + 0.35).astype(int) % nv
    ptone = np.random.default_rng(9).uniform(0.85, 1.12, nv)[plate]
    ph = (0.35 + 0.65 * fv ** 0.8) * smooth(0.0, 0.08, fv) * smooth(0.118, 0.095, bel)
    pcol = CREAM[None, None, :] * ptone[..., None] * (0.9 + 0.1 * grain[..., None])
    pcol = pcol * (0.3 + 0.7 * smooth(0.0, 0.06, fv)[..., None])
    blend = smooth(0.125, 0.105, bel)
    h = h * (1 - blend) + ph * blend
    col = col * (1 - blend[..., None]) + pcol * blend[..., None]
    sm = sm * (1 - blend) + (0.55 * smooth(0.0, 0.06, fv) + 0.15) * blend
    # molten cracks: only some seams glow, in veins that wander across the body
    seam = 1 - smooth(0.0, 0.045, edge)
    seam = seam * (1 - blend) + (1 - smooth(0.0, 0.05, fv)) * blend * 0.4
    veins = smooth(0.35, 1.1, pnoise(H, W, 4, 14, 3))
    glow = seam * (0.06 + 0.94 * veins)
    glow = glow * np.where(band, 1.3, 1.0)
    emis = EMBER[None, None, :] * glow[..., None]
    nrm = normal_from_height(h, 7.0)
    save('Body', col, sm, nrm, emis)


# =============================================================================== HOOD
def hood_warp(t):
    return 2 * np.pi * t - 0.3 * np.sin(4 * np.pi * t)


def hood():
    H = W = 1024
    U = (np.arange(W) + 0.5) / W
    V = (np.arange(H) + 0.5) / H
    uu, vv = np.meshgrid(U, V)
    ph = hood_warp(uu)
    X = np.sin(ph)            # lateral, -1..1 across the hood
    front = np.cos(ph) < 0    # the throat side, facing the prey
    # scales in hood space: (X across, v down), a finer lattice near the rim
    nu, nv = 44, 24
    sid, d1, edge, du, dv = scale_field(H, W, nu, nv, seed=21, jit=0.12, p=1.4)
    grain = pnoise(H, W, 80, 22, 3)
    mott = pnoise(H, W, 5, 23, 3)
    tone = np.random.default_rng(24).uniform(0.78, 1.25, nu * nv)[sid]
    skin = smooth(0.02, 0.14, edge)   # stretched skin shows between the scales
    h = (np.sqrt(np.clip(1 - (d1 / 1.1) ** 2, 0, 1)) * 0.6 + 0.2 * np.clip(dv + 0.5, 0, 1)) * skin + 0.02 * grain
    # --- the back: dark, with the spectacle mark
    col = BASALT[None, None, :] * tone[..., None] * (1 + 0.15 * mott[..., None])
    ax = np.abs(X)
    # two lenses joined by a bridge dipping across the spine
    lens = np.sqrt(((ax - 0.42) / 0.16) ** 2 + ((vv - 0.36) / 0.11) ** 2)
    ring = np.exp(-((lens - 1.0) / 0.22) ** 2)
    eye_dark = smooth(0.9, 0.6, lens)
    bridge_y = 0.47 - 0.11 * (ax / 0.42) ** 2
    bridge = np.exp(-((vv - bridge_y) / 0.028) ** 2) * (ax < 0.36)
    mark = np.clip(ring + bridge, 0, 1)
    back_col = col * (1 - mark[..., None]) + SAND[None, None, :] * 1.15 * mark[..., None]
    back_col = back_col * (1 - 0.6 * eye_dark[..., None])
    # --- the front: a cream throat crossed by dark bands
    tcol = CREAM[None, None, :] * (0.92 + 0.16 * (tone[..., None] - 1)) * (1 + 0.08 * mott[..., None])
    bands = np.exp(-((vv - 0.5) / 0.05) ** 2) + np.exp(-((vv - 0.68) / 0.04) ** 2) * 0.85
    bands = bands * smooth(0.95, 0.55, ax)
    spots = smooth(0.35, 0.8, pnoise(H, W, 9, 25, 2)) * smooth(0.6, 0.9, vv)
    tcol = tcol * (1 - 0.8 * np.clip(bands + 0.6 * spots, 0, 1)[..., None])
    col = np.where(front[..., None], tcol, back_col)
    col = col * (0.3 + 0.7 * skin[..., None]) * (0.92 + 0.08 * grain[..., None])
    rimdark = smooth(0.9, 1.0, ax)
    col = col * (1 - 0.35 * rimdark[..., None])
    sm = 0.6 * skin + 0.12 + 0.04 * grain
    # the spectacle smoulders; the seams glow faintly
    seam = (1 - smooth(0.0, 0.06, edge)) * (0.05 + 0.95 * smooth(0.4, 1.1, pnoise(H, W, 4, 26, 2)))
    emis = EMBER[None, None, :] * (seam * 0.6)[..., None]
    emis = emis + (EMBER * np.array([1.0, 0.8, 0.6]))[None, None, :] * (ring * (~front) * 0.55)[..., None]
    nrm = normal_from_height(h, 6.0, wrap_v=False)
    save('Hood', col, sm, nrm, emis)


# =============================================================================== HEAD
def power_diagram(P, Wt, pts):
    """P: (H, W, 2) physical coords; pts: (n, 2) seeds; Wt: seed radii. returns id, edge"""
    best = np.full(P.shape[:2], 1e9); second = np.full(P.shape[:2], 1e9); bid = np.zeros(P.shape[:2], int)
    for k, (p, w) in enumerate(zip(pts, Wt)):
        d = np.sqrt(((P[..., 0] - p[0]) ** 2 + (P[..., 1] - p[1]) ** 2)) - w
        closer = d < best
        second = np.where(closer, best, np.minimum(second, d))
        bid = np.where(closer, k, bid)
        best = np.where(closer, d, best)
    return bid, second - best, best


def head():
    H = W = 1024
    half = W // 2
    V = (np.arange(H) + 0.5) / H
    # ---------------------------------------------------------------- the skull (left half)
    Us = (np.arange(half) + 0.5) / half            # 0 right lip -> 0.5 crown -> 1 left lip
    uu, vv = np.meshgrid(Us, V)
    X = 1 - 2 * uu                                 # +1 right lip .. 0 crown .. -1 left lip
    Z = vv * 1.08 - 0.08                           # back of the skull .. snout
    P = np.dstack([X * 0.5, Z * 1.15])             # roughly physical (cobra units)
    seeds, rad, kind = [], [], []

    def add(x, z, r, k, mirror=True):
        seeds.append((x * 0.5, z * 1.15)); rad.append(r); kind.append(k)
        if mirror and abs(x) > 1e-3:
            seeds.append((-x * 0.5, z * 1.15)); rad.append(r); kind.append(k)

    # the big shields on the crown
    add(0.0, 1.0, 0.07, 'rostral', False)
    add(0.12, 0.905, 0.05, 'top')          # internasals
    add(0.15, 0.8, 0.065, 'top')           # prefrontals
    add(0.0, 0.62, 0.085, 'frontal', False)
    add(0.27, 0.63, 0.07, 'top')           # supraoculars
    add(0.17, 0.39, 0.12, 'top')           # parietals
    add(0.14, 0.17, 0.085, 'top')          # the king cobra's occipitals
    # the sides of the head
    add(0.44, 0.91, 0.04, 'nasal')
    add(0.56, 0.77, 0.035, 'side')         # preocular
    add(0.6, 0.53, 0.025, 'side')          # postoculars
    add(0.52, 0.56, 0.022, 'side')
    for z, x in ((0.43, 0.5), (0.33, 0.62), (0.2, 0.5), (0.25, 0.72), (0.4, 0.73)):
        add(x, z, 0.04, 'side')            # temporals
    for k in range(7):                     # supralabials along the lip
        add(0.9, 0.22 + k * 0.11, 0.045, 'lip')
    # the neck scales behind the skull
    for z in (-0.06, 0.02):
        for x in np.linspace(0.05, 0.95, 7):
            add(x + (0.06 if z > 0 else 0), z, 0.02, 'neck')
    bid, edge, _ = power_diagram(P, np.array(rad), np.array(seeds))
    kinds = np.array(kind)[bid]
    grain = pnoise(H, half, 60, 31, 3)
    tone = np.random.default_rng(32).uniform(0.85, 1.2, len(seeds))[bid]
    col = BASALT[None, None, :] * tone[..., None]
    col = np.where((kinds == 'lip')[..., None], CREAM[None, None, :] * 0.95 * tone[..., None], col)
    col = np.where((kinds == 'rostral')[..., None], SAND[None, None, :] * 0.7 * tone[..., None], col)
    col = np.where((kinds == 'nasal')[..., None], BASALT[None, None, :] * 1.5 * tone[..., None], col)
    groove = smooth(0.0, 0.012, edge)
    # the nostrils
    nost = np.exp(-(((np.abs(X) - 0.46) / 0.05) ** 2 + ((Z - 0.915) / 0.022) ** 2))
    col = col * (1 - 0.85 * nost[..., None])
    # the orbit: a dark ring around where the eyeball sits
    orbit = np.sqrt(((np.abs(X) - 0.72) / 0.1) ** 2 + ((Z - 0.64) / 0.07) ** 2)
    col = col * (1 - 0.6 * np.exp(-((orbit - 1) / 0.35) ** 2)[..., None])
    col = col * (0.25 + 0.75 * groove[..., None]) * (0.93 + 0.07 * grain[..., None])
    rim = smooth(0.004, 0.012, edge) * (1 - smooth(0.012, 0.03, edge))
    col = col + rim[..., None] * 0.03
    hgt = groove * (0.7 + 0.3 * np.clip(edge / 0.08, 0, 1)) - 0.6 * nost + 0.02 * grain
    sm = 0.1 + 0.62 * groove
    seam = (1 - smooth(0.0, 0.01, edge)) * (0.1 + 0.9 * smooth(0.3, 1.0, pnoise(H, half, 4, 33, 2)))
    emis_s = EMBER[None, None, :] * (seam * 0.5)[..., None]
    col_s, sm_s, h_s = col, sm, hgt

    # ---------------------------------------------------------------- the jaw (right half)
    X = 1 - 2 * uu                                 # +1 right lip .. 0 chin .. -1 left lip
    Z = vv * 0.97
    P = np.dstack([X * 0.45, Z * 1.15])
    seeds, rad, kind = [], [], []
    add(0.0, 0.97, 0.04, 'mental', False)
    add(0.12, 0.85, 0.06, 'chin')
    add(0.13, 0.68, 0.06, 'chin')
    for k in range(7):
        add(0.88, 0.25 + k * 0.1, 0.04, 'ilip')
    r2 = np.random.default_rng(34)
    for z in np.arange(0.02, 0.6, 0.075):
        for x in np.arange(0.0, 0.75, 0.13):
            add(x + r2.uniform(-0.03, 0.03) + (0.065 if int(z / 0.075) % 2 else 0), z + r2.uniform(-0.015, 0.015), 0.02, 'gular')
    bid, edge, _ = power_diagram(P, np.array(rad), np.array(seeds))
    kinds = np.array(kind)[bid]
    tone = np.random.default_rng(35).uniform(0.85, 1.15, len(seeds))[bid]
    grain = pnoise(H, half, 60, 36, 3)
    col = CREAM[None, None, :] * 0.9 * tone[..., None]
    col = np.where((kinds == 'ilip')[..., None], CREAM[None, None, :] * 1.0 * tone[..., None], col)
    mott = smooth(0.4, 0.9, pnoise(H, half, 7, 37, 2)) * smooth(0.35, 0.05, Z)
    col = col * (1 - 0.6 * mott[..., None])
    groove = smooth(0.0, 0.012, edge)
    col = col * (0.3 + 0.7 * groove[..., None]) * (0.93 + 0.07 * grain[..., None])
    hgt = groove * (0.7 + 0.3 * np.clip(edge / 0.08, 0, 1)) + 0.02 * grain
    sm = 0.12 + 0.5 * groove
    seam = (1 - smooth(0.0, 0.01, edge)) * 0.15
    emis_j = EMBER[None, None, :] * seam[..., None]

    col = np.concatenate([col_s, col], axis=1)
    sm = np.concatenate([sm_s, sm], axis=1)
    hgt = np.concatenate([h_s, hgt], axis=1)
    emis = np.concatenate([emis_s, emis_j], axis=1)
    nrm = normal_from_height(hgt, 5.0, wrap_u=False, wrap_v=False)
    save('Head', col, sm, nrm, emis)


# =============================================================================== MOUTH / EYE
def mouth():
    H = W = 256
    U = (np.arange(W) + 0.5) / W
    V = (np.arange(H) + 0.5) / H
    uu, vv = np.meshgrid(U, V)
    ridge = 0.5 + 0.5 * np.cos((uu - 0.5) * np.pi * 9) * smooth(0.45, 0.2, np.abs(uu - 0.5))
    folds = 0.5 + 0.5 * np.sin(vv * np.pi * 16)
    n = pnoise(H, W, 12, 41, 3)
    col = np.array([0.42, 0.07, 0.08])[None, None, :] * (0.8 + 0.25 * ridge[..., None] * folds[..., None] + 0.08 * n[..., None])
    throat = smooth(0.5, 0.0, vv)
    col = col * (1 - 0.5 * throat[..., None]) + np.array([0.9, 0.3, 0.05])[None, None, :] * throat[..., None] * 0.4
    sm = np.full((H, W), 0.85)
    emis = np.array([1.0, 0.34, 0.06])[None, None, :] * (throat ** 1.5 * (0.8 + 0.2 * n))[..., None]
    nrm = normal_from_height(ridge * folds * 0.6 + 0.05 * n, 4.0, wrap_u=False, wrap_v=False)
    save('Mouth', col, sm, nrm, emis)


def eye():
    H = W = 256
    U = (np.arange(W) + 0.5) / W
    V = (np.arange(H) + 0.5) / H
    uu, vv = np.meshgrid(U, V)
    streak = pnoise(H, W, 40, 51, 2)
    iris = smooth(0.7, 0.76, vv) * smooth(0.95, 0.925, vv)
    ring = np.exp(-((vv - 0.765) / 0.012) ** 2)
    pupil = smooth(0.925, 0.94, vv)
    fire = np.array([1.0, 0.55, 0.08]) * 1.0
    col = np.array([0.04, 0.03, 0.03])[None, None, :] * np.ones((H, W, 1))
    icol = fire[None, None, :] * (0.7 + 0.3 * streak[..., None]) * (0.6 + 0.4 * smooth(0.76, 0.92, vv)[..., None])
    col = col * (1 - iris[..., None]) + icol * iris[..., None]
    col = col * (1 - 0.8 * ring[..., None]) * (1 - pupil[..., None])
    sm = np.full((H, W), 0.95)
    emis = icol * iris[..., None] * (1 - pupil[..., None])
    save('Eye', col, sm, None, emis)


# =============================================================================== PLATES
def plates():
    """rough-hewn stone slabs with molten seams: the stone ball's core, the wall's blocks"""
    H = W = 1024
    sid, d1, edge, du, dv = scale_field(H, W, 6, 6, jit=0.3, back=1.0, fwd=1.0, ax=0.6, ay=0.6, seed=71, p=1.6)
    grain = pnoise(H, W, 70, 72, 4)
    mott = pnoise(H, W, 5, 73, 3)
    tone = np.random.default_rng(74).uniform(0.75, 1.25, 36)[sid]
    bevel = smooth(0.0, 0.16, edge)
    h = bevel * (0.8 + 0.2 * np.clip(1 - d1, 0, 1)) + 0.06 * grain
    base = np.array([0.34, 0.31, 0.28])
    col = base[None, None, :] * tone[..., None] * (1 + 0.15 * mott[..., None]) * (0.9 + 0.1 * grain[..., None])
    col = col * (0.3 + 0.7 * bevel[..., None])
    sm = 0.12 + 0.3 * bevel + 0.05 * grain
    veins = smooth(0.2, 1.0, pnoise(H, W, 3, 75, 3))
    glow = (1 - smooth(0.0, 0.05, edge)) * (0.1 + 0.9 * veins)
    emis = EMBER[None, None, :] * glow[..., None]
    nrm = normal_from_height(h, 6.0)
    save('Plates', col, sm, nrm, emis)


if __name__ == '__main__':
    body(); hood(); head(); mouth(); eye(); plates()
