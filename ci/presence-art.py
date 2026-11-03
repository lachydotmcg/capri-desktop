# Builds the moving pictures on her Discord status from her artist's frames in assets/sprites, with glitch layered on top.
# Discord only animates pictures at a public link, so these go in site/assets/presence and go live with the site.
import pathlib
import random

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

ROOT = pathlib.Path(__file__).resolve().parent.parent
SPRITES = ROOT / 'assets' / 'sprites'
OUT = ROOT / 'site' / 'assets' / 'presence'
SIZE = 256
PINK = (255, 45, 120)
CYAN = (60, 235, 235)
BACK = (12, 10, 20)


def cells(name, count, step=1):
    strip = Image.open(SPRITES / (name + '.png')).convert('RGBA')
    width = strip.width // count
    return [strip.crop((i * width, 0, (i + 1) * width, strip.height)) for i in range(0, count, step)]


def fit(frames, room=0.92):
    box = None
    for frame in frames:
        edges = frame.getbbox()
        if edges is None:
            continue
        box = edges if box is None else (min(box[0], edges[0]), min(box[1], edges[1]), max(box[2], edges[2]), max(box[3], edges[3]))
    wide, tall = box[2] - box[0], box[3] - box[1]
    scale = min(SIZE * room / wide, SIZE * room / tall)
    size = (max(1, round(wide * scale)), max(1, round(tall * scale)))
    fitted = []
    for frame in frames:
        layer = Image.new('RGBA', (SIZE, SIZE), (0, 0, 0, 0))
        layer.alpha_composite(frame.crop(box).resize(size, Image.LANCZOS), ((SIZE - size[0]) // 2, (SIZE - size[1]) // 2))
        fitted.append(layer)
    return fitted


def panel():
    image = Image.new('RGBA', (SIZE, SIZE), BACK + (255,))
    glow = Image.new('RGBA', (SIZE, SIZE), (0, 0, 0, 0))
    ImageDraw.Draw(glow).ellipse((28, 28, SIZE - 28, SIZE - 28), fill=PINK + (60,))
    image.alpha_composite(glow.filter(ImageFilter.GaussianBlur(46)))
    lines = Image.new('RGBA', (SIZE, SIZE), (0, 0, 0, 0))
    drawing = ImageDraw.Draw(lines)
    for y in range(0, SIZE, 4):
        drawing.line((0, y, SIZE, y), fill=(0, 0, 0, 46))
    image.alpha_composite(lines)
    return image


# Pink and cyan ghosts either side, and bands of the picture knocked sideways.
def tear(layer, rng, push, bands):
    alpha = layer.getchannel('A')
    ghosts = Image.new('RGBA', layer.size, (0, 0, 0, 0))
    for colour, way in ((PINK, -push), (CYAN, push)):
        tint = Image.new('RGBA', layer.size, colour + (0,))
        tint.putalpha(ImageChops.offset(alpha, way, 0).point(lambda value: value * 7 // 10))
        ghosts.alpha_composite(tint)
    ghosts.alpha_composite(layer)
    for _ in range(bands):
        top = rng.randrange(0, layer.height - 12)
        band = (0, top, layer.width, top + rng.randrange(4, 22))
        piece = ImageChops.offset(ghosts.crop(band), rng.choice((-1, 1)) * rng.randrange(6, 26), 0)
        ghosts.paste(piece, band)
    return ghosts


def controller(scale=4):
    wide, tall = 320 * scale, 214 * scale
    body = Image.new('L', (wide, tall), 0)
    drawing = ImageDraw.Draw(body)

    def at(*values):
        return tuple(value * scale for value in values)

    drawing.rounded_rectangle(at(56, 30, 264, 134), radius=48 * scale, fill=255)
    for near, far in (((96, 96), (56, 170)), ((224, 96), (264, 170))):
        drawing.line(at(*near, *far), fill=255, width=78 * scale)
        drawing.ellipse(at(far[0] - 39, far[1] - 39, far[0] + 39, far[1] + 39), fill=255)
    edge = ImageChops.subtract(body, body.filter(ImageFilter.MinFilter(9 * scale // 2 * 2 + 1)))
    art = Image.new('RGBA', (wide, tall), (0, 0, 0, 0))
    fill = Image.new('RGBA', (wide, tall), (22, 19, 34, 255))
    art.paste(fill, (0, 0), body)
    rim = Image.new('RGBA', (wide, tall), PINK + (255,))
    art.paste(rim, (0, 0), edge)
    marks = ImageDraw.Draw(art)
    marks.rounded_rectangle(at(84, 72, 140, 92), radius=5 * scale, fill=CYAN + (255,))
    marks.rounded_rectangle(at(102, 54, 122, 110), radius=5 * scale, fill=CYAN + (255,))
    for x, y, colour in ((222, 54, PINK), (250, 82, CYAN), (194, 82, CYAN), (222, 110, PINK)):
        marks.ellipse(at(x - 12, y - 12, x + 12, y + 12), fill=colour + (255,))
    for x in (128, 192):
        marks.ellipse(at(x - 20, 124, x + 20, 164), fill=(44, 40, 66, 255), outline=PINK + (255,), width=3 * scale)
    marks.rounded_rectangle(at(146, 62, 174, 72), radius=4 * scale, fill=(244, 244, 248, 255))
    return art.resize((320, 214), Image.LANCZOS)


def cursor():
    return Image.open(ROOT / 'assets' / 'cursor.png').convert('RGBA')


def live_tag(layer, on):
    drawing = ImageDraw.Draw(layer)
    drawing.rounded_rectangle((12, 12, 100, 44), radius=7, fill=(232, 28, 60, 255))
    drawing.ellipse((21, 22, 33, 34), fill=(255, 255, 255, 255 if on else 90))
    drawing.text((39, 17), 'LIVE', font=ImageFont.truetype('arialbd.ttf', 20), fill=(255, 255, 255, 255))
    return layer


def save(name, frames, wait):
    OUT.mkdir(parents=True, exist_ok=True)
    flat = [frame.convert('RGB') for frame in frames]
    sheet = Image.new('RGB', (flat[0].width * min(len(flat), 8), flat[0].height))
    for i, frame in enumerate(flat[:8]):
        sheet.paste(frame, (i * flat[0].width, 0))
    palette = sheet.quantize(colors=200, method=Image.Quantize.MEDIANCUT)
    picked = [frame.quantize(palette=palette, dither=Image.Dither.NONE) for frame in flat]
    picked[0].save(OUT / (name + '.gif'), save_all=True, append_images=picked[1:], duration=wait, loop=0, optimize=True, disposal=1)
    print(name, len(picked), 'frames', (OUT / (name + '.gif')).stat().st_size // 1024, 'KB')


def card(name, sprite, count, step, wait, seed, every=8, push=4, bands=3, room=0.92, tagged=False):
    rng = random.Random(seed)
    back = panel()
    frames = []
    bodies = fit(cells(sprite, count, step), room)
    for i, body in enumerate(bodies):
        image = back.copy()
        image.alpha_composite(tear(body, rng, push, bands) if i % every == 0 or i % every == 1 else body)
        if tagged:
            live_tag(image, i % 4 < 2)
        frames.append(image)
    save(name, frames, wait)


def crowd(name, wait, seed):
    rng = random.Random(seed)
    back = panel()
    bodies = fit(cells('idle', 16), 0.62)
    spots = [(-68, 46), (70, 46), (-40, -44), (44, -44), (0, 26)]
    frames = []
    for i in range(16):
        image = back.copy()
        for at, (x, y) in enumerate(spots):
            body = bodies[(i + at * 5) % 16]
            shifted = ImageChops.offset(body, x, y)
            image.alpha_composite(tear(shifted, rng, 5, 2) if (i + at) % 5 == 0 else shifted)
        frames.append(image)
    save(name, frames, wait)


def badge(name, picture, width, seed):
    rng = random.Random(seed)
    size = (width, picture.height * width // picture.width)
    small = picture.resize(size, Image.LANCZOS)
    frames = []
    for frame in range(12):
        layer = Image.new('RGBA', (SIZE, SIZE), BACK + (255,))
        art = Image.new('RGBA', (SIZE, SIZE), (0, 0, 0, 0))
        art.alpha_composite(small, ((SIZE - size[0]) // 2, (SIZE - size[1]) // 2))
        layer.alpha_composite(tear(art, rng, rng.randrange(3, 9), 3))
        frames.append(layer)
    save(name, frames, 110)


pad = controller()
arrow = cursor()
card('plugged', 'idle', 16, 1, 120, 1)
card('chatting', 'perch_talk', 60, 4, 130, 2)
card('dancing', 'perch_music', 60, 4, 130, 3)
card('asleep', 'laydown_sleep', 60, 4, 170, 4, every=15)
card('voice', 'laydown_talk', 60, 4, 130, 5)
card('break', 'dangle', 24, 2, 130, 6)
card('computer', 'perch', 60, 4, 130, 7, every=5)
card('game', 'perch', 60, 4, 130, 8, every=6)
card('live', 'perch_talk', 60, 4, 130, 9, every=6, tagged=True)
crowd('clones', 120, 10)
badge('controller', pad, 220, 11)
badge('cursor', arrow, 176, 12)
