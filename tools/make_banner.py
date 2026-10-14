"""Draws docs/social-preview.png (README hero and GitHub social card, 1280×640) from the icon and the flyout screenshot.

Everything that matters stays inside GitHub's safe area (80 px from every edge), so the social card is never cropped.

    python tools/make_banner.py
"""
import os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DOCS = os.path.join(ROOT, "docs")
FONTS = os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts")
W, H, SAFE = 1280, 640, 80


def font(name, size):
    return ImageFont.truetype(os.path.join(FONTS, name), size)


def glow(img, xy, radius, color):
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    x, y = xy
    ImageDraw.Draw(layer).ellipse((x - radius, y - radius, x + radius, y + radius), fill=color)
    return Image.alpha_composite(img, layer.filter(ImageFilter.GaussianBlur(radius * 0.55)))


def rounded(im, r):
    mask = Image.new("L", im.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, im.size[0] - 1, im.size[1] - 1), r, fill=255)
    out = im.convert("RGBA"); out.putalpha(mask)
    return out


img = Image.new("RGBA", (W, H), (24, 24, 27, 255))
img = glow(img, (200, 150), 360, (255, 190, 60, 75))      # warm "room light" from the top left

# flyout on the right, scaled to the safe height, with a soft shadow
fly = Image.open(os.path.join(DOCS, "flyout.png")).convert("RGBA")
h = H - 2 * SAFE
fly = fly.resize((int(fly.size[0] * h / fly.size[1]), h), Image.LANCZOS)
x, y = W - SAFE - fly.size[0], SAFE
sh = Image.new("RGBA", img.size, (0, 0, 0, 0))
ImageDraw.Draw(sh).rounded_rectangle((x + 6, y + 12, x + fly.size[0] + 6, y + h + 12), 16, fill=(0, 0, 0, 160))
img = Image.alpha_composite(img, sh.filter(ImageFilter.GaussianBlur(14)))
img.alpha_composite(rounded(fly, 14), (x, y))

d = ImageDraw.Draw(img)
icon = Image.open(os.path.join(DOCS, "icon.png")).convert("RGBA").resize((80, 80), Image.LANCZOS)
img.alpha_composite(icon, (SAFE, SAFE + 4))
d.text((SAFE, 176), "AmbientLight", font=font("seguibl.ttf", 62), fill=(255, 255, 255))
d.text((SAFE, 242), "Calibrate", font=font("seguibl.ttf", 62), fill=(255, 200, 61))
body = font("segoeui.ttf", 26)
d.text((SAFE + 2, 344), "Monitor brightness from real room light.", font=body, fill=(215, 215, 220))
d.text((SAFE + 2, 381), "Colorimeter → lux → nits, over DDC/CI.", font=body, fill=(215, 215, 220))
d.text((SAFE + 2, 428), "Windows tray app · open source", font=body, fill=(255, 200, 61))
d.text((SAFE + 2, 520), "github.com/AndriyOrlov/AmbientLightCalibrate", font=font("segoeui.ttf", 19), fill=(150, 150, 155))

img.convert("RGB").save(os.path.join(DOCS, "social-preview.png"), optimize=True)
print("docs/social-preview.png")
