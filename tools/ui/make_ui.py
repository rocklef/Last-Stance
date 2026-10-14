# Paints the phone controls in three styles and composes mockups on a real game frame.
#   python tools/ui/make_ui.py            -> tools/ui/out/<style>/*.png + tools/ui/out/mockup_<style>.png
#   python tools/ui/make_ui.py install A  -> copies style A's sprites into Assets/Resources/UI
# Styles: A Ember Stone, B Sumi Ink, C Spirit Glass.
import os, sys, math, shutil
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter, ImageChops

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
OUT = os.path.join(HERE, 'out')
RES = os.path.join(ROOT, 'UnityProject', 'Assets', 'Resources', 'UI')
KANJI = r'C:\Windows\Fonts\YuGothB.ttc'
BANGERS = os.path.join(ROOT, 'UnityProject', 'Assets', 'External', 'fonts', 'Bangers-Regular.ttf')
PLATES = os.path.join(ROOT, 'UnityProject', 'Assets', 'Resources', 'Cobra', 'Plates_A.png')
PLATES_E = os.path.join(ROOT, 'UnityProject', 'Assets', 'Resources', 'Cobra', 'Plates_E.png')
S = 512            # sprite size
SS = 2             # supersampling

# the buttons: name, kanji, label, role
BUTTONS = [
    ('attack', '攻', 'ATTACK', 'fire'),
    ('dodge', '避', 'DODGE', 'wind'),
    ('block', '守', 'BLOCK', 'guard'),
    ('climb', '登', 'CLIMB', 'jade'),
    ('leap', '跳', 'LEAP', 'jade'),
    ('palm', '雷', 'PALM', 'spirit'),
    ('spirit', '魂', 'SPIRIT', 'spirit'),
    ('blade', '刃', 'BLADE', 'spirit'),
]
ROLE = {   # rim / glow colour per role
    'fire': (255, 106, 31), 'wind': (120, 220, 255), 'guard': (235, 225, 205),
    'jade': (110, 255, 170), 'spirit': (90, 170, 255),
}


def font(path, size):
    return ImageFont.truetype(path, size)


def radial(n):
    y, x = np.mgrid[0:n, 0:n] + 0.5
    c = n / 2
    return np.sqrt((x - c) ** 2 + (y - c) ** 2) / c, np.arctan2(y - c, x - c)


def to_img(rgba):
    return Image.fromarray(np.clip(rgba * 255 + 0.5, 0, 255).astype(np.uint8), 'RGBA')


def glyph_layer(n, kanji, label, fill_top, fill_bot, outline, glow, kanji_scale=0.42, label_scale=0.13, kanji_dy=-0.05):
    """kanji + small english label, gradient filled, outlined, glowing"""
    layer = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    mask = Image.new('L', (n, n), 0)
    d = ImageDraw.Draw(mask)
    fk = font(KANJI, int(n * kanji_scale))
    fl = font(BANGERS, int(n * label_scale))
    if kanji:
        bb = d.textbbox((0, 0), kanji, font=fk)
        w, h = bb[2] - bb[0], bb[3] - bb[1]
        d.text(((n - w) / 2 - bb[0], n * (0.5 + kanji_dy) - h / 2 - bb[1] - (n * 0.04 if label else 0)), kanji, font=fk, fill=255)
    if label:
        bb = d.textbbox((0, 0), label, font=fl)
        w, h = bb[2] - bb[0], bb[3] - bb[1]
        ly = n * (0.74 if kanji else 0.5) - h / 2 - bb[1]
        d.text(((n - w) / 2 - bb[0], ly), label, font=fl, fill=255)
    m = np.asarray(mask).astype(np.float32) / 255
    # gradient fill
    t = np.linspace(0, 1, n)[:, None] * np.ones((1, n))
    col = np.array(fill_top)[None, None, :] / 255 * (1 - t[..., None]) + np.array(fill_bot)[None, None, :] / 255 * t[..., None]
    # outline (dilated mask) and glow (blurred)
    out_m = np.asarray(mask.filter(ImageFilter.MaxFilter(int(n * 0.022) | 1))).astype(np.float32) / 255
    glow_m = np.asarray(mask.filter(ImageFilter.MaxFilter(int(n * 0.03) | 1)).filter(ImageFilter.GaussianBlur(n * 0.035))).astype(np.float32) / 255
    rgba = np.zeros((n, n, 4), np.float32)
    g = np.array(glow)[None, None, :3] / 255
    ga = glow_m * (glow[3] / 255 if len(glow) > 3 else 0.8)
    rgba[..., :3] = g * ga[..., None]
    rgba[..., 3] = ga
    oc = np.array(outline)[None, None, :] / 255
    rgba = over(rgba, np.dstack([oc * np.ones((n, n, 1)), out_m]))
    rgba = over(rgba, np.dstack([col, m]))
    return rgba


def over(dst, src):
    """premultiply-free 'over' for float rgba arrays"""
    sa = src[..., 3:4]
    da = dst[..., 3:4]
    oa = sa + da * (1 - sa)
    oc = (src[..., :3] * sa + dst[..., :3] * da * (1 - sa)) / np.maximum(oa, 1e-6)
    return np.concatenate([oc, oa], axis=2)


def ring(r, inner, outer, soft):
    return np.clip((r - inner) / soft, 0, 1) * np.clip((outer - r) / soft, 0, 1)


def disc(r, radius, soft):
    return np.clip((radius - r) / soft, 0, 1)


def blur_alpha(a, px):
    im = Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8), 'L').filter(ImageFilter.GaussianBlur(px))
    return np.asarray(im).astype(np.float32) / 255


def stone_tex(n, tint, dark=0.5, seed=0):
    im = Image.open(PLATES).convert('RGB')
    w = im.size[0]
    off = (seed * 197) % (w // 2)
    im = im.crop((off, off, off + w // 2, off + w // 2)).resize((n, n), Image.LANCZOS)
    a = np.asarray(im).astype(np.float32) / 255
    lum = a.mean(axis=2, keepdims=True)
    a = (lum * 0.7 + a * 0.3) * np.array(tint)[None, None, :] / 255 * dark * 2.2
    em = Image.open(PLATES_E).convert('L').crop((off, off, off + w // 2, off + w // 2)).resize((n, n), Image.LANCZOS)
    return a, np.asarray(em).astype(np.float32) / 255


# =================================================================== A: EMBER STONE
def ember_stone(name, kanji, label, role, n):
    r, ang = radial(n)
    c = np.array(ROLE[role]) / 255
    R = 0.8                                 # the stone disc; the rest is glow
    tex, cracks = stone_tex(n, (150, 140, 130), 0.55, seed=hash(name) % 7)
    # bevel: lit from the top left
    light = np.cos(ang + math.pi * 0.75)
    rim_band = np.clip((r - (R - 0.12)) / 0.12, 0, 1)
    shade = 1 + 0.35 * light * rim_band - 0.25 * np.clip(r / R, 0, 1) ** 3
    body = tex * shade[..., None]
    # glowing cracks in the stone, the colour of the button
    body = body + c[None, None, :] * (cracks ** 1.5)[..., None] * 0.9
    a = disc(r, R, 2.0 / n * 2)
    rgba = np.zeros((n, n, 4), np.float32)
    # outer glow
    ga = blur_alpha(ring(r, R - 0.02, R + 0.03, 0.01), n * 0.045) * 0.9
    rgba = over(rgba, np.dstack([c * np.ones((n, n, 1)), ga]))
    rgba = over(rgba, np.dstack([body, a * 0.92]))
    # metal-and-ember rim
    rim = ring(r, R - 0.06, R, 0.008)
    rim_col = c[None, None, :] * (0.75 + 0.45 * np.clip(light, 0, 1))[..., None] + 0.25
    rgba = over(rgba, np.dstack([np.clip(rim_col, 0, 1), rim]))
    inner = ring(r, R - 0.1, R - 0.085, 0.006) * 0.55
    rgba = over(rgba, np.dstack([np.zeros((n, n, 3)) + 0.02, inner]))
    gold_top, gold_bot = (255, 236, 170), (255, 150, 40)
    if role == 'spirit':
        gold_top, gold_bot = (220, 245, 255), (110, 180, 255)
    elif role == 'jade':
        gold_top, gold_bot = (220, 255, 230), (90, 230, 150)
    elif role == 'guard':
        gold_top, gold_bot = (255, 255, 255), (200, 190, 170)
    g = glyph_layer(n, kanji, label, gold_top, gold_bot, (25, 10, 5), tuple(list((c * 255).astype(int)) + [200]))
    return over(rgba, g)


def ember_stick(part, n):
    r, ang = radial(n)
    c = np.array(ROLE['fire']) / 255
    rgba = np.zeros((n, n, 4), np.float32)
    if part == 'base':
        tex, cracks = stone_tex(n, (140, 130, 120), 0.45, seed=3)
        band = ring(r, 0.72, 0.9, 0.01)
        light = np.cos(ang + math.pi * 0.75)
        body = tex * (1 + 0.3 * light)[..., None] + c[None, None, :] * (cracks ** 1.5)[..., None] * 0.7
        rgba = over(rgba, np.dstack([c * np.ones((n, n, 1)), blur_alpha(ring(r, 0.88, 0.92, 0.01), n * 0.03) * 0.7]))
        rgba = over(rgba, np.dstack([np.zeros((n, n, 3)), disc(r, 0.72, 0.01) * 0.22]))
        rgba = over(rgba, np.dstack([body, band * 0.85]))
        # eight carved notches, the compass of the stick
        for k in range(8):
            a0 = k * math.pi / 4
            notch = np.clip(1 - np.abs(((ang - a0 + math.pi) % (2 * math.pi)) - math.pi) / 0.05, 0, 1) * ring(r, 0.75, 0.87, 0.01)
            rgba = over(rgba, np.dstack([c * np.ones((n, n, 1)), notch * 0.9]))
    else:
        R = 0.62
        tex, cracks = stone_tex(n, (170, 150, 130), 0.6, seed=5)
        light = np.cos(ang + math.pi * 0.75)
        body = tex * (1 + 0.45 * light * np.clip(r / R, 0, 1))[..., None] + c[None, None, :] * (cracks ** 1.2)[..., None] * 1.2
        core = np.clip(1 - r / 0.35, 0, 1) ** 2
        body = body + np.array([1.0, 0.55, 0.15])[None, None, :] * core[..., None] * 0.8
        rgba = over(rgba, np.dstack([c * np.ones((n, n, 1)), blur_alpha(disc(r, R, 0.01), n * 0.06) * 0.85]))
        rgba = over(rgba, np.dstack([np.clip(body, 0, 1), disc(r, R, 0.01)]))
        rgba = over(rgba, np.dstack([c * np.ones((n, n, 1)) + 0.2, ring(r, R - 0.05, R, 0.008)]))
    return rgba


# =================================================================== B: SUMI INK
def enso(r, ang, n, seed, width=0.1, gap=0.5):
    rng = np.random.default_rng(seed)
    start = rng.uniform(0, 2 * math.pi)
    t = ((ang - start) % (2 * math.pi)) / (2 * math.pi)      # 0..1 round the circle
    w = width * (0.35 + 0.9 * np.sin(np.clip(t / (1 - gap * 0.25), 0, 1) * math.pi) ** 0.6)
    rad = 0.74 + 0.03 * np.sin(t * math.pi * 2 + 1.3)
    stroke = np.clip((w / 2 - np.abs(r - rad)) / 0.012, 0, 1) * (t < 1 - gap * 0.2)
    # dry brush: streaks along the stroke
    noise = np.asarray(Image.fromarray((rng.random((n, n)) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.2))).astype(np.float32) / 255
    streak = np.clip(0.55 + 0.9 * (noise - 0.5) + 0.35 * np.sin(r * 180), 0, 1)
    dry = np.where(t > 0.8, streak * (1 - (t - 0.8) * 3), np.clip(streak + 0.5, 0, 1))
    return stroke * np.clip(dry, 0, 1)


def sumi_ink(name, kanji, label, role, n):
    r, ang = radial(n)
    rgba = np.zeros((n, n, 4), np.float32)
    rgba = over(rgba, np.dstack([np.zeros((n, n, 3)) + 0.03, disc(r, 0.7, 0.01) * 0.45]))
    ink = enso(r, ang, n, seed=hash(name) % 97)
    cream = np.array([0.96, 0.92, 0.82])
    rgba = over(rgba, np.dstack([np.zeros((n, n, 3)), blur_alpha(ink, n * 0.012) * 0.8]))
    rgba = over(rgba, np.dstack([cream * np.ones((n, n, 1)), ink]))
    red = np.array([0.82, 0.12, 0.08])
    if role in ('fire', 'spirit'):
        # the red hanko seal in the corner
        y, x = np.mgrid[0:n, 0:n] / n
        sq = ((np.abs(x - 0.78) < 0.1) & (np.abs(y - 0.78) < 0.1)).astype(np.float32)
        sq = blur_alpha(sq, 1.0)
        rgba = over(rgba, np.dstack([red * np.ones((n, n, 1)), sq * 0.95]))
    g = glyph_layer(n, kanji, label, (250, 245, 230), (225, 210, 180), (10, 8, 6), (0, 0, 0, 170), kanji_scale=0.4)
    return over(rgba, g)


def sumi_stick(part, n):
    r, ang = radial(n)
    rgba = np.zeros((n, n, 4), np.float32)
    cream = np.array([0.96, 0.92, 0.82])
    if part == 'base':
        rgba = over(rgba, np.dstack([np.zeros((n, n, 3)), disc(r, 0.85, 0.01) * 0.25]))
        ink = enso(r, ang, n, seed=11, width=0.07, gap=0.3)
        rgba = over(rgba, np.dstack([cream * np.ones((n, n, 1)), ink * 0.9]))
    else:
        red = np.array([0.8, 0.12, 0.08])
        rgba = over(rgba, np.dstack([np.zeros((n, n, 3)), blur_alpha(disc(r, 0.6, 0.01), n * 0.03) * 0.6]))
        rgba = over(rgba, np.dstack([red * np.ones((n, n, 1)), disc(r, 0.55, 0.01) * 0.92]))
        rgba = over(rgba, np.dstack([cream * np.ones((n, n, 1)), ring(r, 0.45, 0.5, 0.01) * 0.8]))
    return rgba


# =================================================================== C: SPIRIT GLASS
def spirit_glass(name, kanji, label, role, n):
    r, ang = radial(n)
    c = np.array(ROLE[role]) / 255
    R = 0.78
    rgba = np.zeros((n, n, 4), np.float32)
    rgba = over(rgba, np.dstack([c * np.ones((n, n, 1)), blur_alpha(ring(r, R - 0.02, R + 0.02, 0.01), n * 0.05) * 0.95]))
    y = (np.mgrid[0:n, 0:n][0] / n)
    glass = np.array([0.05, 0.08, 0.14])[None, None, :] * (1.2 - y[..., None] * 0.6) + c[None, None, :] * 0.12 * (1 - r / R)[..., None]
    rgba = over(rgba, np.dstack([np.clip(glass, 0, 1), disc(r, R, 0.01) * 0.62]))
    # the glass highlight: a bright arc across the top
    hl = np.clip(1 - np.abs(r - R * 0.8) / 0.05, 0, 1) * np.clip(-np.sin(ang) - 0.35, 0, 1) * 0.5
    rgba = over(rgba, np.dstack([np.ones((n, n, 3)), hl]))
    rgba = over(rgba, np.dstack([np.clip(c + 0.35, 0, 1) * np.ones((n, n, 1)), ring(r, R - 0.025, R, 0.006)]))
    g = glyph_layer(n, kanji, label, (255, 255, 255), tuple(int(v) for v in np.clip(c * 255 + 60, 0, 255)), (4, 10, 22), tuple(list((c * 255).astype(int)) + [230]))
    return over(rgba, g)


def glass_stick(part, n):
    r, ang = radial(n)
    c = np.array(ROLE['spirit']) / 255
    rgba = np.zeros((n, n, 4), np.float32)
    if part == 'base':
        rgba = over(rgba, np.dstack([c * np.ones((n, n, 1)), blur_alpha(ring(r, 0.87, 0.9, 0.01), n * 0.03) * 0.8]))
        rgba = over(rgba, np.dstack([np.array([0.04, 0.07, 0.12]) * np.ones((n, n, 1)), disc(r, 0.88, 0.01) * 0.35]))
        rgba = over(rgba, np.dstack([np.clip(c + 0.3, 0, 1) * np.ones((n, n, 1)), ring(r, 0.86, 0.885, 0.006)]))
    else:
        core = np.clip(1 - r / 0.58, 0, 1)
        rgba = over(rgba, np.dstack([c * np.ones((n, n, 1)), blur_alpha(disc(r, 0.58, 0.01), n * 0.06) * 0.9]))
        col = np.clip(c[None, None, :] * 0.6 + core[..., None] ** 2 * 0.9, 0, 1)
        rgba = over(rgba, np.dstack([col, disc(r, 0.55, 0.01) * 0.9]))
    return rgba


STYLES = {
    'A': ('Ember Stone', ember_stone, ember_stick, (255, 150, 60)),
    'B': ('Sumi Ink', sumi_ink, sumi_stick, (240, 230, 210)),
    'C': ('Spirit Glass', spirit_glass, glass_stick, (130, 190, 255)),
}


def pause_sprite(style, n):
    r, ang = radial(n)
    rgba = style[1]('pause', '', '', 'guard' if style[0] != 'Spirit Glass' else 'spirit', n)
    y, x = np.mgrid[0:n, 0:n] / n
    bars = ((np.abs(x - 0.41) < 0.045) | (np.abs(x - 0.59) < 0.045)) & (np.abs(y - 0.5) < 0.16)
    bars = blur_alpha(bars.astype(np.float32), 1.0)
    col = np.array(style[3]) / 255
    return over(rgba, np.dstack([col * np.ones((n, n, 1)), bars]))


def build(key):
    title, btn, stick, accent = STYLES[key]
    out = os.path.join(OUT, key)
    os.makedirs(out, exist_ok=True)
    n = S * SS
    for name, kanji, label, role in BUTTONS:
        im = to_img(btn(name, kanji, label, role, n)).resize((S, S), Image.LANCZOS)
        im.save(os.path.join(out, f'btn_{name}.png'))
    to_img(pause_sprite(STYLES[key], n)).resize((S, S), Image.LANCZOS).save(os.path.join(out, 'btn_pause.png'))
    for part in ('base', 'knob'):
        to_img(stick(part, n)).resize((S, S), Image.LANCZOS).save(os.path.join(out, f'stick_{part}.png'))
    print('built', key, title)


# the layout the game uses (reference pixels, 1080 tall; anchored as in MobileControls.cs)
LAYOUT = {
    'attack': ((-265, 245), 128, 'br'), 'dodge': ((-92, 100), 76, 'br'), 'block': ((-530, 118), 84, 'br'),
    'climb': ((-105, 440), 72, 'br'),
    'palm': ((-265 + math.cos(math.radians(96)) * 222, 245 + math.sin(math.radians(96)) * 222), 60, 'br'),
    'spirit': ((-265 + math.cos(math.radians(132)) * 222, 245 + math.sin(math.radians(132)) * 222), 60, 'br'),
    'blade': ((-265 + math.cos(math.radians(168)) * 222, 245 + math.sin(math.radians(168)) * 222), 60, 'br'),
    'pause': ((0, -150), 40, 'tc'),
}
SPRITE_RADIUS = {'btn': 0.8, 'stick_base': 0.9, 'stick_knob': 0.62}   # the visible disc inside each sprite


def mockup(key, background, dst):
    W, H = 2400, 1080
    bg = Image.open(background).convert('RGBA')
    bw, bh = bg.size
    tw = int(bh * W / H)
    if tw <= bw:
        x0 = (bw - tw) // 2
        bg = bg.crop((x0, 0, x0 + tw, bh))
    else:
        th = int(bw * H / W)
        bg = bg.crop((0, 0, bw, th))
    bg = bg.resize((W, H), Image.LANCZOS)
    d = os.path.join(OUT, key)

    def place(img_path, cx, cy, radius, vis):
        im = Image.open(img_path).convert('RGBA')
        size = int(2 * radius / vis)
        im = im.resize((size, size), Image.LANCZOS)
        bg.alpha_composite(im, (int(cx - size / 2), int(cy - size / 2)))

    for name, ((x, y), rad, anchor) in LAYOUT.items():
        if anchor == 'br':
            cx, cy = W + x, H - y
        else:
            cx, cy = W / 2 + x, -y
        place(os.path.join(d, f'btn_{name}.png'), cx, cy, rad * 1.18, SPRITE_RADIUS['btn'])
    place(os.path.join(d, 'stick_base.png'), 300, H - 280, 150, SPRITE_RADIUS['stick_base'])
    place(os.path.join(d, 'stick_knob.png'), 300 + 55, H - 280 - 40, 68, SPRITE_RADIUS['stick_knob'])
    # a style tag
    dr = ImageDraw.Draw(bg)
    f = font(BANGERS, 54)
    dr.text((40, H - 90), f'STYLE {key}:  {STYLES[key][0].upper()}', font=f, fill=(255, 255, 255, 255), stroke_width=4, stroke_fill=(0, 0, 0, 255))
    bg.convert('RGB').save(dst, quality=92)
    print('mockup', dst)


if __name__ == '__main__':
    if len(sys.argv) > 2 and sys.argv[1] == 'install':
        key = sys.argv[2]
        os.makedirs(RES, exist_ok=True)
        for f in os.listdir(os.path.join(OUT, key)):
            shutil.copy(os.path.join(OUT, key, f), os.path.join(RES, f))
        print('installed style', key, 'into', RES)
        sys.exit(0)
    bgs = sys.argv[1:] or []
    for key in STYLES:
        build(key)
    for i, b in enumerate(bgs):
        for key in STYLES:
            mockup(key, b, os.path.join(OUT, f'mockup_{key}_{i}.jpg'))
