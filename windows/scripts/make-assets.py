#!/usr/bin/env python3
"""Render the MSIX images and the .ico from data/io.github.rem2608.FolidStruct.svg.

    pip install cairosvg pillow
    python3 windows/scripts/make-assets.py

The results are committed, so this is only needed when the icon changes.
"""

import io
from pathlib import Path

import cairosvg
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SVG = (ROOT / "data/io.github.rem2608.FolidStruct.svg").read_bytes()
OUT = ROOT / "windows/src/FolidStruct.App/Assets"


def icon(size):
    png = cairosvg.svg2png(bytestring=SVG, output_width=size, output_height=size)
    return Image.open(io.BytesIO(png)).convert("RGBA")


def canvas(width, height, icon_size):
    """The icon centred on a transparent image."""
    image = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    image.alpha_composite(icon(icon_size), ((width - icon_size) // 2, (height - icon_size) // 2))
    return image


def save(image, name):
    image.save(OUT / name, optimize=True)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    scales = {100: 1, 125: 1.25, 150: 1.5, 200: 2, 400: 4}

    for scale, factor in scales.items():
        save(icon(round(44 * factor)), f"Square44x44Logo.scale-{scale}.png")
        save(icon(round(50 * factor)), f"StoreLogo.scale-{scale}.png")
        size = round(150 * factor)
        save(canvas(size, size, round(size * 0.66)), f"Square150x150Logo.scale-{scale}.png")
        width, height = round(310 * factor), round(150 * factor)
        save(canvas(width, height, round(height * 0.66)), f"Wide310x150Logo.scale-{scale}.png")
        width, height = round(620 * factor), round(300 * factor)
        save(canvas(width, height, round(height * 0.5)), f"SplashScreen.scale-{scale}.png")

    # Taskbar, Start and File Explorer sizes.
    for size in (16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256):
        image = icon(size)
        save(image, f"Square44x44Logo.targetsize-{size}.png")
        save(image, f"Square44x44Logo.targetsize-{size}_altform-unplated.png")
        save(image, f"Square44x44Logo.targetsize-{size}_altform-lightunplated.png")

    sizes = [16, 20, 24, 32, 40, 48, 64, 256]
    icon(256).save(OUT / "FolidStruct.ico", sizes=[(s, s) for s in sizes])


if __name__ == "__main__":
    main()
