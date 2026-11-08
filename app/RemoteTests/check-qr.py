# Reads back the QR codes `RemoteTests qr` made and checks each says exactly what went in. Usage: python check-qr.py <folder>
#
# OpenCV's own decoder reads them first, as a second implementation. Pairing URLs, the only codes Capri shows, must all
# read in OpenCV. For the sweep of every version and level, a code OpenCV can't read (it misses a few version 21 Low ones)
# is checked here instead: its format bits, every block's Reed-Solomon syndromes and its data, from ISO/IEC 18004's tables.
import sys
from pathlib import Path

import cv2
from PIL import Image

EXP, LOG = [0] * 512, [0] * 256
x = 1
for i in range(255):
    EXP[i], LOG[x] = x, i
    x <<= 1
    if x & 0x100:
        x ^= 0x11D
for i in range(255, 512):
    EXP[i] = EXP[i - 255]

# ISO/IEC 18004 Table 9, by level L M Q H then version: error correction codewords per block, and the number of blocks.
ECC = {
    "Low": [7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
    "Medium": [10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28],
    "Quartile": [13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
    "High": [17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
}
BLOCKS = {
    "Low": [1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25],
    "Medium": [1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49],
    "Quartile": [1, 1, 2, 2, 4, 4, 6, 6, 8, 8, 8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68],
    "High": [1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81],
}
MASKS = [lambda x, y: (x + y) % 2 == 0, lambda x, y: y % 2 == 0, lambda x, y: x % 3 == 0, lambda x, y: (x + y) % 3 == 0,
         lambda x, y: (x // 3 + y // 2) % 2 == 0, lambda x, y: x * y % 2 + x * y % 3 == 0,
         lambda x, y: (x * y % 2 + x * y % 3) % 2 == 0, lambda x, y: ((x + y) % 2 + x * y % 3) % 2 == 0]


def alignment(version):
    if version == 1:
        return []
    count = version // 7 + 2
    step = 26 if version == 32 else -(-(version * 4 + 4) // (count * 2 - 2)) * 2
    return [6] + [version * 4 + 10 - step * i for i in range(count - 2, -1, -1)]


def function_modules(version):
    size = version * 4 + 17
    used = [[False] * size for _ in range(size)]

    def mark(x0, y0, x1, y1):
        for yy in range(max(0, y0), min(size, y1)):
            for xx in range(max(0, x0), min(size, x1)):
                used[yy][xx] = True

    mark(0, 0, 9, 9)
    mark(size - 8, 0, size, 9)
    mark(0, size - 8, 9, size)
    for i in range(size):
        used[6][i] = used[i][6] = True
    spots = alignment(version)
    for a in spots:
        for b in spots:
            if (a, b) not in [(6, 6), (6, spots[-1]), (spots[-1], 6)]:
                mark(a - 2, b - 2, a + 3, b + 3)
    if version >= 7:
        mark(size - 11, 0, size - 8, 6)
        mark(0, size - 11, 6, size - 8)
    return used


def syndromes_ok(path, version, level, text, scale):
    size = version * 4 + 17
    image = Image.open(path).convert("L")
    dark = [[image.getpixel(((xx + 4) * scale + scale // 2, (yy + 4) * scale + scale // 2)) < 128 for xx in range(size)] for yy in range(size)]
    bits = 0
    for i, (xx, yy) in enumerate([(8, i) for i in range(6)] + [(8, 7), (8, 8), (7, 8)] + [(14 - i, 8) for i in range(9, 15)]):
        bits |= dark[yy][xx] << i
    found = None
    for data in range(32):
        rem = data
        for _ in range(10):
            rem = (rem << 1) ^ ((rem >> 9) * 0x537)
        if ((data << 10) | rem) ^ 0x5412 == bits:
            found = data
    if found is None or {1: "Low", 0: "Medium", 3: "Quartile", 2: "High"}[found >> 3] != level:
        return False
    mask, used, stream = MASKS[found & 7], function_modules(version), []
    right = size - 1
    while right >= 1:
        if right == 6:
            right = 5
        for vertical in range(size):
            for j in range(2):
                xx = right - j
                yy = size - 1 - vertical if ((right + 1) & 2) == 0 else vertical
                if not used[yy][xx]:
                    stream.append(dark[yy][xx] ^ mask(xx, yy))
        right -= 2
    words = [int("".join("1" if b else "0" for b in stream[i:i + 8]), 2) for i in range(0, len(stream) // 8 * 8, 8)]
    ecc, count = ECC[level][version - 1], BLOCKS[level][version - 1]
    total = len(words)
    short = count - total % count
    sizes = [total // count - ecc + (0 if i < short else 1) for i in range(count)]
    data, checks, at = [[] for _ in sizes], [[] for _ in sizes], 0
    for i in range(max(sizes)):
        for j, n in enumerate(sizes):
            if i < n:
                data[j].append(words[at])
                at += 1
    for _ in range(ecc):
        for j in range(count):
            checks[j].append(words[at])
            at += 1
    for j in range(count):
        for k in range(ecc):
            s = 0
            for c in data[j] + checks[j]:
                s = (0 if s == 0 else EXP[LOG[s] + k]) ^ c
            if s != 0:
                return False
    stream = "".join(format(b, "08b") for b in sum(data, []))
    width = 8 if version <= 9 else 16
    length = int(stream[4:4 + width], 2)
    payload = bytes(int(stream[4 + width + 8 * i:12 + width + 8 * i], 2) for i in range(length))
    return stream[:4] == "0100" and payload.decode("utf-8") == text


folder = Path(sys.argv[1])
# OpenCV has two QR readers; a code counts as read if either gets it exactly.
detectors = [cv2.QRCodeDetector()] + ([cv2.QRCodeDetectorAruco()] if hasattr(cv2, "QRCodeDetectorAruco") else [])


def opencv_read(path, text):
    image = cv2.imread(str(path), cv2.IMREAD_GRAYSCALE)
    return any(detector.detectAndDecode(image)[0] == text for detector in detectors)


urls = urls_read = sweep = sweep_cv = sweep_rs = 0
failed = []
for row in (folder / "manifest.tsv").read_text(encoding="utf-8").splitlines():
    name, version, level, mask, scale, text = row.split("\t", 5)
    read = opencv_read(folder / name, text)
    if text.startswith("https://capriai.app/remote/#"):
        urls += 1
        urls_read += read
        if not read:
            failed.append(f"{name}: a pairing URL (version {version} {level}) didn't read in OpenCV")
        continue
    sweep += 1
    if read:
        sweep_cv += 1
    elif syndromes_ok(folder / name, int(version), level, text, int(scale)):
        sweep_rs += 1
    else:
        failed.append(f"{name}: version {version} {level} mask {mask} is not a valid code")
for line in failed:
    print("FAIL " + line)
print(("PASS" if urls and urls == urls_read else "FAIL") + f" qr pairing URLs: {urls_read} of {urls} read back exactly in OpenCV {cv2.__version__}")
print(("PASS" if sweep == sweep_cv + sweep_rs else "FAIL") + f" qr sweep: {sweep_cv + sweep_rs} of {sweep} codes (versions 1 to 40, every level) are exact: {sweep_cv} read in OpenCV, {sweep_rs} it missed pass the format, Reed-Solomon and data check")
sys.exit(1 if failed else 0)
