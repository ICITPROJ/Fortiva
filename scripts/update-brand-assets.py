#!/usr/bin/env python3
"""Process Fortiva logo/icon PNGs and write app + installer assets (transparency preserved)."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from PIL import Image, ImageEnhance, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src" / "Fortiva.AppHost" / "Assets"
PACKAGING = ROOT / "packaging" / "assets"
EXTENSION = ROOT / "extension"

OUTPUT_SIZE = 512
BLACK_THRESHOLD = 22  # RGB all <= this → treated as background (only when no alpha channel)
ICO_SIZES = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
WIZARD_SIDEBAR_SIZE = (164, 314)
WIZARD_SMALL_SIZE = (55, 58)
WIZARD_BG = (10, 14, 20)


def has_transparency(img: Image.Image) -> bool:
    if img.mode != "RGBA":
        return False
    lo, _ = img.getchannel("A").getextrema()
    return lo < 250


def remove_black_background(img: Image.Image) -> Image.Image:
    img = img.convert("RGBA")
    pixels = img.load()
    w, h = img.size
    for y in range(h):
        for x in range(w):
            r, g, b, a = pixels[x, y]
            if r <= BLACK_THRESHOLD and g <= BLACK_THRESHOLD and b <= BLACK_THRESHOLD:
                pixels[x, y] = (r, g, b, 0)
    return img


def trim_transparent(img: Image.Image, padding: int = 8) -> Image.Image:
    bbox = img.getbbox()
    if not bbox:
        return img
    left, top, right, bottom = bbox
    left = max(0, left - padding)
    top = max(0, top - padding)
    right = min(img.width, right + padding)
    bottom = min(img.height, bottom + padding)
    return img.crop((left, top, right, bottom))


def fit_square(img: Image.Image, size: int) -> Image.Image:
    img = trim_transparent(img)
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    scale = min(size / img.width, size / img.height)
    nw, nh = int(img.width * scale), int(img.height * scale)
    resized = img.resize((nw, nh), Image.Resampling.LANCZOS)
    ox, oy = (size - nw) // 2, (size - nh) // 2
    canvas.paste(resized, (ox, oy), resized)
    return canvas


def prepare_rgba(source: Path) -> Image.Image:
    """Load source PNG; keep existing transparency, otherwise remove black matte."""
    raw = Image.open(source)
    img = raw.convert("RGBA")
    if has_transparency(img):
        print(f"  preserving alpha channel from {source.name}")
        return trim_transparent(img, padding=4)
    print(f"  removing black background from {source.name}")
    return trim_transparent(remove_black_background(img), padding=4)


def paranoia_variant(img: Image.Image) -> Image.Image:
    """Slightly brighter logo for Paranoia Mode (same geometry, enhanced glow)."""
    base = img.copy()
    bright = ImageEnhance.Brightness(base).enhance(1.12)
    bright = ImageEnhance.Contrast(bright).enhance(1.06)
    glow = bright.filter(ImageFilter.GaussianBlur(radius=6))
    out = Image.new("RGBA", base.size, (0, 0, 0, 0))
    glow_layer = Image.blend(
        Image.new("RGBA", base.size, (0, 0, 0, 0)),
        glow,
        0.35,
    )
    out.alpha_composite(glow_layer)
    out.alpha_composite(bright)
    return out


def write_png(img: Image.Image, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, format="PNG", optimize=True)
    print(f"  wrote {path.relative_to(ROOT)} ({path.stat().st_size:,} bytes)")


def write_ico(img: Image.Image, path: Path) -> None:
    """Multi-size ICO with alpha (taskbar / exe / installer)."""
    path.parent.mkdir(parents=True, exist_ok=True)
    master = fit_square(img, 256)
    master.save(path, format="ICO", sizes=ICO_SIZES)
    print(f"  wrote {path.relative_to(ROOT)} ({path.stat().st_size:,} bytes)")


def write_extension_icons(standard: Image.Image) -> None:
    for size in (16, 48, 128):
        icon = fit_square(standard, size)
        write_png(icon, EXTENSION / f"icon{size}.png")


def crop_globe_mark(img: Image.Image) -> Image.Image:
    """Globe portion of the horizontal ICMCLAB publisher lockup."""
    w, h = img.size
    return img.crop((0, 0, int(w * 0.40), h))


def paste_centered_rgba(canvas: Image.Image, overlay: Image.Image, box: tuple[int, int, int, int]) -> None:
    """Paste RGBA overlay centered inside (left, top, right, bottom) on an RGB/RGBA canvas."""
    max_w = box[2] - box[0]
    max_h = box[3] - box[1]
    scale = min(max_w / overlay.width, max_h / overlay.height)
    nw, nh = max(1, int(overlay.width * scale)), max(1, int(overlay.height * scale))
    resized = overlay.resize((nw, nh), Image.Resampling.LANCZOS)
    ox = box[0] + (max_w - nw) // 2
    oy = box[1] + (max_h - nh) // 2
    canvas.paste(resized, (ox, oy), resized)


def compose_wizard_sidebar(logo: Image.Image) -> Image.Image:
    canvas = Image.new("RGB", WIZARD_SIDEBAR_SIZE, WIZARD_BG)
    paste_centered_rgba(canvas, logo, (8, 36, WIZARD_SIDEBAR_SIZE[0] - 8, 150))
    return canvas


def compose_wizard_small(globe: Image.Image) -> Image.Image:
    canvas = Image.new("RGB", WIZARD_SMALL_SIZE, WIZARD_BG)
    paste_centered_rgba(canvas, globe, (3, 3, WIZARD_SMALL_SIZE[0] - 3, WIZARD_SMALL_SIZE[1] - 3))
    return canvas


def write_bmp(img: Image.Image, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    img.convert("RGB").save(path, format="BMP")
    print(f"  wrote {path.relative_to(ROOT)} ({path.stat().st_size:,} bytes)")


def generate_installer_publisher_assets(publisher_source: Path) -> None:
    print(f"Publisher source: {publisher_source}")
    logo_rgba = prepare_rgba(publisher_source)

    write_png(logo_rgba, ASSETS / "icmclab-logo.png")
    write_png(logo_rgba, PACKAGING / "icmclab-logo.png")
    write_bmp(compose_wizard_sidebar(logo_rgba), PACKAGING / "wizard-sidebar.bmp")
    globe = crop_globe_mark(logo_rgba)
    write_bmp(compose_wizard_small(globe), PACKAGING / "wizard-small.bmp")
    write_ico(fit_square(globe, 256), PACKAGING / "icmclab-setup.ico")


def main() -> int:
    parser = argparse.ArgumentParser(description="Update Fortiva brand PNG/ICO assets")
    default_icon = ASSETS / "source" / "fortiva-icon-source.png"
    default_logo = ASSETS / "source" / "fortiva-logo-source.png"
    parser.add_argument(
        "--icon-source",
        default=str(default_icon),
        help="Taskbar/window/installer icon master (Logo Icon 3)",
    )
    parser.add_argument(
        "--logo-source",
        default="",
        help="Optional separate UI logo; defaults to icon source when omitted",
    )
    parser.add_argument(
        "--publisher-source",
        default=str(ASSETS / "source" / "icmclab-logo-source.png"),
        help="ICMCLAB publisher lockup master (black matte OK); outputs transparent in-app PNG",
    )
    args = parser.parse_args()

    icon_source = Path(args.icon_source)
    if not icon_source.is_file():
        print(f"Icon source not found: {icon_source}", file=sys.stderr)
        return 1

    logo_source = Path(args.logo_source) if args.logo_source else icon_source
    if not logo_source.is_file():
        print(f"Logo source not found: {logo_source}", file=sys.stderr)
        return 1

    print(f"Icon source:  {icon_source}")
    print(f"Logo source:  {logo_source}")

    icon_rgba = prepare_rgba(icon_source)
    icon_standard = fit_square(icon_rgba, OUTPUT_SIZE)
    icon_paranoia = paranoia_variant(icon_standard)

    if logo_source.resolve() == icon_source.resolve():
        logo_standard = icon_standard
        logo_paranoia = icon_paranoia
    else:
        logo_rgba = prepare_rgba(logo_source)
        logo_standard = fit_square(logo_rgba, OUTPUT_SIZE)
        logo_paranoia = paranoia_variant(logo_standard)

    ASSETS.mkdir(parents=True, exist_ok=True)
    PACKAGING.mkdir(parents=True, exist_ok=True)

    standard_png = ASSETS / "fortiva-logo.png"
    paranoia_png = ASSETS / "fortiva-logo-paranoia.png"
    write_png(logo_standard, standard_png)
    write_png(logo_paranoia, paranoia_png)

    print("Generating ICO files (transparent) from icon source...")
    write_ico(icon_standard, ASSETS / "fortiva.ico")
    write_ico(icon_paranoia, ASSETS / "fortiva-paranoia.ico")
    write_ico(icon_standard, PACKAGING / "fortiva-setup.ico")

    print("Generating browser extension icons...")
    write_extension_icons(icon_standard)

    publisher_source = Path(args.publisher_source)
    if publisher_source.is_file():
        print("Generating installer publisher assets...")
        generate_installer_publisher_assets(publisher_source)
    else:
        print(f"Skipping installer publisher assets (not found: {publisher_source})")

    print("Done.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
