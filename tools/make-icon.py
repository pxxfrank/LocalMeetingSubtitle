"""Generates the app icon (coral rounded tile + white caption-card glyph).

Run:  uv run --with pillow python dist/_makeicon.py
Writes src/LocalMeetingSubtitle.App/Assets/app.ico (multi-size) and dist/_icon-preview.png
"""
from pathlib import Path

from PIL import Image, ImageDraw

REPO = Path(r"G:\AI_Project\13-meetingsubtitle")
OUT_ICO = REPO / "src" / "LocalMeetingSubtitle.App" / "Assets" / "app.ico"
OUT_PREVIEW = REPO / "dist" / "_icon-preview.png"

ACCENT = (201, 100, 66, 255)   # #C96442, the UI accent
WHITE = (255, 255, 255, 255)

SIZES = [256, 128, 64, 48, 32, 24, 16]

# Caption card in the app's 24x24 icon space (matches Theme/Icons.xaml -> IconCaptions).
CARD = (3.0, 5.0, 21.0, 19.0)      # x0, y0, x1, y1
LINE_Y = 14.0
LINE1 = (7.0, 11.0)
LINE2 = (13.0, 17.0)
STROKE_24 = 1.9                    # stroke width in the 24 space
GLYPH_FRACTION = 0.60              # glyph occupies 60% of the tile


def render(size: int, supersample: int = 8) -> Image.Image:
    s = size * supersample
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # Coral rounded tile.
    d.rounded_rectangle([0, 0, s - 1, s - 1], radius=int(s * 0.22), fill=ACCENT)

    # Scale/centre the glyph.
    x0, y0, x1, y1 = CARD
    gw, gh = x1 - x0, y1 - y0
    scale = (s * GLYPH_FRACTION) / max(gw, gh)
    tx = (s - gw * scale) / 2 - x0 * scale
    ty = (s - gh * scale) / 2 - y0 * scale

    def px(x: float, y: float) -> tuple[float, float]:
        return (x * scale + tx, y * scale + ty)

    stroke = max(1, round(STROKE_24 * scale))
    radius = max(1, round(1.0 * scale))

    # Card outline.
    d.rounded_rectangle([px(x0, y0), px(x1, y1)], radius=radius, outline=WHITE, width=stroke)

    # Two caption lines with round caps.
    for (lx0, lx1) in (LINE1, LINE2):
        a = px(lx0, LINE_Y)
        b = px(lx1, LINE_Y)
        d.line([a, b], fill=WHITE, width=stroke)
        r = stroke / 2
        for cx, cy in (a, b):
            d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=WHITE)

    return img.resize((size, size), Image.LANCZOS)


frames = {size: render(size) for size in SIZES}

OUT_PREVIEW.parent.mkdir(parents=True, exist_ok=True)
frames[256].save(OUT_PREVIEW)

OUT_ICO.parent.mkdir(parents=True, exist_ok=True)
largest = frames[256]
largest.save(OUT_ICO, format="ICO", sizes=[(s, s) for s in sorted(SIZES)])

print("sizes :", ", ".join(str(s) for s in sorted(SIZES, reverse=True)))
print("ico   :", OUT_ICO, f"({OUT_ICO.stat().st_size / 1024:.1f} KB)")
print("png   :", OUT_PREVIEW)
