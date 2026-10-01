"""Generate original emoji-style key artwork; requires Pillow only for regeneration."""

from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter


ASSETS = Path(__file__).resolve().parents[1] / "Assets"
SCALE = 8
SIZE = 128 * SCALE


def box(values):
    return tuple(round(value * SCALE) for value in values)


def polygon(draw, points, fill):
    draw.polygon([(round(x * SCALE), round(y * SCALE)) for x, y in points], fill=fill)


def make_key():
    mask = Image.new("L", (SIZE, SIZE))
    draw = ImageDraw.Draw(mask)
    polygon(draw, [(66, 47), (79, 60), (31, 108), (18, 111), (11, 102)], 255)
    polygon(draw, [(48, 79), (61, 92), (52, 101), (39, 88)], 255)
    polygon(draw, [(30, 96), (43, 109), (34, 118), (21, 105)], 255)
    draw.ellipse(box((62, 9, 114, 61)), fill=255)
    draw.ellipse(box((77, 24, 99, 46)), fill=0)

    outline = mask.filter(ImageFilter.MaxFilter(2 * 2 * SCALE + 1))
    art = Image.new("RGBA", (SIZE, SIZE))
    art.paste((117, 73, 16, 255), mask=outline)
    gradient = Image.new("RGBA", (SIZE, SIZE))
    paint = ImageDraw.Draw(gradient)
    top, bottom = (255, 225, 115), (226, 151, 28)
    for y in range(SIZE):
        fraction = min(1, max(0, (y / SCALE - 9) / 109))
        color = tuple(round(a + (b - a) * fraction) for a, b in zip(top, bottom))
        paint.line((0, y, SIZE, y), fill=(*color, 255))
    art.paste(gradient, mask=mask)

    shine = Image.new("RGBA", (SIZE, SIZE))
    highlight = ImageDraw.Draw(shine)
    highlight.arc(box((67, 14, 109, 56)), 205, 305, fill=(255, 248, 211, 230), width=3 * SCALE)
    highlight.line(box((68, 59, 23, 104)), fill=(255, 242, 184, 150), width=2 * SCALE)
    shine.putalpha(ImageChops.multiply(shine.getchannel("A"), mask))
    return Image.alpha_composite(art, shine)


def logo(art, width, height, proportion=0.82):
    edge = round(min(width, height) * proportion)
    glyph = art.resize((edge, edge), Image.Resampling.LANCZOS)
    image = Image.new("RGBA", (width, height))
    image.alpha_composite(glyph, ((width - edge) // 2, (height - edge) // 2))
    return image


def main():
    art = make_key()
    icon = art.resize((256, 256), Image.Resampling.LANCZOS)
    icon.save(
        ASSETS / "AppIcon.ico",
        sizes=[(size, size) for size in (16, 20, 24, 32, 40, 48, 64, 96, 128, 256)],
    )
    for name, dimensions, proportion in (
        ("LockScreenLogo.scale-200.png", (48, 48), 0.9),
        ("SplashScreen.scale-200.png", (1240, 600), 0.44),
        ("Square150x150Logo.scale-200.png", (300, 300), 0.82),
        ("Square44x44Logo.scale-200.png", (88, 88), 0.9),
        ("Square44x44Logo.targetsize-24_altform-unplated.png", (24, 24), 0.98),
        ("Square44x44Logo.targetsize-48_altform-lightunplated.png", (48, 48), 0.98),
        ("StoreLogo.png", (50, 50), 0.9),
        ("Wide310x150Logo.scale-200.png", (620, 300), 0.82),
    ):
        logo(art, *dimensions, proportion).save(ASSETS / name)
    print("Generated original key icon in 10 ICO sizes and all packaged logo assets.")


if __name__ == "__main__":
    main()
