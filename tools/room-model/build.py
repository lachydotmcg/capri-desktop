import bpy
import bmesh
import math
import os
import sys
import json
import shutil
import struct
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))

MODELS = {
    'capri': dict(sheet='assets/sprites/perch.png', across=60, rows=1, frame=0, round=0.075, pillow=0.35, step=2),
    'capri-lie': dict(sheet='site/remote/sprites/laydown.png', across=10, rows=6, frame=0, round=0.075, pillow=0.35, step=2),
}


def load_sheet(path):
    image = bpy.data.images.load(path)
    w, h = image.size
    px = np.empty(w * h * 4, dtype=np.float32)
    image.pixels.foreach_get(px)
    px = px.reshape(h, w, 4)[::-1]
    bpy.data.images.remove(image)
    return px


def box_blur(a, r):
    k = np.ones(2 * r + 1, dtype=np.float32) / (2 * r + 1)
    pad = np.pad(a, r, mode='edge')
    out = np.apply_along_axis(lambda v: np.convolve(v, k, mode='valid'), 0, pad)
    return np.apply_along_axis(lambda v: np.convolve(v, k, mode='valid'), 1, out)


def distance_inside(mask):
    ys, xs = np.nonzero(mask)
    edge = mask & ~(np.roll(mask, 1, 0) & np.roll(mask, -1, 0) & np.roll(mask, 1, 1) & np.roll(mask, -1, 1))
    edge[0, :] = edge[-1, :] = edge[:, 0] = edge[:, -1] = mask[0, :].any() or True
    edge &= mask
    outside = ~mask
    oy, ox = np.nonzero(outside)
    d = np.zeros(mask.shape, dtype=np.float32)
    pts = np.stack([ys, xs], 1).astype(np.float32)
    ref = np.stack([oy, ox], 1).astype(np.float32)
    near = np.empty(len(pts), dtype=np.float32)
    for i in range(0, len(pts), 2000):
        chunk = pts[i:i + 2000]
        dd = ((chunk[:, None, :] - ref[None, :, :]) ** 2).sum(2)
        near[i:i + 2000] = np.sqrt(dd.min(1))
    d[ys, xs] = near - 0.5
    return d


def bleed(rgb, solid, rounds):
    rgb = rgb.copy()
    have = solid.copy()
    for _ in range(rounds):
        acc = np.zeros_like(rgb)
        cnt = np.zeros(have.shape, dtype=np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1)):
            sh = np.roll(np.roll(have, dy, 0), dx, 1)
            sc = np.roll(np.roll(rgb, dy, 0), dx, 1)
            acc += sc * sh[..., None]
            cnt += sh
        fill = (~have) & (cnt > 0)
        rgb[fill] = acc[fill] / cnt[fill][..., None]
        have = have | fill
    return rgb


def detach_images(path, textures, names):
    data = open(path, 'rb').read()
    jlen, _ = struct.unpack('<II', data[12:20])
    doc = json.loads(data[20:20 + jlen])
    blen, _ = struct.unpack('<II', data[20 + jlen:28 + jlen])
    blob = data[28 + jlen:28 + jlen + blen]
    views = doc['bufferViews']
    dropped = set()
    for image in doc.get('images', []):
        dropped.add(image.pop('bufferView'))
        image.pop('mimeType', None)
        image['uri'] = image['name'] + '.png'
    parts = []
    remap = {}
    cursor = 0
    for i, view in enumerate(views):
        if i in dropped:
            continue
        chunk = blob[view.get('byteOffset', 0):view.get('byteOffset', 0) + view['byteLength']]
        pad = (-len(chunk)) % 4
        remap[i] = len(remap)
        view['byteOffset'] = cursor
        parts.append(chunk + b'\0' * pad)
        cursor += len(chunk) + pad
    doc['bufferViews'] = [v for i, v in enumerate(views) if i not in dropped]
    for accessor in doc['accessors']:
        if 'bufferView' in accessor:
            accessor['bufferView'] = remap[accessor['bufferView']]
    blob = b''.join(parts)
    doc['buffers'] = [{'byteLength': len(blob)}]
    text = json.dumps(doc, separators=(',', ':')).encode()
    text += b' ' * ((-len(text)) % 4)
    total = 12 + 8 + len(text) + 8 + len(blob)
    with open(path, 'wb') as out:
        out.write(struct.pack('<III', 0x46546C67, 2, total))
        out.write(struct.pack('<II', len(text), 0x4E4F534A) + text)
        out.write(struct.pack('<II', len(blob), 0x004E4942) + blob)
    for name in names:
        shutil.copyfile(os.path.join(textures, name + '.png'), os.path.join(os.path.dirname(path), name + '.png'))


def sample(field, x, y):
    h, w = field.shape
    x = np.clip(x, 0, w - 1.001)
    y = np.clip(y, 0, h - 1.001)
    x0 = x.astype(int)
    y0 = y.astype(int)
    fx = x - x0
    fy = y - y0
    return (field[y0, x0] * (1 - fx) * (1 - fy) + field[y0, x0 + 1] * fx * (1 - fy) +
            field[y0 + 1, x0] * (1 - fx) * fy + field[y0 + 1, x0 + 1] * fx * fy)


def build_texture(name, rgb, alpha, out_dir):
    image = bpy.data.images.new(name, rgb.shape[1], rgb.shape[0], alpha=True)
    out = np.concatenate([rgb, alpha[..., None]], 2)[::-1].astype(np.float32)
    image.pixels.foreach_set(out.ravel())
    image.filepath_raw = os.path.join(out_dir, name + '.png')
    image.file_format = 'PNG'
    image.save()
    image.pack()
    return image


def make_material(name, image):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    bsdf = nodes['Principled BSDF']
    tex = nodes.new('ShaderNodeTexImage')
    tex.image = image
    tex.interpolation = 'Linear'
    tex.extension = 'EXTEND'
    links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
    links.new(tex.outputs['Alpha'], bsdf.inputs['Alpha'])
    bsdf.inputs['Roughness'].default_value = 0.8
    bsdf.inputs['Metallic'].default_value = 0.0
    mat.blend_method = 'CLIP' if hasattr(mat, 'blend_method') else None
    return mat


def build(key, spec, out_dir, render=None):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sheet = load_sheet(os.path.join(ROOT, spec['sheet']))
    fw = sheet.shape[1] // spec['across']
    fh = sheet.shape[0] // spec['rows']
    col = spec['frame'] % spec['across']
    row = spec['frame'] // spec['across']
    frame = sheet[row * fh:(row + 1) * fh, col * fw:(col + 1) * fw].copy()
    alpha = frame[..., 3]
    mask = alpha > 0.5
    ys, xs = np.nonzero(mask)
    y0, y1, x0, x1 = ys.min(), ys.max(), xs.min(), xs.max()
    pad = 3
    y0, y1 = max(0, y0 - pad), min(fh - 1, y1 + pad)
    x0, x1 = max(0, x0 - pad), min(fw - 1, x1 + pad)
    frame = frame[y0:y1 + 1, x0:x1 + 1]
    alpha = frame[..., 3]
    mask = alpha > 0.5
    h, w = mask.shape

    rgb = frame[..., :3]
    solid = alpha > 0.85
    rgb = bleed(rgb, solid, 6)
    grown = mask.copy()
    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        grown |= np.roll(np.roll(mask, dy, 0), dx, 1)
    tex_alpha = np.maximum(alpha, grown.astype(np.float32))
    back_rgb = rgb * np.array([0.5, 0.42, 0.55], dtype=np.float32)
    front_img = build_texture(key + '_front', rgb, tex_alpha, out_dir)
    back_img = build_texture(key + '_back', back_rgb[:, ::-1], tex_alpha[:, ::-1], out_dir)

    dist = distance_inside(mask)
    dist = box_blur(dist, 1)
    dist[~mask] = 0
    rad = spec['round'] * h
    t = np.clip(dist / rad, 0, 1)
    edge = np.sqrt(np.clip(1 - (1 - t) ** 2, 0, 1))
    soft = np.clip(box_blur(dist, 6) / (0.25 * h), 0, 1)
    depth = (edge * (1 - spec['pillow']) + soft * spec['pillow']) * 0.5 * rad * 1.9
    depth[~mask] = 0

    step = spec['step']
    gx = np.arange(0, w + step, step, dtype=np.float32)
    gy = np.arange(0, h + step, step, dtype=np.float32)
    nx, ny = len(gx), len(gy)
    X, Y = np.meshgrid(gx, gy)
    Z = sample(depth, X, Y)
    inside = sample(dist, X, Y) > 0.0
    near = inside.copy()
    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1)):
        near |= np.roll(np.roll(inside, dy, 0), dx, 1)
    cells = near[:-1, :-1] | near[1:, :-1] | near[:-1, 1:] | near[1:, 1:]
    cy, cx = np.nonzero(cells)
    idx = np.arange(nx * ny).reshape(ny, nx)
    a, b, c, d = idx[cy, cx], idx[cy, cx + 1], idx[cy + 1, cx + 1], idx[cy + 1, cx]
    used = np.unique(np.concatenate([a, b, c, d]))
    remap = -np.ones(nx * ny, dtype=int)
    remap[used] = np.arange(len(used))
    a, b, c, d = remap[a], remap[b], remap[c], remap[d]
    px = X.ravel()[used]
    py = Y.ravel()[used]
    pz = Z.ravel()[used]
    scale = 1.0 / (y1 - y0 + 1)
    wx = (px - w / 2) * scale
    wy = (h - py) * scale
    wz = pz * scale
    u = np.clip(px / w, 0, 1)
    v = np.clip(1 - py / h, 0, 1)
    n = len(used)
    verts = np.concatenate([np.stack([wx, wz, wy], 1), np.stack([wx, -wz, wy], 1)])
    uv_front = np.stack([u, v], 1)
    uv_back = np.stack([1 - u, v], 1)
    tris_front = np.concatenate([np.stack([a, b, c], 1), np.stack([a, c, d], 1)])
    tris_back = np.concatenate([np.stack([a, c, b], 1), np.stack([a, d, c], 1)]) + n
    faces = np.concatenate([tris_front, tris_back])
    uvs = np.concatenate([uv_front, uv_back])

    mesh = bpy.data.meshes.new(key)
    mesh.from_pydata(verts.tolist(), [], faces.tolist())
    layer = mesh.uv_layers.new(name='UVMap')
    loop_uv = uvs[mesh.loops.foreach_get and np.array([l.vertex_index for l in mesh.loops])]
    layer.data.foreach_set('uv', loop_uv.ravel().astype(np.float32))
    mesh.materials.append(make_material(key + '_front', front_img))
    mesh.materials.append(make_material(key + '_back', back_img))
    nf = len(tris_front)
    mesh.polygons.foreach_set('material_index', np.concatenate([np.zeros(nf, int), np.ones(len(tris_back), int)]))
    mesh.update()
    obj = bpy.data.objects.new(key, mesh)
    bpy.context.scene.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6) if False else None
    bm.to_mesh(mesh)
    bm.free()
    for p in mesh.polygons:
        p.use_smooth = True
    mesh.shade_smooth() if hasattr(mesh, 'shade_smooth') else None

    out = os.path.join(ROOT, 'site', 'remote', 'models', key + '.glb')
    os.makedirs(os.path.dirname(out), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', use_selection=True, export_yup=True,
                              export_image_format='AUTO', export_apply=True, export_cameras=False, export_lights=False)
    detach_images(out, out_dir, [key + '_front', key + '_back'])
    print('wrote', out, os.path.getsize(out), 'bytes', n * 2, 'verts')
    if render:
        render_view(obj, render)


def render_view(obj, path):
    scene = bpy.context.scene
    world = bpy.data.worlds.new('w')
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes['Background'].inputs[0].default_value = (0.32, 0.34, 0.42, 1)
    world.node_tree.nodes['Background'].inputs[1].default_value = 1.0
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam'))
    scene.collection.objects.link(cam)
    scene.camera = cam
    bb = [obj.matrix_world @ v.co for v in obj.data.vertices]
    top = max(v.z for v in bb)
    cx = sum(v.x for v in bb) / len(bb)
    r = 2.4
    ang = math.radians(38)
    cam.location = (cx + r * math.sin(ang), -r * math.cos(ang) * 1.0, top * 0.5)
    direction = (cx, 0, top * 0.5)
    from mathutils import Vector
    look = Vector(direction) - Vector(cam.location)
    cam.rotation_euler = look.to_track_quat('-Z', 'Y').to_euler()
    cam.data.lens = 50
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN'))
    sun.data.energy = 4.0
    sun.rotation_euler = (math.radians(50), 0, math.radians(35))
    scene.collection.objects.link(sun)
    scene.view_settings.view_transform = 'Standard'
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 24
    scene.cycles.device = 'CPU'
    scene.render.resolution_x = 560
    scene.render.resolution_y = 760
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


if __name__ == '__main__':
    args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else sys.argv[1:]
    which = args[0] if args else 'all'
    render = args[1] if len(args) > 1 else None
    out_dir = os.path.join(HERE, 'textures')
    os.makedirs(out_dir, exist_ok=True)
    for key, spec in MODELS.items():
        if which in ('all', key):
            build(key, spec, out_dir, render if which == key else None)
