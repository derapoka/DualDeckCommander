"""
Dual Deck Commander grafikai elemek előállítása (ikon, logó, indítóképernyő, telepítő képek).
Használat:  python tools/make_assets.py   (Pillow és az Inter betűtípus kell hozzá)
"""
import os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "src", "Assets")
INSTALLER = os.path.join(ROOT, "installer")
os.makedirs(ASSETS, exist_ok=True)
os.makedirs(INSTALLER, exist_ok=True)

BLUE = (59, 130, 246)
BLUE_DARK = (37, 99, 235)
GREEN = (52, 211, 153)
GREEN_DARK = (16, 185, 129)
BG_TOP = (24, 27, 36)
BG_BOTTOM = (13, 15, 21)
WHITE = (241, 245, 249)
DIM = (148, 163, 184)


def font(weight, size):
    names = {
        "black": "Inter-Black.ttf", "bold": "Inter-Bold.ttf", "semibold": "Inter-SemiBold.ttf",
        "medium": "Inter-Medium.ttf", "regular": "Inter-Regular.ttf", "light": "Inter-Light.ttf",
    }
    for d in ["/usr/share/fonts/truetype/inter", "/usr/share/fonts/opentype/inter", "/usr/share/fonts"]:
        for root, _, files in os.walk(d):
            for f in files:
                if f.lower() == names[weight].lower() or f.lower() == names[weight].lower().replace(".ttf", ".otf"):
                    return ImageFont.truetype(os.path.join(root, f), size)
    from matplotlib import font_manager  # tartalék
    return ImageFont.truetype(font_manager.findfont("DejaVu Sans"), size)


def vertical_gradient(w, h, top, bottom):
    img = Image.new("RGB", (w, h), top)
    d = ImageDraw.Draw(img)
    for y in range(h):
        t = y / max(1, h - 1)
        c = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3))
        d.line([(0, y), (w, y)], fill=c)
    return img


def draw_logo(size, detail=True):
    """Két "deck" (panel) egymás mellett, alul egy lemez – átlátszó háttéren."""
    s = size * 4  # túlmintavételezés a sima élekért
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    pad = int(s * 0.08)
    gap = int(s * 0.06)
    w = (s - 2 * pad - gap) // 2
    top = int(s * 0.12)
    bottom = s - int(s * 0.12)
    r = int(s * 0.09)

    panels = [((pad, top, pad + w, bottom), BLUE, BLUE_DARK),
              ((pad + w + gap, top, pad + 2 * w + gap, bottom), GREEN, GREEN_DARK)]
    for (x0, y0, x1, y1), col, dark in panels:
        d.rounded_rectangle((x0, y0, x1, y1), radius=r, fill=col)
        # fejléc sáv
        d.rounded_rectangle((x0, y0, x1, y0 + int(s * 0.13)), radius=r, fill=dark)
        d.rectangle((x0, y0 + int(s * 0.07), x1, y0 + int(s * 0.13)), fill=dark)
        if detail and size >= 32:
            # fájlsorok
            lx0 = x0 + int(w * 0.16)
            for i in range(4):
                y = y0 + int(s * 0.22) + i * int(s * 0.115)
                lx1 = x1 - int(w * (0.16 if i % 2 == 0 else 0.34))
                d.rounded_rectangle((lx0, y, lx1, y + int(s * 0.045)), radius=int(s * 0.02),
                                    fill=(255, 255, 255, 215))

    if detail and size >= 48:
        # lemez a jobb alsó sarokban
        cr = int(s * 0.2)
        cx, cy = s - pad - int(cr * 0.75), bottom - int(cr * 0.7)
        d.ellipse((cx - cr - int(s * 0.025), cy - cr - int(s * 0.025), cx + cr + int(s * 0.025), cy + cr + int(s * 0.025)),
                  fill=BG_BOTTOM)
        d.ellipse((cx - cr, cy - cr, cx + cr, cy + cr), fill=(226, 232, 240))
        d.ellipse((cx - int(cr * 0.78), cy - int(cr * 0.78), cx + int(cr * 0.78), cy + int(cr * 0.78)),
                  outline=(148, 163, 184), width=max(2, int(s * 0.008)))
        d.ellipse((cx - int(cr * 0.28), cy - int(cr * 0.28), cx + int(cr * 0.28), cy + int(cr * 0.28)),
                  fill=(100, 116, 139))
        d.ellipse((cx - int(cr * 0.13), cy - int(cr * 0.13), cx + int(cr * 0.13), cy + int(cr * 0.13)),
                  fill=BG_BOTTOM)
    return img.resize((size, size), Image.LANCZOS)


# ---- ikon --------------------------------------------------------------------
sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
icons = [draw_logo(sz, detail=sz >= 32) for sz in sizes]
icons[-1].save(os.path.join(ASSETS, "app.ico"), sizes=[(s, s) for s in sizes], append_images=icons[:-1])
draw_logo(256).save(os.path.join(ASSETS, "logo.png"))


# ---- indítóképernyő (2x felbontás: 1280 x 760) ---------------------------------
def splash():
    W, H = 1280, 760
    img = vertical_gradient(W, H, BG_TOP, BG_BOTTOM).convert("RGBA")

    # finom rács
    grid = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    g = ImageDraw.Draw(grid)
    for x in range(0, W, 40):
        g.line([(x, 0), (x, H)], fill=(255, 255, 255, 9))
    for y in range(0, H, 40):
        g.line([(0, y), (W, y)], fill=(255, 255, 255, 9))
    img = Image.alpha_composite(img, grid)

    # színes fény a logó mögött
    glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    gd.ellipse((60, 110, 520, 570), fill=(59, 130, 246, 70))
    gd.ellipse((240, 200, 640, 600), fill=(16, 185, 129, 55))
    glow = glow.filter(ImageFilter.GaussianBlur(90))
    img = Image.alpha_composite(img, glow)

    logo = draw_logo(300)
    img.alpha_composite(logo, (110, 190))

    d = ImageDraw.Draw(img)
    d.text((468, 150), "Dual Deck", font=font("black", 104), fill=WHITE)
    # "Commander" a logó színeivel (kék -> zöld átmenet)
    cf = font("black", 104)
    cw = int(d.textlength("Commander", font=cf)) + 10
    mask = Image.new("L", (cw, 140), 0)
    ImageDraw.Draw(mask).text((0, 0), "Commander", font=cf, fill=255)
    grad = Image.new("RGBA", (cw, 140))
    gdr = ImageDraw.Draw(grad)
    for x in range(cw):
        t = x / max(1, cw - 1)
        c = tuple(int(BLUE[i] + (GREEN[i] - BLUE[i]) * t) for i in range(3)) + (255,)
        gdr.line([(x, 0), (x, 140)], fill=c)
    img.paste(grad, (468, 262), mask)
    d.text((474, 388), "Kétpaneles fájlkezelő", font=font("semibold", 31), fill=(203, 213, 225))
    d.text((474, 428), "emulátorokhoz és lemezképekhez", font=font("semibold", 31), fill=(203, 213, 225))

    # formátum-címkék
    x = 476
    for label, col in [("Xbox 360", GREEN_DARK), ("Amiga", BLUE), ("ADF · DMS · LHA · LZX", (100, 116, 139)),
                       ("ZIP · RAR · 7z", (100, 116, 139))]:
        f = font("semibold", 24)
        tw = d.textlength(label, font=f)
        d.rounded_rectangle((x, 492, x + tw + 32, 536), radius=22, fill=col + (255,))
        d.text((x + 16, 499), label, font=f, fill=(255, 255, 255))
        x += tw + 32 + 14

    # alsó sáv
    d.line([(0, H - 120), (W, H - 120)], fill=(44, 50, 64, 255), width=2)
    d.text((64, H - 86), "Készítette: Baranyi László", font=font("medium", 28), fill=DIM)
    d.text((64, H - 48), "Freeware – ingyenes program", font=font("regular", 24), fill=(100, 116, 139))
    # a jobb alsó sarokba futás közben kerül a verziószám és az állapot
    return img.convert("RGB")


splash().save(os.path.join(ASSETS, "splash.png"), optimize=True)


# ---- telepítő képek (Inno Setup) ----------------------------------------------
def wizard_image(w, h):
    img = vertical_gradient(w, h, BG_TOP, BG_BOTTOM).convert("RGBA")
    glow = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    gd.ellipse((-w * 0.3, h * 0.15, w * 1.0, h * 0.6), fill=(59, 130, 246, 80))
    glow = glow.filter(ImageFilter.GaussianBlur(w * 0.25))
    img = Image.alpha_composite(img, glow)
    ls = int(w * 0.62)
    img.alpha_composite(draw_logo(ls), ((w - ls) // 2, int(h * 0.2)))
    d = ImageDraw.Draw(img)
    f = font("black", int(w * 0.17))
    t = "Dual Deck"
    tw = d.textlength(t, font=f)
    d.text(((w - tw) / 2, h * 0.2 + ls + w * 0.04), t, font=f, fill=WHITE)
    fc = font("bold", int(w * 0.13))
    tc = "Commander"
    tw = d.textlength(tc, font=fc)
    d.text(((w - tw) / 2, h * 0.2 + ls + w * 0.24), tc, font=fc, fill=GREEN)
    f2 = font("medium", int(w * 0.075))
    t2 = "emulátorokhoz és"
    t3 = "lemezképekhez"
    for i, tt in enumerate([t2, t3]):
        tw = d.textlength(tt, font=f2)
        d.text(((w - tw) / 2, h * 0.2 + ls + w * 0.46 + i * w * 0.1), tt, font=f2, fill=DIM)
    return img.convert("RGB")


for scale, suffix in [(1, ""), (1.5, "-150"), (2, "-200")]:
    wizard_image(int(164 * scale), int(314 * scale)).save(os.path.join(INSTALLER, f"wizard{suffix}.bmp"))
    small = Image.new("RGB", (int(55 * scale), int(58 * scale)), (255, 255, 255))
    lg = draw_logo(int(48 * scale))
    small.paste(lg, ((small.width - lg.width) // 2, (small.height - lg.height) // 2), lg)
    small.save(os.path.join(INSTALLER, f"wizard-small{suffix}.bmp"))

# NSIS fejléckép (150 x 57, fehér háttér, jobbra igazítva a logó)
for scale, suffix in [(1, ""), (2, "-200")]:
    hw, hh = int(150 * scale), int(57 * scale)
    head = Image.new("RGB", (hw, hh), (255, 255, 255))
    lg = draw_logo(int(48 * scale))
    head.paste(lg, (hw - lg.width - int(6 * scale), (hh - lg.height) // 2), lg)
    head.save(os.path.join(INSTALLER, f"header{suffix}.bmp"))

print("kész:", ASSETS, INSTALLER)
