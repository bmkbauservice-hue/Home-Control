"""Zeichnet die Demo-Kamerabilder für den Demo-Modus (backend/HomeControl.Api/Demo/Bilder).

Bewusst selbst gezeichnet statt Fotos: keine Rechte Dritter, keine echte Wohnung, nichts Persönliches.
Je Kamera zwei Fassungen; "Neues Bild" im Demo wechselt zwischen ihnen.

Aufruf aus dem Repo-Hauptordner (braucht Pillow, z. B. in einer venv: pip install pillow):
    python3 tools/demo-bilder/bilder_erzeugen.py
"""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ZIEL = Path("backend/HomeControl.Api/Demo/Bilder")
BREITE, HOEHE = 640, 360
SCHRIFT = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"


def schrift(groesse):
    try:
        return ImageFont.truetype(SCHRIFT, groesse)
    except OSError:
        return ImageFont.load_default()


def verlauf(bild, oben, unten, bis_y):
    """Senkrechter Farbverlauf von oben nach unten (Himmel, Wand)."""
    zeichnen = ImageDraw.Draw(bild)
    for y in range(bis_y):
        t = y / max(bis_y - 1, 1)
        farbe = tuple(int(oben[i] + (unten[i] - oben[i]) * t) for i in range(3))
        zeichnen.line([(0, y), (BREITE, y)], fill=farbe)


def kamera_look(bild, abend):
    """Leichte Unschärfe, Vignette und Aufschrift DEMO-BILD: Es soll erkennbar kein echtes Bild sein."""
    bild = bild.filter(ImageFilter.GaussianBlur(0.6))
    maske = Image.new("L", (BREITE, HOEHE), 0)
    ImageDraw.Draw(maske).ellipse([-120, -90, BREITE + 120, HOEHE + 90], fill=255)
    maske = maske.filter(ImageFilter.GaussianBlur(70))
    dunkel = Image.new("RGB", (BREITE, HOEHE), (10, 12, 20))
    bild = Image.composite(bild, dunkel, maske)
    if abend:
        bild = Image.blend(bild, Image.new("RGB", (BREITE, HOEHE), (20, 30, 70)), 0.35)
    zeichnen = ImageDraw.Draw(bild)
    zeichnen.rounded_rectangle([BREITE - 150, HOEHE - 42, BREITE - 14, HOEHE - 14], 8, fill=(0, 0, 0))
    zeichnen.text((BREITE - 140, HOEHE - 38), "DEMO-BILD", font=schrift(18), fill=(255, 255, 255))
    return bild


def haustuer(fassung):
    abend = fassung == 2
    bild = Image.new("RGB", (BREITE, HOEHE))
    verlauf(bild, (214, 200, 180), (190, 176, 156), HOEHE)  # Putzwand
    z = ImageDraw.Draw(bild)
    # Boden mit Fliesen
    z.rectangle([0, 290, BREITE, HOEHE], fill=(120, 116, 110))
    for x in range(0, BREITE, 64):
        z.line([(x, 290), (x - 40, HOEHE)], fill=(105, 101, 96), width=2)
    # Türrahmen und Tür
    z.rectangle([230, 40, 410, 292], fill=(90, 86, 80))
    z.rectangle([242, 52, 398, 292], fill=(130, 82, 48))
    for y in (70, 170):
        z.rectangle([258, y, 382, y + 80], outline=(105, 64, 36), width=4)
    z.ellipse([370, 172, 384, 186], fill=(200, 190, 150))
    # Hausnummer und Lampe
    z.rounded_rectangle([430, 90, 470, 130], 6, fill=(40, 60, 90))
    z.text((440, 96), "7", font=schrift(26), fill=(255, 255, 255))
    z.rectangle([175, 80, 195, 115], fill=(60, 60, 60))
    lampe = (255, 220, 140) if abend else (210, 210, 200)
    z.ellipse([170, 112, 200, 142], fill=lampe)
    # Fußmatte und Pflanze
    z.rectangle([250, 296, 390, 318], fill=(70, 60, 50))
    z.rectangle([470, 230, 520, 292], fill=(150, 90, 60))
    for dx, dy in ((-30, -60), (0, -80), (30, -55), (-10, -40), (15, -35)):
        z.ellipse([495 + dx - 22, 230 + dy - 14, 495 + dx + 22, 230 + dy + 14], fill=(70, 130, 70))
    if abend:
        # Paket vor der Tür und Lichtkegel der Lampe
        z.rectangle([300, 262, 352, 300], fill=(180, 140, 90))
        z.line([(300, 280), (352, 280)], fill=(150, 110, 70), width=3)
        licht = Image.new("RGB", (BREITE, HOEHE), (255, 210, 130))
        maske = Image.new("L", (BREITE, HOEHE), 0)
        ImageDraw.Draw(maske).ellipse([80, 60, 300, 300], fill=90)
        bild = Image.composite(licht, bild, maske.filter(ImageFilter.GaussianBlur(40)))
    return kamera_look(bild, abend)


def garten(fassung):
    abend = fassung == 2
    bild = Image.new("RGB", (BREITE, HOEHE))
    himmel = ((120, 170, 225), (200, 225, 245)) if not abend else ((60, 70, 120), (240, 160, 120))
    verlauf(bild, himmel[0], himmel[1], 200)
    z = ImageDraw.Draw(bild)
    # Rasen
    z.rectangle([0, 200, BREITE, HOEHE], fill=(92, 150, 80))
    z.polygon([(0, 260), (BREITE, 230), (BREITE, HOEHE), (0, HOEHE)], fill=(80, 138, 70))
    # Sonne bzw. Abendsonne
    if abend:
        z.ellipse([470, 150, 540, 220], fill=(255, 190, 120))
        z.rectangle([0, 200, BREITE, 206], fill=(92, 150, 80))
    else:
        z.ellipse([520, 30, 580, 90], fill=(255, 235, 150))
        z.ellipse([90, 40, 190, 80], fill=(245, 248, 252))
        z.ellipse([140, 30, 230, 75], fill=(245, 248, 252))
    # Zaun
    for x in range(0, BREITE, 34):
        z.rectangle([x, 160, x + 22, 214], fill=(225, 220, 205))
        z.polygon([(x, 160), (x + 11, 148), (x + 22, 160)], fill=(225, 220, 205))
    z.rectangle([0, 176, BREITE, 184], fill=(200, 195, 180))
    # Baum
    z.rectangle([110, 150, 132, 262], fill=(110, 80, 55))
    for cx, cy, r in ((121, 120, 58), (85, 145, 40), (158, 145, 42)):
        z.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(60, 120, 60))
    # Gartenweg und Beet
    z.polygon([(330, HOEHE), (410, HOEHE), (380, 214), (360, 214)], fill=(190, 175, 150))
    for x in range(470, 620, 24):
        z.ellipse([x, 268, x + 16, 284], fill=(230, 120, 140) if not abend else (200, 110, 130))
    if abend:
        # Ball auf dem Rasen
        z.ellipse([230, 285, 262, 317], fill=(240, 240, 240))
        z.arc([230, 285, 262, 317], 200, 340, fill=(60, 90, 200), width=4)
    return kamera_look(bild, abend)


def main():
    ZIEL.mkdir(parents=True, exist_ok=True)
    for name, zeichne in (("haustuer", haustuer), ("garten", garten)):
        for fassung in (1, 2):
            pfad = ZIEL / f"{name}-{fassung}.jpg"
            zeichne(fassung).save(pfad, "JPEG", quality=82, optimize=True)
            print(f"{pfad} ({pfad.stat().st_size // 1024} KB)")


if __name__ == "__main__":
    main()
