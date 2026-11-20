import json
import math
import os
import random
import sys

import bpy
import mapbox_earcut as earcut
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
OUT = os.path.join(ROOT, 'site', 'remote', 'models', 'capri-face.glb')
PX = 0.01
CENTRE = (200.0, 200.0)

trace = json.load(open(os.path.join(HERE, 'face-trace.json')))
shapes = trace['shapes']
colour = dict(trace['colours'])
colour.update({'mouth': '#2a0016', 'tongue': '#ff3d7f', 'teeth': '#fff6f8', 'lid': '#01f76d', 'lash': '#120010', 'star': '#ffe9f2'})

RIGHT_EYE = (212.6, 198.4)
LEFT_EYE = (169.6, 208.0)
MOUTH = (192.0, 227.0)
MOUTH_TURN = -9.0


def point(x, y):
    return ((x - CENTRE[0]) * PX, (CENTRE[1] - y) * PX)


def material(name):
    key = 'c_' + colour[name].lstrip('#')
    found = bpy.data.materials.get(key)
    if found:
        return found
    made = bpy.data.materials.new(key)
    hexed = colour[name].lstrip('#')
    srgb = [int(hexed[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    linear = [c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in srgb]
    made.use_nodes = True
    shader = made.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = linear + [1.0]
    shader.inputs['Roughness'].default_value = 1.0
    return made


def triangulate(outer, holes):
    rings = [np.array(outer, np.float64)] + [np.array(h, np.float64) for h in holes]
    flat = np.concatenate(rings)
    ends = np.cumsum([len(r) for r in rings]).astype(np.uint32)
    tris = earcut.triangulate_float64(flat, ends).reshape(-1, 3)
    return flat, tris, rings


def prism(name, polygons, colour_name, depth, thick, parent=None, origin=None):
    verts, faces = [], []
    ox, oz = point(*origin) if origin else (0.0, 0.0)
    for outer, holes in polygons:
        if len(outer) < 3:
            continue
        flat, tris, rings = triangulate(outer, holes)
        base = len(verts)
        for x, y in flat:
            px, pz = point(x, y)
            verts.append((px - ox, depth, pz - oz))
        for a, b, c in tris:
            faces.append((base + int(a), base + int(c), base + int(b)))
        if thick > 0:
            back = len(verts)
            for x, y in flat:
                px, pz = point(x, y)
                verts.append((px - ox, depth + thick, pz - oz))
            start = 0
            for ring in rings:
                n = len(ring)
                for i in range(n):
                    j = (i + 1) % n
                    faces.append((base + start + i, base + start + j, back + start + j, back + start + i))
                start += n
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.materials.append(material(colour_name))
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    if parent:
        obj.parent = parent
    return obj


def circle(cx, cy, r, steps=40):
    return [(cx + r * math.cos(2 * math.pi * i / steps), cy + r * math.sin(2 * math.pi * i / steps)) for i in range(steps)]


def inside(px, py, ring):
    hit = False
    n = len(ring)
    for i in range(n):
        x1, y1 = ring[i]
        x2, y2 = ring[(i + 1) % n]
        if (y1 > py) != (y2 > py) and px < (x2 - x1) * (py - y1) / (y2 - y1) + x1:
            hit = not hit
    return hit


def extend(ring, seed):
    jag = random.Random(seed)
    out = []
    for x, y in ring:
        if y <= 39.5:
            y = -170 + jag.choice((0, 0, 40, 70, 95))
            x += jag.uniform(-14, 14)
        if y >= 316.5:
            y = 470
        if x <= 39.5:
            x = -90 + jag.choice((0, 30, 55))
        if x >= 360.5:
            x = 490 - jag.choice((0, 30, 55))
        out.append((x, y))
    return out


def stretched(found):
    return [(extend(s['outer'], i), s['holes']) for i, s in enumerate(found)]


def near(shape, spot, room):
    pts = np.array(shape['outer'])
    middle = pts.mean(0)
    return math.hypot(middle[0] - spot[0], middle[1] - spot[1]) < room


bpy.ops.wm.read_factory_settings(use_empty=True)
root = bpy.data.objects.new('capri', None)
bpy.context.collection.objects.link(root)

hair = [s for s in shapes['pink'] if s['area'] > 40 and np.array(s['outer'])[:, 1].mean() < 262]
torso = [s for s in shapes['pink'] if s['area'] > 40 and np.array(s['outer'])[:, 1].mean() >= 262]
prism('hair', stretched(hair), 'pink', 0.34, 0.08, root)
prism('rim', stretched([s for s in shapes['purple'] if s['area'] > 20]), 'purple', 0.35, 0.06, root)

random.seed(7)
big = max(hair, key=lambda s: s['area'])
stars = []
for s in shapes['white']:
    pts = np.array(s['outer'])
    cx, cy = pts.mean(0)
    if s['area'] < 40 and not near(s, RIGHT_EYE, 14) and not near(s, LEFT_EYE, 12):
        stars.append((cx, cy, 0.9 + min(1.6, s['area'] / 10)))
tries = 0
big_ring = extend(big['outer'], hair.index(big))
while len(stars) < 260 and tries < 12000:
    tries += 1
    x, y = random.uniform(-90, 490), random.uniform(-170, 262)
    if inside(x, y, big_ring) and not any(inside(x, y, h) for h in big['holes']):
        stars.append((x, y, random.choice([0.55, 0.7, 0.85, 1.1])))
diamonds = []
for x, y, r in stars:
    diamonds.append(([(x, y - r), (x + r * 0.8, y), (x, y + r), (x - r * 0.8, y)], []))
prism('stars', diamonds, 'star', 0.335, 0, root)

prism('body', stretched([s for s in shapes['black'] if s['area'] > 100 and not near(s, RIGHT_EYE, 16)]), 'black', 0.12, 0.1, root)
prism('torso', stretched(torso), 'pink', 0.1, 0.06, root)
prism('shoulder', [(s['outer'], []) for s in shapes['green'] if s['area'] < 1500], 'green', 0.09, 0.05, root)

head = bpy.data.objects.new('head', None)
bpy.context.collection.objects.link(head)
head.parent = root
prism('face', [(s['outer'], []) for s in shapes['green'] if s['area'] >= 1500], 'green', 0.0, 0.1, head)
socket = [s for s in shapes['black'] if near(s, RIGHT_EYE, 16) and s['area'] > 300]
socket = max(socket, key=lambda s: np.ptp(np.array(s['outer']), axis=0).prod())
for side, centre, rings, sock in (('r', RIGHT_EYE, ((10.2, 'green'), (8.8, 'black'), (7.6, 'blue'), (2.8, 'white')), socket),
                                  ('l', LEFT_EYE, ((8.4, 'black'), (7.1, 'green'), (6.2, 'black'), (5.3, 'blue'), (2.0, 'white')), None)):
    holder = bpy.data.objects.new('eye_' + side, None)
    bpy.context.collection.objects.link(holder)
    holder.parent = head
    gx, gz = point(*centre)
    holder.location = (gx, 0, gz)
    iris = bpy.data.objects.new('iris_' + side, None)
    bpy.context.collection.objects.link(iris)
    iris.parent = holder
    if sock:
        prism('socket_' + side, [(sock['outer'], [])], 'black', -0.004, 0, holder, centre)
    cx, cy = centre
    for step, (r, tone) in enumerate(rings):
        dx = -0.6 if tone == 'white' else 0
        dy = 0.4 if tone == 'white' else 0
        prism('ring_' + side + '_' + str(step), [(circle(cx + dx, cy + dy, r), [])], tone, -0.006 - step * 0.002, 0,
              iris if tone in ('blue', 'white') else holder, centre)


W, UP, LO = 27.0, -5.0, 5.5
TOP_TEETH, LOW_TEETH = 6, 5


def mouth_frame(u, v):
    t = math.radians(MOUTH_TURN)
    return (MOUTH[0] + u * math.cos(t) - v * math.sin(t), MOUTH[1] + u * math.sin(t) + v * math.cos(t))


def build_mouth():
    us = [-W + 2 * W * i / 12 for i in range(13)]
    upper = [(u, UP) for u in us]
    lower = [(u, LO) for u in reversed(us)]
    interior = [(-W - 1.5, 0.0)] + upper + [(W + 1.5, 0.0)] + lower
    teeth_top = []
    step = 2 * W / TOP_TEETH
    for i in range(TOP_TEETH):
        a = -W + i * step
        teeth_top.append([(a + 0.3, UP - 0.4), (a + step - 0.3, UP - 0.4), (a + step / 2, UP + 9.0)])
    teeth_low = []
    for i in range(LOW_TEETH):
        a = -W + step / 2 + i * step
        teeth_low.append([(a + 0.4, LO + 0.4), (a + step / 2, LO - 8.6), (a + step - 0.4, LO + 0.4)])
    tongue = [(-12 + 24 * i / 10, LO - 0.2) for i in range(11)] + [(12 - 24 * i / 10, LO) for i in range(11)]
    return interior, teeth_top, teeth_low, tongue


def keyed(name, rings, tone, depth, moves):
    obj = prism(name, [([mouth_frame(u, v) for u, v in ring], []) for ring in rings], tone, depth, 0, head)
    obj.shape_key_add(name='Basis')
    flat = [p for ring in rings for p in ring]
    for key_name, move in moves.items():
        key = obj.shape_key_add(name=key_name)
        for i, v in enumerate(key.data):
            u, w = flat[i]
            nu, nw = move(u, w, i)
            x, z = point(*mouth_frame(nu, nw))
            v.co.x, v.co.z = x, z
    return obj


def open_lip(u, v):
    edge = max(0.0, 1 - (u / (W + 1.5)) ** 2)
    return v + 15.0 * edge ** 0.8 if v > 0.5 else v - 2.0 * edge


def wide(u, v):
    return u * 1.22, v * 0.7


def round_(u, v):
    edge = max(0.0, 1 - (u / (W + 1.5)) ** 2)
    return u * 0.74, (v * 1.1 + (3.0 * edge if v > 0.5 else -0.5 * edge))


interior, teeth_top, teeth_low, tongue = build_mouth()
low_offset = lambda u: 15.0 * max(0.0, 1 - (u / (W + 1.5)) ** 2) ** 0.8
keyed('mouth', [interior], 'mouth', -0.008, {
    'open': lambda u, v, i: (u, open_lip(u, v)),
    'wide': lambda u, v, i: wide(u, v),
    'round': lambda u, v, i: round_(u, v),
})
keyed('teeth_top', teeth_top, 'teeth', -0.012, {
    'open': lambda u, v, i: (u, v - 2.0 * max(0.0, 1 - (u / (W + 1.5)) ** 2)),
    'wide': lambda u, v, i: wide(u, v),
    'round': lambda u, v, i: round_(u, v if v < 0 else v - 1.5),
})
low_base = [p for t in teeth_low for p in t]
keyed('teeth_low', teeth_low, 'teeth', -0.012, {
    'open': lambda u, v, i: (u, v + low_offset(low_base[i][0] if i < len(low_base) else u)),
    'wide': lambda u, v, i: wide(u, v),
    'round': lambda u, v, i: round_(u, v + 1.5),
})
keyed('tongue', [tongue], 'tongue', -0.010, {
    'open': lambda u, v, i: (u * 1.05, v + 15.0 - (5.5 * math.sqrt(max(0.0, 1 - (u / 12.5) ** 2)) if i < 11 else 0.0) - 0.5),
    'wide': lambda u, v, i: wide(u, v),
    'round': lambda u, v, i: round_(u, v),
})

for obj in bpy.data.objects:
    obj.select_set(True)
os.makedirs(os.path.dirname(OUT), exist_ok=True)
bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', export_morph=True, export_morph_normal=False, export_apply=False,
                          export_yup=True, export_normals=False, export_texcoords=False, export_materials='EXPORT')
print('wrote', OUT, os.path.getsize(OUT))
