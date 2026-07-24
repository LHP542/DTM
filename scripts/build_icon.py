"""
DTM App-Icon-Generator (Checkmk-Familien-Look).

Design-Prinzipien (siehe Kroste/Checkmk/Assets/app.png):
- Gesaettigter dunkelblauer Grund (#1E3A5F) mit abgerundetem Quadrat —
  identisch zu Checkmk, damit die Kroste-Familie visuell zusammenhaengt.
- Motiv in Weiss (#FFFFFF), klar gezeichnet, ~60% Iconflaeche mit Luft am Rand.
- Kleiner Akzent-Kreis oben rechts in DTM-Teal (#2DD4BF), analog zu
  Checkmks gruenem "OK"-Punkt — kennzeichnet die App via Farbe.

Motiv: klassischer Datenbank-Zylinder (drei Scheiben, saubere Rundungen).

Erzeugt:
- DTM/Assets/dtm.png   (256x256, master fuer Fenster/Tray/AppImage)
- DTM/Assets/dtm.ico   (multi-res 16..256 fuer <ApplicationIcon>)
"""

from PIL import Image, ImageDraw

# Checkmk-Familien-Palette
BG        = (30, 58, 95, 255)      # #1E3A5F — dunkles Blau (wie Checkmk)
FG        = (255, 255, 255, 255)   # #FFFFFF — Motiv
FG_SHADOW = (220, 228, 240, 255)   # leicht abgesetzter Zylinder-Boden
ACCENT    = (45, 212, 191, 255)    # #2DD4BF — DTM-Teal-Akzentpunkt
ACCENT_R  = (24, 148, 132, 255)    # dunkler Akzent-Rand
TRANSP    = (0, 0, 0, 0)

SIZE   = 256
CORNER = 48    # Rundung des Grunds


def make_icon(size: int) -> Image.Image:
    """Baut das Icon in der angegebenen Kantenlaenge."""
    scale = size / 256
    img = Image.new("RGBA", (size, size), TRANSP)
    d = ImageDraw.Draw(img)

    # Grund: abgerundetes Quadrat, plattformneutral
    d.rounded_rectangle([(0, 0), (size - 1, size - 1)],
                        radius=int(CORNER * scale),
                        fill=BG)

    # === Datenbank-Zylinder (weiss, zentriert, mit Luft am Rand) ===
    cx = size / 2
    r_x = int(64 * scale)     # Halbachse X — schmaler als vorher, mehr Luft
    r_y = int(13 * scale)     # Halbachse Y — flache Ellipse
    top = int(78 * scale)     # y der obersten Ellipse
    gap = int(42 * scale)     # Abstand der Scheiben
    stroke = max(2, int(3 * scale))

    # Zylinder-Body (Rechteck zwischen oberer und unterster Ellipse)
    body_top = top
    body_bot = top + 3 * gap
    d.rectangle([(cx - r_x, body_top), (cx + r_x, body_bot)], fill=FG)

    # Zylinder-Boden (dezent schattiert, damit Rundung erkennbar bleibt)
    d.ellipse([(cx - r_x, body_bot - r_y), (cx + r_x, body_bot + r_y)],
              fill=FG_SHADOW)

    # Vier Scheibendeckel: oberste voll, die anderen als "Rille"
    for i in range(4):
        y = top + i * gap
        if i == 0:
            # Oberste Scheibe voll ausgefuellt (Deckel)
            d.ellipse([(cx - r_x, y - r_y), (cx + r_x, y + r_y)],
                      fill=FG, outline=BG, width=stroke)
        else:
            # Rille: Umriss in Grund-Farbe zeichnen, obere Haelfte durch
            # Zylinder-Fuellfarbe uebermalen (nur die "vordere Bogen"-Linie
            # bleibt sichtbar).
            d.ellipse([(cx - r_x, y - r_y), (cx + r_x, y + r_y)],
                      outline=BG, width=stroke)
            d.rectangle([(cx - r_x - 2, y - r_y - 2),
                         (cx + r_x + 2, y)], fill=FG)

    # === Akzent-Punkt oben rechts (analog Checkmk-Status-Kreis) ===
    # Sitzt komplett neben dem Motiv, damit kein Halo noetig ist —
    # sauberer Kontrast Teal auf dunkelblau.
    # Nur bei groesseren Groessen — bei 16x16 wuerde der Punkt matschen.
    if size >= 48:
        dot_r  = int(20 * scale)
        dot_cx = int(206 * scale)
        dot_cy = int(50 * scale)
        d.ellipse([(dot_cx - dot_r, dot_cy - dot_r),
                   (dot_cx + dot_r, dot_cy + dot_r)],
                  fill=ACCENT, outline=ACCENT_R, width=max(1, int(1.5 * scale)))

    return img


# 1) Master-PNG 256x256
master = make_icon(256)
master.save("/home/OsteL/Entwicklung/DTM/DTM/Assets/dtm.png", "PNG")
print("Wrote dtm.png (256x256)")

# 2) Multi-Res ICO fuer Windows-Exe (16/32/48/64/128/256)
sizes = [16, 24, 32, 48, 64, 128, 256]
icons = [make_icon(s) for s in sizes]
icons[0].save(
    "/home/OsteL/Entwicklung/DTM/DTM/Assets/dtm.ico",
    format="ICO",
    sizes=[(s, s) for s in sizes],
    append_images=icons[1:],
)
print(f"Wrote dtm.ico (multi-res: {sizes})")
