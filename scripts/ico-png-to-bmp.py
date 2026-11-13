"""把 ICO 裡 PNG 壓縮的圖重新封裝成未壓縮 32bpp BMP (像素完全相同，只是不壓縮)。
用法: python scripts/ico-png-to-bmp.py <輸入.ico> <輸出.ico> [--keep-png-256]
--keep-png-256: 256x256 保留 PNG (其餘轉 BMP)。只用 Python 標準庫 (zlib)。
"""
import struct, sys, zlib


def decode_png(data):
    assert data[:8] == b"\x89PNG\r\n\x1a\n"
    pos, idat, ihdr = 8, [], None
    while pos < len(data):
        (length,) = struct.unpack(">I", data[pos:pos + 4])
        ctype = data[pos + 4:pos + 8]
        body = data[pos + 8:pos + 8 + length]
        pos += 12 + length
        if ctype == b"IHDR":
            ihdr = struct.unpack(">IIBBBBB", body)
        elif ctype == b"IDAT":
            idat.append(body)
        elif ctype == b"IEND":
            break
    w, h, depth, color, _comp, _filt, interlace = ihdr
    if depth != 8 or interlace != 0 or color not in (2, 6):
        raise ValueError(f"不支援的 PNG 格式: depth={depth} color={color} interlace={interlace}")
    bpp = 4 if color == 6 else 3
    raw = zlib.decompress(b"".join(idat))
    stride = w * bpp
    rows, prev, i = [], bytearray(stride), 0
    for _ in range(h):
        f = raw[i]; line = bytearray(raw[i + 1:i + 1 + stride]); i += 1 + stride
        for x in range(stride):
            a = line[x - bpp] if x >= bpp else 0
            b = prev[x]
            c = prev[x - bpp] if x >= bpp else 0
            if f == 1: line[x] = (line[x] + a) & 0xFF
            elif f == 2: line[x] = (line[x] + b) & 0xFF
            elif f == 3: line[x] = (line[x] + ((a + b) >> 1)) & 0xFF
            elif f == 4:
                p = a + b - c; pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[x] = (line[x] + pr) & 0xFF
            elif f != 0:
                raise ValueError(f"未知 filter {f}")
        rows.append(bytes(line)); prev = line
    rgba = []
    for r in rows:
        if bpp == 4:
            rgba.append(r)
        else:
            rgba.append(b"".join(r[k:k + 3] + b"\xff" for k in range(0, len(r), 3)))
    return w, h, rgba


def bmp_entry(w, h, rgba_rows):
    """ICO 用的 DIB：BITMAPINFOHEADER (高度 x2) + 由下而上的 BGRA + AND mask。"""
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    pixels = bytearray()
    for row in reversed(rgba_rows):
        for k in range(0, len(row), 4):
            r, g, b, a = row[k:k + 4]
            pixels += bytes((b, g, r, a))
    mask_stride = ((w + 31) // 32) * 4
    mask = bytearray()
    for row in reversed(rgba_rows):
        line = bytearray(mask_stride)
        for x in range(w):
            if row[x * 4 + 3] == 0:  # 全透明的像素在 AND mask 設 1 (給不認 alpha 的舊程式)
                line[x // 8] |= 0x80 >> (x % 8)
        mask += line
    return header + bytes(pixels) + bytes(mask)


def main():
    src, dst = sys.argv[1], sys.argv[2]
    keep256 = "--keep-png-256" in sys.argv
    d = open(src, "rb").read()
    _, typ, cnt = struct.unpack("<HHH", d[:6])
    entries = []
    for i in range(cnt):
        w, h, cc, res, planes, bitcount, size, off = struct.unpack("<BBBBHHII", d[6 + 16 * i:22 + 16 * i])
        img = d[off:off + size]
        rw, rh = (w or 256), (h or 256)
        if img[:8] == b"\x89PNG\r\n\x1a\n" and not (keep256 and rw == 256):
            pw, ph, rgba = decode_png(img)
            assert (pw, ph) == (rw, rh), (pw, ph, rw, rh)
            img = bmp_entry(pw, ph, rgba)
            planes, bitcount = 1, 32
        entries.append((w, h, cc, res, planes, bitcount, img))
    out = bytearray(struct.pack("<HHH", 0, 1, len(entries)))
    offset = 6 + 16 * len(entries)
    blobs = []
    for w, h, cc, res, planes, bitcount, img in entries:
        out += struct.pack("<BBBBHHII", w, h, cc, res, planes, bitcount, len(img), offset)
        offset += len(img); blobs.append(img)
    for b in blobs:
        out += b
    open(dst, "wb").write(out)
    print(f"{dst}: {len(entries)} 張, {len(out)} bytes")


if __name__ == "__main__":
    main()
