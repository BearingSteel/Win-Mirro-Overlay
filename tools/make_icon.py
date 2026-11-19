#!/usr/bin/env python3
"""Generate assets/app.ico (multi-size, PNG-compressed) from the logo geometry.

Source of truth: assets/logo.svg (512x512 viewBox). The shapes below mirror it
exactly, so no third-party SVG rasterizer is needed.
"""
import math
import os
import struct
import zlib

HERE = os.path.dirname(__file__)
ASSETS = os.path.join(HERE, "..", "assets")
OUT = os.path.join(ASSETS, "app.ico")
SIZES = [16, 24, 32, 48, 64, 128, 256]
SS = 4  # supersampling factor

VIEW = 512.0
ACCENT = (0x2D, 0xD4, 0xEF)

# --- geometry in 512x512 user units -------------------------------------
SCREEN = dict(x0=80, y0=72, x1=432, y1=328, r=56, stroke=36)
LENS = dict(cx=256, cy=200, r=64)
SHADOW = dict(x0=176, y0=408, x1=336, y1=440, r=16, alpha=0.45)


def sd_round_rect(px, py, x0, y0, x1, y1, r):
    cx = min(max(px, x0 + r), x1 - r)
    cy = min(max(py, y0 + r), y1 - r)
    return math.hypot(px - cx, py - cy) - r


def over(dst, src, alpha):
    """source-over composite of straight-alpha colors (r,g,b,a in 0..1)."""
    if alpha <= 0:
        return dst
    oa = alpha + dst[3] * (1 - alpha)
    if oa <= 0:
        return (0.0, 0.0, 0.0, 0.0)
    return (
        (src[0] * alpha + dst[0] * dst[3] * (1 - alpha)) / oa,
        (src[1] * alpha + dst[1] * dst[3] * (1 - alpha)) / oa,
        (src[2] * alpha + dst[2] * dst[3] * (1 - alpha)) / oa,
        oa,
    )


def render(size):
    """Return size*size straight-alpha RGBA bytes."""
    n = size * SS
    scale = size / VIEW  # output px per user unit
    out = bytearray(size * size * 4)

    sr, sg, sb = (c / 255.0 for c in ACCENT)

    for y in range(size):
        for x in range(size):
            r = g = b = a = 0.0
            for dy in range(SS):
                for dx in range(SS):
                    # sample position in user units
                    ux = (x * SS + dx + 0.5) / SS / scale
                    uy = (y * SS + dy + 0.5) / SS / scale

                    col = (0.0, 0.0, 0.0, 0.0)

                    d = sd_round_rect(ux, uy, SCREEN["x0"], SCREEN["y0"],
                                      SCREEN["x1"], SCREEN["y1"], SCREEN["r"])
                    if abs(d) <= SCREEN["stroke"] / 2:
                        col = over(col, (sr, sg, sb), 1.0)

                    if math.hypot(ux - LENS["cx"], uy - LENS["cy"]) <= LENS["r"]:
                        col = over(col, (sr, sg, sb), 1.0)

                    d = sd_round_rect(ux, uy, SHADOW["x0"], SHADOW["y0"],
                                      SHADOW["x1"], SHADOW["y1"], SHADOW["r"])
                    if d <= 0:
                        col = over(col, (sr, sg, sb), SHADOW["alpha"])

                    r += col[0] * col[3]
                    g += col[1] * col[3]
                    b += col[2] * col[3]
                    a += col[3]

            cnt = SS * SS
            o = (y * size + x) * 4
            if a > 0:
                out[o] = round(r / a * 255)
                out[o + 1] = round(g / a * 255)
                out[o + 2] = round(b / a * 255)
                out[o + 3] = round(a / cnt * 255)
    return bytes(out)


def png(size, rgba):
    raw = b"".join(b"\x00" + rgba[y * size * 4:(y + 1) * size * 4] for y in range(size))

    def chunk(tag, data):
        return (struct.pack(">I", len(data)) + tag + data +
                struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) +
            chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def main():
    images = [(sz, png(sz, render(sz))) for sz in SIZES]
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries, blobs = b"", b""
    for sz, data in images:
        entries += struct.pack("<BBBBHHII", sz % 256, sz % 256, 0, 0, 1, 32, len(data), offset)
        blobs += data
        offset += len(data)

    os.makedirs(ASSETS, exist_ok=True)
    with open(OUT, "wb") as f:
        f.write(header + entries + blobs)
    print("wrote", os.path.abspath(OUT), os.path.getsize(OUT), "bytes")

    with open(os.path.join(ASSETS, "app-preview.png"), "wb") as f:
        f.write(png(256, render(256)))


if __name__ == "__main__":
    main()
