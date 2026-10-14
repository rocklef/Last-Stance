# Style D, "Obsidian": smoky glass discs, crisp glowing rims, clean game icons (no calligraphy).
#   python tools/ui/style_d.py          -> tools/ui/out/D/*.png and installs them into Resources/UI
import os, sys, math, shutil
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import make_ui as M
from make_ui import radial, ring, disc, blur_alpha, over, to_img, stone_tex, font, BANGERS, ROLE

S, SS = M.S, M.SS
ICONS = [('attack', 'ATTACK', 'fire'), ('dodge', 'DODGE', 'wind'), ('block', 'BLOCK', 'guard'),
         ('jump', 'JUMP', 'jade'), ('climb', 'CLIMB', 'jade'), ('leap', 'LEAP', 'jade'),
         ('palm', 'PALM', 'spirit'), ('spirit', 'SPIRIT', 'spirit'), ('blade', 'BLADE', 'spirit')]


def icon_mask(name, n):
    m = Image.new('L', (n, n), 0)
    d = ImageDraw.Draw(m)
    u = n / 100.0

    def P(pts):
        return [(x * u, y * u) for x, y in pts]

    if name == 'attack':            # a clenched fist
        d.rounded_rectangle(P([(32, 42), (70, 70)]), radius=8 * u, fill=255)
        for i in range(4):
            d.ellipse(P([(31 + i * 9.6, 30), (42 + i * 9.6, 47)]), fill=255)
        d.polygon(P([(26, 50), (40, 48), (46, 58), (30, 62)]), fill=255)
        d.rectangle(P([(38, 66), (66, 76)]), fill=255)
        for i in range(3):
            d.line(P([(41.5 + i * 9.6, 33), (41.5 + i * 9.6, 45)]), fill=0, width=max(1, int(2 * u)))
    elif name == 'dodge':           # speed chevrons
        for k in range(3):
            x = 24 + k * 17
            d.polygon(P([(x, 30), (x + 12, 30), (x + 26, 50), (x + 12, 70), (x, 70), (x + 14, 50)]), fill=110 + 72 * k)
    elif name == 'block':           # a shield
        d.polygon(P([(50, 22), (73, 31), (71, 56), (50, 78), (29, 56), (27, 31)]), fill=255)
        d.polygon(P([(50, 31), (65, 37), (64, 55), (50, 69)]), fill=0)
    elif name == 'jump':            # double chevron up
        for k in range(2):
            yy = 44 - k * 20
            d.polygon(P([(50, yy - 10), (74, yy + 14), (64, yy + 22), (50, yy + 8), (36, yy + 22), (26, yy + 14)]), fill=255 if k == 0 else 170)
        d.rectangle(P([(34, 72), (66, 77)]), fill=255)
    elif name == 'climb':
        d.polygon(P([(50, 20), (72, 44), (59, 44), (59, 62), (41, 62), (41, 44), (28, 44)]), fill=255)
        d.rectangle(P([(28, 68), (72, 75)]), fill=255)
    elif name == 'leap':
        d.arc(P([(22, 36), (74, 88)]), 195, 325, fill=255, width=max(2, int(9 * u)))
        d.polygon(P([(64, 26), (82, 50), (58, 52)]), fill=255)
    elif name == 'palm':            # lightning
        d.polygon(P([(58, 16), (30, 54), (47, 54), (38, 84), (72, 42), (54, 42), (64, 16)]), fill=255)
    elif name == 'spirit':          # spirit flame
        d.ellipse(P([(33, 44), (67, 80)]), fill=255)
        d.polygon(P([(33, 62), (42, 36), (50, 48), (57, 16), (67, 62)]), fill=255)
        d.ellipse(P([(43, 57), (57, 73)]), fill=0)
    elif name == 'blade':           # a sword
        d.polygon(P([(76, 20), (80, 24), (47, 62), (41, 56)]), fill=255)
        d.polygon(P([(33, 51), (37, 47), (55, 65), (51, 69)]), fill=255)
        d.polygon(P([(40, 62), (44, 66), (29, 81), (25, 77)]), fill=255)
    elif name == 'pause':
        d.rectangle(P([(35, 30), (45, 70)]), fill=255)
        d.rectangle(P([(55, 30), (65, 70)]), fill=255)
    return m


def icon_layer(n, name, label, top, bot, glow):
    mask = icon_mask(name, n)
    if label:
        icon = mask.resize((int(n * 0.78), int(n * 0.78)), Image.LANCZOS)
        mask = Image.new('L', (n, n), 0)
        mask.paste(icon, (int(n * 0.11), int(n * 0.03)))
        d = ImageDraw.Draw(mask)
        fl = font(BANGERS, int(n * 0.115))
        bb = d.textbbox((0, 0), label, font=fl)
        d.text(((n - (bb[2] - bb[0])) / 2 - bb[0], n * 0.685 - bb[1]), label, font=fl, fill=255)
    m = np.asarray(mask).astype(np.float32) / 255
    t = np.linspace(0, 1, n)[:, None] * np.ones((1, n))
    col = np.array(top)[None, None, :] / 255 * (1 - t[..., None]) + np.array(bot)[None, None, :] / 255 * t[..., None]
    glow_m = np.asarray(mask.filter(ImageFilter.GaussianBlur(n * 0.03))).astype(np.float32) / 255
    rgba = np.zeros((n, n, 4), np.float32)
    rgba = over(rgba, np.dstack([np.array(glow[:3]) / 255 * np.ones((n, n, 1)), np.clip(glow_m * 1.6, 0, 1) * glow[3] / 255]))
    return over(rgba, np.dstack([col, m]))


def button(name, label, role, n):
    r, ang = radial(n)
    c = np.array(ROLE[role]) / 255
    R = 0.8
    y = np.mgrid[0:n, 0:n][0] / n
    tex, _ = stone_tex(n, (90, 90, 100), 0.35, seed=2)
    body = np.array([0.03, 0.035, 0.05])[None, None, :] + tex * 0.3 + c[None, None, :] * (0.3 * y ** 2)[..., None] * np.clip(1 - r / R, 0, 1)[..., None]
    rgba = np.zeros((n, n, 4), np.float32)
    rgba = over(rgba, np.dstack([c * np.ones((n, n, 1)), blur_alpha(ring(r, R - 0.02, R + 0.02, 0.01), n * 0.04) * 0.75]))
    rgba = over(rgba, np.dstack([np.clip(body, 0, 1), disc(r, R, 0.008) * 0.82]))
    sheen = np.clip(1 - np.abs(r - R * 0.78) / 0.06, 0, 1) * np.clip(-np.sin(ang) - 0.4, 0, 1) * 0.35
    rgba = over(rgba, np.dstack([np.ones((n, n, 3)), sheen]))
    light = np.clip(-np.sin(ang), 0, 1)
    rim_col = c[None, None, :] * (1 - light[..., None] * 0.6) + light[..., None] * 0.6
    rgba = over(rgba, np.dstack([np.clip(rim_col + 0.1, 0, 1), ring(r, R - 0.035, R, 0.006)]))
    rgba = over(rgba, np.dstack([c * np.ones((n, n, 1)), ring(r, R - 0.09, R - 0.08, 0.004) * 0.35]))
    bot = tuple(int(v) for v in np.clip(c * 255 * 0.8 + 70, 0, 255))
    return over(rgba, icon_layer(n, name, label, (255, 255, 255), bot, tuple(list((c * 255).astype(int)) + [220])))


def stick(part, n):
    r, ang = radial(n)
    c = np.array([255, 120, 50]) / 255
    rgba = np.zeros((n, n, 4), np.float32)
    if part == 'base':
        rgba = over(rgba, np.dstack([np.array([0.03, 0.035, 0.05]) * np.ones((n, n, 1)), disc(r, 0.9, 0.01) * 0.4]))
        rgba = over(rgba, np.dstack([c * np.ones((n, n, 1)), blur_alpha(ring(r, 0.87, 0.9, 0.01), n * 0.03) * 0.6]))
        rgba = over(rgba, np.dstack([np.clip(c + 0.25, 0, 1) * np.ones((n, n, 1)), ring(r, 0.87, 0.895, 0.005)]))
        for k in range(4):
            a0 = k * math.pi / 2 - math.pi / 2
            tick = np.clip(1 - np.abs(((ang - a0 + math.pi) % (2 * math.pi)) - math.pi) / 0.09, 0, 1) * ring(r, 0.74, 0.82, 0.01)
            rgba = over(rgba, np.dstack([np.clip(c + 0.2, 0, 1) * np.ones((n, n, 1)), tick * 0.9]))
    else:
        R = 0.6
        core = np.clip(1 - r / R, 0, 1)
        body = np.array([0.05, 0.05, 0.07])[None, None, :] + c[None, None, :] * (core ** 1.5)[..., None] * 0.9
        rgba = over(rgba, np.dstack([c * np.ones((n, n, 1)), blur_alpha(disc(r, R, 0.01), n * 0.05) * 0.8]))
        rgba = over(rgba, np.dstack([np.clip(body, 0, 1), disc(r, R, 0.008)]))
        rgba = over(rgba, np.dstack([np.clip(c + 0.4, 0, 1) * np.ones((n, n, 1)), ring(r, R - 0.035, R, 0.006)]))
    return rgba


if __name__ == '__main__':
    out = os.path.join(M.OUT, 'D')
    os.makedirs(out, exist_ok=True)
    n = S * SS
    for name, label, role in ICONS:
        to_img(button(name, label, role, n)).resize((S, S), Image.LANCZOS).save(os.path.join(out, f'btn_{name}.png'))
    to_img(button('pause', '', 'guard', n)).resize((S, S), Image.LANCZOS).save(os.path.join(out, 'btn_pause.png'))
    for part in ('base', 'knob'):
        to_img(stick(part, n)).resize((S, S), Image.LANCZOS).save(os.path.join(out, f'stick_{part}.png'))
    os.makedirs(M.RES, exist_ok=True)
    for f in os.listdir(out):
        shutil.copy(os.path.join(out, f), os.path.join(M.RES, f))
    names = [x[0] for x in ICONS] + ['pause']
    sheet = Image.new('RGBA', (len(names) * 160, 160), (70, 55, 60, 255))
    for i, nm in enumerate(names):
        sheet.alpha_composite(Image.open(os.path.join(out, f'btn_{nm}.png')).resize((160, 160), Image.LANCZOS), (i * 160, 0))
    sheet.convert('RGB').save(os.path.join(M.OUT, 'D_icons.png'))
    print('style D built and installed')
