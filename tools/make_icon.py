"""Иконка приложения: куб Geometry Dash в незамкнутом кольце прогресса.

Палитра совпадает с фавиконкой сайта (docs/index.html): голубой #38E1FF,
розовый #FF4D9D, подложка #0B0D12. Рисуем с четырёхкратным сглаживанием и
собираем многоразмерный .ico; мелкие размеры рисуются отдельным, упрощённым
вариантом — тонкие линии в 16 px превращаются в грязь.
"""
import math
import sys
from PIL import Image, ImageDraw, ImageFilter

BG = (11, 13, 18, 255)
CYAN = (56, 225, 255)
PINK = (255, 77, 157)

OUT_DIR = sys.argv[1] if len(sys.argv) > 1 else "."


def lerp(a, b, t):
    return tuple(round(x + (y - x) * t) for x, y in zip(a, b))


def rounded_square_outline(draw, box, radius, width, color):
    draw.rounded_rectangle(box, radius=radius, outline=color, width=width)


def draw_icon(size, detailed=True):
    """Рисует иконку размером size в четырёхкратном масштабе и уменьшает её."""
    ss = 4
    s = size * ss
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # Подложка: скруглённый квадрат. Радиус ~22% — как у иконок Windows 11.
    pad = round(s * 0.02)
    d.rounded_rectangle([pad, pad, s - pad - 1, s - pad - 1], radius=round(s * 0.22), fill=BG)

    if detailed:
        # Мягкое свечение под кольцом, чтобы иконка не выглядела плоской наклейкой.
        glow = Image.new("RGBA", (s, s), (0, 0, 0, 0))
        gd = ImageDraw.Draw(glow)
        gd.ellipse([s * 0.10, s * 0.10, s * 0.90, s * 0.90], fill=(*CYAN, 26))
        gd.ellipse([s * 0.28, s * 0.34, s * 0.86, s * 0.92], fill=(*PINK, 30))
        glow = glow.filter(ImageFilter.GaussianBlur(s * 0.06))
        img.alpha_composite(glow)

    # Кольцо прогресса: дуга с разрывом слева сверху, цвет перетекает из розового
    # в голубой. PIL не умеет градиентную обводку, поэтому рисуем по градусу.
    r = s * (0.335 if detailed else 0.35)
    cx = cy = s / 2
    width = round(s * (0.085 if detailed else 0.10))
    box = [cx - r, cy - r, cx + r, cy + r]
    start, sweep = -60, 300
    step = 2
    for i in range(0, sweep, step):
        t = i / sweep
        color = lerp(PINK, CYAN, t)
        d.arc(box, start + i, start + i + step + 1, fill=(*color, 255), width=width)

    # Скруглённые концы дуги: PIL рисует срез, а не капсулу.
    for angle, color in ((start, PINK), (start + sweep, CYAN)):
        a = math.radians(angle)
        px, py = cx + r * math.cos(a), cy + r * math.sin(a)
        d.ellipse([px - width / 2, py - width / 2, px + width / 2, py + width / 2], fill=(*color, 255))

    # Куб: контур голубым, ядро розовым — тот же знак, что и на фавиконке сайта.
    half = s * (0.145 if detailed else 0.155)
    stroke = round(s * (0.062 if detailed else 0.075))
    rounded_square_outline(
        d, [cx - half, cy - half, cx + half, cy + half],
        radius=round(s * 0.05), width=stroke, color=(*CYAN, 255))

    core = s * (0.052 if detailed else 0.058)
    d.rounded_rectangle(
        [cx - core, cy - core, cx + core, cy + core],
        radius=round(s * 0.018), fill=(*PINK, 255))

    return img.resize((size, size), Image.LANCZOS)


def main():
    sizes = [256, 128, 64, 48, 32, 24, 20, 16]
    frames = [draw_icon(n, detailed=n >= 48) for n in sizes]

    ico_path = OUT_DIR + "/app.ico"
    frames[0].save(ico_path, format="ICO", sizes=[(n, n) for n in sizes], append_images=frames[1:])
    print("saved " + ico_path)

    # Отдельные PNG: превью для глаза и картинка для сайта.
    for n in (256, 48, 32, 16):
        p = OUT_DIR + "/icon_preview_%d.png" % n
        draw_icon(n, detailed=n >= 48).save(p)
        print("saved " + p)

    # Полоса предпросмотра: все размеры в ряд на нейтральном фоне.
    strip = Image.new("RGBA", (620, 300), (26, 28, 36, 255))
    x = 20
    for n in (256, 64, 48, 32, 24, 16):
        strip.alpha_composite(draw_icon(n, detailed=n >= 48), (x, 20))
        x += n + 18
    strip.save(OUT_DIR + "/icon_strip.png")
    print("saved " + OUT_DIR + "/icon_strip.png")


main()
