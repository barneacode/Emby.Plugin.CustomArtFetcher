"""Renders thumb.png: a poster card between template braces, drawn with analytic SDF AA."""
import math, struct, zlib

W = H = 256

def clamp(v, a=0.0, b=1.0):
    return a if v < a else b if v > b else v

def cov(d):
    """Coverage from a signed distance (negative inside), ~1px soft edge."""
    return clamp(0.5 - d)

def sd_round_rect(px, py, cx, cy, hw, hh, r):
    qx = abs(px - cx) - (hw - r)
    qy = abs(py - cy) - (hh - r)
    return math.hypot(max(qx, 0.0), max(qy, 0.0)) + min(max(qx, qy), 0.0) - r

def sd_circle(px, py, cx, cy, r):
    return math.hypot(px - cx, py - cy) - r

def sd_polygon(px, py, pts):
    n = len(pts)
    d = (px - pts[0][0]) ** 2 + (py - pts[0][1]) ** 2
    s = 1.0
    j = n - 1
    for i in range(n):
        ex, ey = pts[j][0] - pts[i][0], pts[j][1] - pts[i][1]
        wx, wy = px - pts[i][0], py - pts[i][1]
        t = clamp((wx * ex + wy * ey) / (ex * ex + ey * ey))
        bx, by = wx - ex * t, wy - ey * t
        d = min(d, bx * bx + by * by)
        c1 = py >= pts[i][1]
        c2 = py < pts[j][1]
        c3 = ex * wy > ey * wx
        if (c1 and c2 and c3) or (not c1 and not c2 and not c3):
            s = -s
        j = i
    return s * math.sqrt(d)

def sd_polyline(px, py, pts, half_width):
    d = 1e9
    for i in range(len(pts) - 1):
        ax, ay = pts[i]
        bx, by = pts[i + 1]
        ex, ey = bx - ax, by - ay
        wx, wy = px - ax, py - ay
        t = clamp((wx * ex + wy * ey) / (ex * ex + ey * ey))
        qx, qy = wx - ex * t, wy - ey * t
        d = min(d, qx * qx + qy * qy)
    return math.sqrt(d) - half_width

def bezier(p0, p1, p2, p3, steps):
    out = []
    for i in range(steps + 1):
        t = i / steps
        u = 1 - t
        a, b, c, d = u * u * u, 3 * u * u * t, 3 * u * t * t, t * t * t
        out.append((a * p0[0] + b * p1[0] + c * p2[0] + d * p3[0],
                    a * p0[1] + b * p1[1] + c * p2[1] + d * p3[1]))
    return out

def brace(x_spine, x_tip, y_top, y_bot, mirror=False):
    """Centerline of a curly brace: tips at x_tip, the middle cusp at x_spine."""
    y_mid = (y_top + y_bot) / 2
    if mirror:
        x_spine, x_tip = 2 * x_spine - x_spine, x_tip  # kept for clarity; mirroring is done by caller
    top = bezier((x_tip, y_top), (x_spine + (x_tip - x_spine) * 0.30, y_top),
                 (x_spine + (x_tip - x_spine) * 0.62, y_mid), (x_spine, y_mid), 28)
    bot = bezier((x_spine, y_mid), (x_spine + (x_tip - x_spine) * 0.62, y_mid),
                 (x_spine + (x_tip - x_spine) * 0.30, y_bot), (x_tip, y_bot), 28)
    return top + bot[1:]

def mix(c1, c2, t):
    return tuple(c1[i] + (c2[i] - c1[i]) * t for i in range(3))

# palette
BG_TOP, BG_BOT = (0x2A, 0x30, 0x35), (0x16, 0x1A, 0x1D)
CARD_TOP, CARD_BOT = (0x7A, 0xDC, 0x72), (0x32, 0x8E, 0x3C)
BRACE = (0x9A, 0xE8, 0x93)
SKY_TOP, SKY_BOT = (0x1C, 0x46, 0x2E), (0x14, 0x33, 0x22)
WHITE = (0xFF, 0xFF, 0xFF)

# geometry
CARD_CX, CARD_CY, CARD_HW, CARD_HH, CARD_R = 128.0, 128.0, 49.0, 67.0, 9.0
INNER_INSET = 7.0
L_BRACE = brace(30.0, 56.0, 62.0, 194.0)
R_BRACE = [(256.0 - x, y) for x, y in L_BRACE]
SUN = (151.0, 98.0, 10.0)
RIDGE_BACK = [(66.0, 190.0), (106.0, 124.0), (146.0, 190.0)]
RIDGE_FRONT = [(98.0, 190.0), (138.0, 134.0), (190.0, 190.0)]

def shade(px, py):
    """Colour and alpha of one pixel, compositing the shapes back to front."""
    acc = list(mix(BG_TOP, BG_BOT, py / H)) + [cov(sd_round_rect(px, py, 128.0, 128.0, 128.0, 128.0, 46.0))]

    def over(color, c):
        if c <= 0.0:
            return
        alpha = acc[3]
        na = c + alpha * (1 - c)
        if na > 0:
            for i in range(3):
                acc[i] = (color[i] * c + acc[i] * alpha * (1 - c)) / na
        acc[3] = na

    if 20 <= px <= 236 and 50 <= py <= 206:
        for pts in (L_BRACE, R_BRACE):
            over(BRACE, cov(sd_polyline(px, py, pts, 5.0)))

    card = cov(sd_round_rect(px, py, CARD_CX, CARD_CY, CARD_HW, CARD_HH, CARD_R))
    if card > 0:
        t = clamp((py - (CARD_CY - CARD_HH)) / (2 * CARD_HH))
        over(mix(CARD_TOP, CARD_BOT, t), card)

        # the image area inside the card
        inner = cov(sd_round_rect(px, py, CARD_CX, CARD_CY, CARD_HW - INNER_INSET,
                                  CARD_HH - INNER_INSET, CARD_R - 4.0))
        if inner > 0:
            over(mix(SKY_TOP, SKY_BOT, t), inner)
            over(WHITE, min(inner, cov(sd_circle(px, py, *SUN))))
            over(mix(WHITE, SKY_TOP, 0.30), min(inner, cov(sd_polygon(px, py, RIDGE_BACK))))
            over(WHITE, min(inner, cov(sd_polygon(px, py, RIDGE_FRONT))))

    return acc

rows = []
for y in range(H):
    row = bytearray()
    for x in range(W):
        r, g, b, a = shade(x + 0.5, y + 0.5)
        row += bytes((int(round(clamp(r, 0, 255))), int(round(clamp(g, 0, 255))),
                      int(round(clamp(b, 0, 255))), int(round(clamp(a) * 255))))
    rows.append(row)

raw = b"".join(b"\x00" + bytes(r) for r in rows)

def chunk(tag, data):
    return (struct.pack(">I", len(data)) + tag + data
            + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

png = (b"\x89PNG\r\n\x1a\n"
       + chunk(b"IHDR", struct.pack(">IIBBBBB", W, H, 8, 6, 0, 0, 0))
       + chunk(b"IDAT", zlib.compress(raw, 9))
       + chunk(b"IEND", b""))

with open("src/Emby.Plugin.CustomPosterFetcher/thumb.png", "wb") as f:
    f.write(png)
print("wrote", len(png), "bytes")
