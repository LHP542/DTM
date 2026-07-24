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


def _draw_bg(d: ImageDraw.ImageDraw, size: int, scale: float) -> None:
    """Abgerundetes Quadrat als Grund (Kroste-Familien-Look)."""
    # Bei sehr kleinen Groessen kleinere Rundung, sonst wirkt es rund.
    corner = int(CORNER * scale) if size >= 48 else max(2, int(size * 0.14))
    d.rounded_rectangle([(0, 0), (size - 1, size - 1)],
                        radius=corner, fill=BG)


def make_icon_large(size: int) -> Image.Image:
    """
    Volles Design fuer >= 64px: Zylinder mit 3 Scheiben (2 Rillen),
    Akzent-Punkt oben rechts. Weniger Rillen als vorher (3->2), damit
    bei 64/128 keine Aliasing-Matschung entsteht.
    """
    scale = size / 256
    img = Image.new("RGBA", (size, size), TRANSP)
    d = ImageDraw.Draw(img)
    _draw_bg(d, size, scale)

    # === Datenbank-Zylinder (weiss, zentriert, mit Luft am Rand) ===
    cx = size / 2
    r_x = int(60 * scale)   # etwas schmaler
    r_y = int(15 * scale)   # flache Ellipse
    top = int(82 * scale)   # y der obersten Ellipse
    gap = int(52 * scale)   # Abstand der Scheiben (nur 3 Scheiben -> mehr Gap)
    stroke = max(2, int(3 * scale))

    body_top = top
    body_bot = top + 2 * gap
    d.rectangle([(cx - r_x, body_top), (cx + r_x, body_bot)], fill=FG)
    d.ellipse([(cx - r_x, body_bot - r_y), (cx + r_x, body_bot + r_y)],
              fill=FG_SHADOW)

    # 3 Scheibendeckel: oberste voll, dazwischen 1 Rille, unten nur der
    # Boden (kein extra Deckel).
    for i in range(3):
        y = top + i * gap
        if i == 0:
            # Oberste Scheibe voll (der eigentliche Deckel)
            d.ellipse([(cx - r_x, y - r_y), (cx + r_x, y + r_y)],
                      fill=FG, outline=BG, width=stroke)
        else:
            # Rille: Umriss zeichnen, obere Haelfte uebermalen
            d.ellipse([(cx - r_x, y - r_y), (cx + r_x, y + r_y)],
                      outline=BG, width=stroke)
            d.rectangle([(cx - r_x - 2, y - r_y - 2),
                         (cx + r_x + 2, y)], fill=FG)

    # Akzent-Punkt (Teal) oben rechts
    dot_r  = int(20 * scale)
    dot_cx = int(206 * scale)
    dot_cy = int(50 * scale)
    d.ellipse([(dot_cx - dot_r, dot_cy - dot_r),
               (dot_cx + dot_r, dot_cy + dot_r)],
              fill=ACCENT, outline=ACCENT_R, width=max(1, int(1.5 * scale)))

    return img


def make_icon_small(size: int) -> Image.Image:
    """
    Vereinfachte Variante fuer 16..48px (Windows-Taskbar, Explorer).
    Aggressives Padding, damit blauer Grund nicht komplett verschwindet;
    nur oberer Deckel + Rundung unten, keine Rillen, kein Akzent-Punkt.
    Zylinder wirkt dadurch wie ein "kleiner weisser Turm" auf blauem Grund
    (aus 30cm Entfernung noch als DB-Silhouette erkennbar).
    """
    img = Image.new("RGBA", (size, size), TRANSP)
    d = ImageDraw.Draw(img)
    _draw_bg(d, size, 1.0)

    cx = size / 2
    # Padding: 28% seitlich (viel Grund sichtbar, kein "Weisser Kloetzchen"-Effekt)
    pad_x = max(2, int(size * 0.28))
    r_x = size / 2 - pad_x

    # Vertikaler Aufbau: schmaler Rand oben/unten, dazwischen Zylinder
    top_y = int(size * 0.28)
    bot_y = int(size * 0.78)
    r_y   = max(2, int(size * 0.09))

    # Body
    d.rectangle([(cx - r_x, top_y), (cx + r_x, bot_y)], fill=FG)
    # Boden (leicht schattiert fuer Rundungs-Andeutung)
    d.ellipse([(cx - r_x, bot_y - r_y), (cx + r_x, bot_y + r_y)],
              fill=FG_SHADOW)
    # Deckel
    d.ellipse([(cx - r_x, top_y - r_y), (cx + r_x, top_y + r_y)],
              fill=FG)

    # Rille nur bei 48px sinnvoll — darunter matscht sie
    if size >= 48:
        stroke = 1
        mid_y = (top_y + bot_y) / 2
        d.ellipse([(cx - r_x, mid_y - r_y), (cx + r_x, mid_y + r_y)],
                  outline=BG, width=stroke)
        d.rectangle([(cx - r_x - 1, mid_y - r_y - 1),
                     (cx + r_x + 1, mid_y)], fill=FG)

    return img


def make_icon(size: int) -> Image.Image:
    """
    Dispatch:
    - 16..48px: vereinfachtes Design (small) — Windows-Taskbar/Explorer.
    - >=64px: volles Design (large) — Fenster/Tray/App-Grid.
    """
    return make_icon_small(size) if size <= 48 else make_icon_large(size)


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
