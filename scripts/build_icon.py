"""
DTM App-Icon-Generator.

Design: klassischer Datenbank-Zylinder in DTM's Teal (#2DD4BF) auf
abgerundetem dunklem Grund (#161C23), thematisch DB-Manager, ohne
Text (funktioniert auch als 16x16-Favicon).

Erzeugt:
- /home/OsteL/Entwicklung/DTM/DTM/Assets/dtm.png   (256x256, master)
- /home/OsteL/Entwicklung/DTM/DTM/Assets/dtm.ico   (Windows-Multi-Res)
"""

from PIL import Image, ImageDraw

# DTM-Farben (aus App.axaml)
ACCENT   = (45, 212, 191, 255)     # #2DD4BF
ACCENT_D = (30, 150, 135, 255)     # dunkler fuer Ellipsen-Unterkante
SURFACE  = (22, 28, 35, 255)       # #161C23 (fast schwarz)
BORDER   = (42, 51, 61, 255)       # #2A333D
TRANSP   = (0, 0, 0, 0)

SIZE = 256
CORNER = 48  # abgerundete Ecke des Grunds

def make_icon(size: int) -> Image.Image:
    """Baut das Icon in der angegebenen Kantenlaenge."""
    scale = size / 256
    img = Image.new("RGBA", (size, size), TRANSP)
    d = ImageDraw.Draw(img)

    # Grund: abgerundetes Quadrat (App-Icon-Stil, plattformneutral)
    corner = int(CORNER * scale)
    d.rounded_rectangle([(0, 0), (size - 1, size - 1)],
                        radius=corner, fill=SURFACE, outline=BORDER,
                        width=max(1, int(2 * scale)))

    # Datenbank-Zylinder: drei Scheiben, oben nach unten mit
    # abnehmender Sichtbarkeit (Perspektive)
    cx = size / 2
    r_x = int(72 * scale)   # Ellipsen-Halbachse X
    r_y = int(14 * scale)   # Ellipsen-Halbachse Y (flach)
    top = int(72 * scale)    # y der oberen Ellipse
    gap = int(46 * scale)    # Abstand zwischen den Scheiben
    stroke = max(2, int(4 * scale))

    # Zylinder-Body zwischen oberer und unterster Ellipse (Rechteck-Seiten)
    body_top = top
    body_bot = top + 3 * gap
    d.rectangle([(cx - r_x, body_top), (cx + r_x, body_bot)],
                fill=ACCENT)

    # Untere Rundung (Boden), damit der Zylinder unten geschlossen ist
    d.ellipse([(cx - r_x, body_bot - r_y), (cx + r_x, body_bot + r_y)],
              fill=ACCENT_D, outline=None)

    # Drei "Deckel" (Ellipsen) — obere ist voll, mittlere nur Umriss (Rille)
    for i in range(4):
        y = top + i * gap
        if i == 0:
            # Oberste: voll ausgefuellt
            d.ellipse([(cx - r_x, y - r_y), (cx + r_x, y + r_y)],
                      fill=ACCENT, outline=SURFACE, width=stroke)
        else:
            # Rille: nur die Frontlinie (untere Haelfte) sichtbar,
            # obere Haelfte deckt der darueberliegende Zylinder ab.
            # Trick: schwarzen Umriss zeichnen, dann die obere Haelfte
            # mit Zylinder-Farbe uebermalen.
            d.ellipse([(cx - r_x, y - r_y), (cx + r_x, y + r_y)],
                      fill=None, outline=SURFACE, width=stroke)
            # Obere Haelfte uebermalen, damit nur die "vordere Rille" bleibt
            d.rectangle([(cx - r_x - 2, y - r_y - 2),
                         (cx + r_x + 2, y)],
                        fill=ACCENT)

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
