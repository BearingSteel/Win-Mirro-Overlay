#!/usr/bin/env python3
"""Generate assets/app.ico (multi-size, PNG-compressed) without third-party deps.

Design: a dark rounded tile, an outlined "source window" and an offset cyan
"mirror window" on top of it.
"""
import math
import os
import struct
import zlib

OUT = os.path.join(os.path.dirname(__file__), "..", "assets", "app.ico")
SIZES = [16, 24, 32, 48, 64, 128, 256]
SS = 4  # supersampling factor


def sd_round_rect(px, py, x0, y0, x1, y1, r):
    cx = min(max(px, x0 + r), x1 - r)
    cy = min(max(py, y0 + r), y1 - r)
    dx = px - cx
    dy = py - cy
    return math.hypot(dx, dy) - r


def lerp(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


def over(dst, src, alpha):
    return tuple(dst[i] * (1 - alpha) + src[i] * alpha for i in range(3))


def render(size):
    """Return size*size RGBA bytes (straight alpha)."""
    n = size * SS
    px = [0.0] * (n * n * 4)
    s = float(n)

    top = (0x2E, 0x35, 0x46)
    bottom = (0x15, 0x18, 0x21)
    accent = (0x4C, 0xC2, 0xFF)

    # tile
    for y in range(n):
        for x in range(n):
            i = (y * n + x) * 4
            cx, cy = x + 0.5, y + 0.5
            d = sd_round_rect(cx, cy, s * 0.04, s * 0.04, s * 0.96, s * 0.96, s * 0.215)
            cover = min(max(0.5 - d, 0.0), 1.0)  # 1px AA
            if cover <= 0:
                continue
            col = lerp(top, bottom, cy / s)
            px[i] = col[0]
            px[i + 1] = col[1]
            px[i + 2] = col[2]
            px[i + 3] = 255.0 * cover

    def rect(x0, y0, x1, y1, r, stroke, fill=None, stroke_w=None):
        for y in range(n):
            for x in range(n):
                i = (y * n + x) * 4
                if px[i + 3] == 0:
                    continue
                cx, cy = x + 0.5, y + 0.5
                d = sd_round_rect(cx, cy, x0 * s, y0 * s, x1 * s, y1 * s, r * s)
                alpha = 0.0
                if fill is not None:
                    alpha = min(max(0.5 - d, 0.0), 1.0)
                    if alpha > 0:
                        for c in range(3):
                            px[i + c] = px[i + c] * (1 - alpha) + fill[c] * alpha
                        px[i + 3] = px[i + 3] * (1 - alpha) + 255.0 * alpha
                if stroke is not None:
                    sw = (0.5 / s) if stroke_w is None else stroke_w
                    band = min(max(sw * s * 0.5 - abs(d), 0.0), 1.0)
                    if band > 0:
                        for c in range(3):
                            px[i + c] = px[i + c] * (1 - band) + stroke[c] * band
                        px[i + 3] = px[i + 3] * (1 - band) + 255.0 * band

    # source window (behind, light outline)
    rect(0.18, 0.24, 0.62, 0.64, 0.07, (235, 240, 250), (0x24, 0x2A, 0x36), 0.030)
    # mirror window (front, cyan)
    rect(0.38, 0.38, 0.84, 0.78, 0.07, accent, (0x10, 0x3A, 0x52), 0.030)

    # box downscale
    out = bytearray(size * size * 4)
    for y in range(size):
        for x in range(size):
            r = g = b = a = 0.0
            for dy in range(SS):
                for dx in range(SS):
                    i = ((y * SS + dy) * n + (x * SS + dx)) * 4
                    ta = px[i + 3] / 255.0
                    r += px[i] * ta
                    g += px[i + 1] * ta
                    b += px[i + 2] * ta
                    a += ta
            cnt = SS * SS
            o = (y * size + x) * 4
            if a > 0:
                out[o] = round(r / a)
                out[o + 1] = round(g / a)
                out[o + 2] = round(b / a)
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
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "wb") as f:
        f.write(header + entries + blobs)
    print("wrote", OUT, os.path.getsize(OUT), "bytes")
    # preview
    with open(os.path.join(os.path.dirname(OUT), "app-preview.png"), "wb") as f:
        f.write(png(256, render(256)))


if __name__ == "__main__":
    main()
