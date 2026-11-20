import json
import os
import sys

import cv2
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
SCALE = 4
PALETTE = {
    'pink': (253, 2, 80),
    'black': (34, 1, 26),
    'green': (1, 250, 110),
    'blue': (30, 60, 220),
    'white': (255, 255, 255),
    'purple': (120, 40, 200),
}


def trace(path):
    art = np.array(Image.open(path).convert('RGBA')).astype(np.float32)
    h, w = art.shape[:2]
    big = cv2.resize(art, (w * SCALE, h * SCALE), interpolation=cv2.INTER_CUBIC)
    rgb, alpha = big[..., :3], big[..., 3]
    colours = np.array(list(PALETTE.values()), np.float32)
    nearest = ((rgb[:, :, None, :] - colours[None, None]) ** 2).sum(-1).argmin(-1)
    solid = alpha > 110
    found = {}
    for index, name in enumerate(PALETTE):
        mask = ((nearest == index) & solid).astype(np.uint8) * 255
        mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, np.ones((3, 3), np.uint8))
        contours, tree = cv2.findContours(mask, cv2.RETR_CCOMP, cv2.CHAIN_APPROX_NONE)
        shapes = []
        for at, contour in enumerate(contours):
            if tree[0][at][3] >= 0:
                continue
            area = cv2.contourArea(contour) / SCALE / SCALE
            if area < 4:
                continue
            outer = cv2.approxPolyDP(contour, 2.0, True)[:, 0, :] / SCALE
            holes = []
            child = tree[0][at][2]
            while child >= 0:
                if cv2.contourArea(contours[child]) / SCALE / SCALE >= 4:
                    holes.append((cv2.approxPolyDP(contours[child], 2.0, True)[:, 0, :] / SCALE).round(2).tolist())
                child = tree[0][child][0]
            shapes.append({'area': round(area, 1), 'outer': outer.round(2).tolist(), 'holes': holes})
        found[name] = shapes
    return {'size': [w, h], 'colours': {k: '#%02x%02x%02x' % v for k, v in PALETTE.items()}, 'shapes': found}


if __name__ == '__main__':
    source = os.path.join(ROOT, 'assets', 'sprites', 'face.png')
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, 'face-trace.json')
    json.dump(trace(source), open(out, 'w'))
    print('traced', out)
