"""
Генератор значков Shotly для трея (assets/brand/tray, src/Shotly/Assets/Tray*.ico).

Трей-глиф — упрощённый знак Orbit: те же две половины «S», но
  * обрезан по контуру и заполняет почти всё поле 16/20/24/32/40/48 px;
  * половины чуть утолщены, зазор между ними шире (~1.8 px при 16 px), чтобы «S» читалась;
  * каждый размер растеризуется отдельно (суперсэмплинг + подтяжка альфы к пикселям);
  * два набора цветов: для тёмной и для светлой панели задач.

Запуск: python tools/make_tray_icons.py   (нужны numpy, scipy, Pillow)
"""
import re
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

ROOT = Path(__file__).resolve().parents[1]

# Контуры половин знака из assets/brand/shotly-mark.svg (исходная система координат).
TOP = "M78.50 263.64C69.59 261.31 58.31 255.22 52.61 249.65C40.59 237.91 34.47 223.56 33.36 204.50C32.28 186.03 36.27 169.04 46.51 148.50C67.78 105.82 110.61 73.49 160.00 62.82C172.17 60.19 200.20 59.21 213.35 60.96C242.96 64.88 270.40 76.96 288.75 94.13C293.78 98.84 294.43 101.58 290.25 100.52C278.84 97.63 260.12 97.16 249.09 99.48C226.78 104.17 209.24 114.10 178.78 139.27C144.32 167.73 119.70 191.28 118.14 197.25C116.18 204.76 120.56 211.65 128.78 213.97C133.99 215.45 148.60 216.78 168.30 217.57C182.88 218.16 185.06 218.46 184.79 219.83C184.34 222.20 168.83 235.26 157.90 242.48C145.31 250.80 134.23 256.30 121.15 260.72C111.36 264.03 109.41 264.34 97.00 264.64C88.11 264.84 81.79 264.51 78.50 263.64Z"
BOTTOM = "M159.00 348.54C158.18 348.34 154.12 347.64 150.00 346.97C128.58 343.53 108.58 334.27 89.75 319.09C80.90 311.95 80.08 310.00 85.95 310.00C92.26 310.00 110.35 306.17 119.80 302.84C144.90 293.98 181.61 271.29 218.00 242.14C242.96 222.15 249.70 212.73 244.44 205.22C240.20 199.16 233.64 198.10 194.23 197.11C176.49 196.67 161.65 195.99 161.27 195.60C160.02 194.36 162.47 191.65 172.58 183.13C217.82 144.97 263.71 134.18 300.12 153.12C341.88 174.85 348.07 228.17 314.13 273.85C287.46 309.75 245.45 337.07 203.16 346.04C194.30 347.92 163.60 349.66 159.00 348.54Z"

# Цвета (в рамках Ember: #F2541B / #FFB000). Для тёмной панели — чуть ярче и насыщеннее,
# для светлой — глубже, чтобы янтарь не растворялся на #F3F3F3.
THEMES = {
    "dark": {"top": (0xFF, 0x5A, 0x1F), "bottom": (0xFF, 0xB8, 0x1A)},
    "light": {"top": (0xE8, 0x46, 0x0C), "bottom": (0xF0, 0x9A, 0x00)},
}

SIZES = [16, 20, 24, 32, 40, 48, 64]
SS = 16  # суперсэмплинг


def parse(d):
    tokens = re.findall(r"[MCZ]|-?\d+(?:\.\d+)?", d)
    pts, i, cur = [], 0, None
    while i < len(tokens):
        t = tokens[i]
        if t == "M":
            cur = (float(tokens[i + 1]), float(tokens[i + 2])); pts.append(cur); i += 3
        elif t == "C":
            p1 = (float(tokens[i + 1]), float(tokens[i + 2]))
            p2 = (float(tokens[i + 3]), float(tokens[i + 4]))
            p3 = (float(tokens[i + 5]), float(tokens[i + 6]))
            for k in range(1, 25):
                u = k / 24
                a, b, c, e = (1 - u) ** 3, 3 * (1 - u) ** 2 * u, 3 * (1 - u) * u * u, u ** 3
                pts.append((a * cur[0] + b * p1[0] + c * p2[0] + e * p3[0],
                            a * cur[1] + b * p1[1] + c * p2[1] + e * p3[1]))
            cur = p3; i += 7
        else:
            i += 1
    return np.array(pts)


TOP_P, BOT_P = parse(TOP), parse(BOTTOM)


def params(size):
    """Утолщение, зазор и поле — в пикселях итогового размера. Подобраны на 16 px и
    масштабируются пропорционально: зазор ~1.8 px при 16 px, иначе «S» слипается в шар."""
    f = size / 16
    return dict(grow=0.15 * f, gap=1.8 * f, pad=0.0)


def masks(size):
    p = params(size)
    n = size * SS
    allp = np.vstack([TOP_P, BOT_P])
    lo, hi = allp.min(0), allp.max(0)
    # запас под утолщение, чтобы после него глиф упёрся ровно в край поля
    inner = size - 2 * (p["pad"] + p["grow"])
    scale = inner / (hi - lo).max()
    off = (size - (hi - lo) * scale) / 2 - lo * scale

    def fill(poly):
        im = Image.new("L", (n, n), 0)
        ImageDraw.Draw(im).polygon([tuple(v) for v in (poly * scale + off) * SS], fill=255)
        return np.array(im) > 127

    a, b = fill(TOP_P), fill(BOT_P)
    g = p["grow"] * SS
    a = ndimage.distance_transform_edt(~a) <= g
    b = ndimage.distance_transform_edt(~b) <= g
    # ровный зазор: нижняя половина отступает от верхней (верхняя «лежит сверху»)
    b &= ndimage.distance_transform_edt(~a) > p["gap"] * SS
    return a, b


def coverage(m, size):
    cov = m.reshape(size, SS, size, SS).mean(axis=(1, 3))
    # подтяжка к пиксельной сетке: полупрозрачная кайма тоньше, края контрастнее
    return np.clip((cov - 0.5) * 1.35 + 0.5, 0, 1) * (cov > 0.04)


def render(size, theme):
    t = THEMES[theme]
    a, b = masks(size)
    ca, cb = coverage(a, size), coverage(b, size)
    rgb = np.zeros((size, size, 3)); alpha = np.zeros((size, size))

    def over(color, cov):
        nonlocal rgb, alpha
        col = np.array(color[:3], float) / 255
        out_a = cov + alpha * (1 - cov)
        rgb = np.where(out_a[..., None] > 0,
                       (col * cov[..., None] + rgb * alpha[..., None] * (1 - cov[..., None])) / np.maximum(out_a, 1e-6)[..., None],
                       0)
        alpha = out_a

    over(t["bottom"], cb)
    over(t["top"], ca)
    out = np.dstack([rgb * 255, alpha * 255]).round().clip(0, 255).astype(np.uint8)
    return Image.fromarray(out, "RGBA")


def main():
    out_png = ROOT / "assets" / "brand" / "tray"
    out_png.mkdir(parents=True, exist_ok=True)
    for theme in THEMES:
        frames = [render(s, theme) for s in SIZES]
        for f in frames:
            f.save(out_png / f"shotly-tray-{theme}-{f.width}.png")
        name = "TrayDark.ico" if theme == "dark" else "TrayLight.ico"
        frames[-1].save(ROOT / "src" / "Shotly" / "Assets" / name, format="ICO",
                        sizes=[(s, s) for s in SIZES], append_images=frames[:-1])
        frames[-1].save(out_png / f"shotly-tray-{theme}.ico", format="ICO",
                        sizes=[(s, s) for s in SIZES], append_images=frames[:-1])
    print("ok")


if __name__ == "__main__":
    main()
