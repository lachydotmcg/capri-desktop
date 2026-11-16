import { readFileSync, writeFileSync } from 'node:fs';
import { inflateSync, inflateRawSync, deflateSync } from 'node:zlib';
import { createHash } from 'node:crypto';

export function chunks(bytes) {
  const out = [];
  for (let at = 8; at < bytes.length; at += 12 + bytes.readUInt32BE(at)) out.push({ type: bytes.toString('ascii', at + 4, at + 8), data: bytes.subarray(at + 8, at + 8 + bytes.readUInt32BE(at)) });
  return out;
}
export function png(bytes) {
  const parts = chunks(bytes), head = parts[0].data, width = head.readUInt32BE(0), height = head.readUInt32BE(4);
  if (head[8] !== 8 || head[12] !== 0 || ![3, 6].includes(head[9])) throw new Error('Unsupported source PNG');
  const step = head[9] === 3 ? 1 : 4, stride = width * step;
  const raw = inflateSync(Buffer.concat(parts.filter(p => p.type === 'IDAT').map(p => p.data)));
  const scan = Buffer.alloc(stride * height), palette = parts.find(p => p.type === 'PLTE')?.data, alpha = parts.find(p => p.type === 'tRNS')?.data;
  for (let y = 0; y < height; y++) {
    const kind = raw[y * (stride + 1)], from = y * (stride + 1) + 1, to = y * stride;
    if (kind === 0) { raw.copy(scan, to, from, from + stride); continue; }
    for (let x = 0; x < stride; x++) {
      const left = x >= step ? scan[to + x - step] : 0, up = y ? scan[to - stride + x] : 0, corner = y && x >= step ? scan[to - stride + x - step] : 0;
      let add;
      if (kind === 1) add = left;
      else if (kind === 2) add = up;
      else if (kind === 3) add = (left + up) >> 1;
      else { const p = left + up - corner, a = Math.abs(p - left), b = Math.abs(p - up), c = Math.abs(p - corner); add = a <= b && a <= c ? left : b <= c ? up : corner; }
      scan[to + x] = raw[from + x] + add;
    }
  }
  if (step === 4) return { width, height, pixels: scan };
  const pixels = Buffer.alloc(width * height * 4);
  for (let i = 0; i < scan.length; i++) {
    const color = scan[i] * 3, at = i * 4;
    pixels[at] = palette[color]; pixels[at + 1] = palette[color + 1]; pixels[at + 2] = palette[color + 2];
    pixels[i * 4 + 3] = alpha && scan[i] < alpha.length ? alpha[scan[i]] : 255;
  }
  return { width, height, pixels };
}
export function heartSource() {
  const bytes = readFileSync('assets/emotes/heart.zip'), entries = new Map();
  let at = bytes.length - 22;
  while (bytes.readUInt32LE(at) !== 0x06054b50) at--;
  let pos = bytes.readUInt32LE(at + 16);
  while (bytes.readUInt32LE(pos) === 0x02014b50) {
    const n = bytes.readUInt16LE(pos + 28), local = bytes.readUInt32LE(pos + 42), start = local + 30 + bytes.readUInt16LE(local + 26) + bytes.readUInt16LE(local + 28);
    const data = bytes.subarray(start, start + bytes.readUInt32LE(pos + 20));
    entries.set(bytes.toString('utf8', pos + 46, pos + 46 + n), bytes.readUInt16LE(pos + 10) === 8 ? inflateRawSync(data) : data);
    pos += 46 + n + bytes.readUInt16LE(pos + 30) + bytes.readUInt16LE(pos + 32);
  }
  const frames = entries.get('frames.txt').toString().split(/\r?\n/).filter(line => /^\d+ /.test(line)).map(line => {
    const [n, file, x, y] = line.split(' ');
    return file === '-' ? { x: 0, y: 0, width: 1, height: 1, pixels: Buffer.alloc(4) } : { ...png(entries.get(file)), x: +x, y: +y };
  });
  return frames;
}
function chunk(type, data) {
  const name = Buffer.from(type), out = Buffer.alloc(data.length + 12);
  out.writeUInt32BE(data.length); name.copy(out, 4); data.copy(out, 8);
  let crc = 0xffffffff;
  for (const byte of Buffer.concat([name, data])) { crc ^= byte; for (let i = 0; i < 8; i++) crc = (crc >>> 1) ^ ((crc & 1) ? 0xedb88320 : 0); }
  out.writeUInt32BE((crc ^ 0xffffffff) >>> 0, out.length - 4);
  return out;
}
export function makeHeart() {
  const frames = heartSource(), head = Buffer.alloc(13), act = Buffer.alloc(8);
  head.writeUInt32BE(1920); head.writeUInt32BE(1080, 4); head[8] = 8; head[9] = 6;
  act.writeUInt32BE(frames.length);
  const out = [Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', head), chunk('acTL', act)];
  let seq = 0;
  frames.forEach((frame, index) => {
    if (!index) frame = { x: 0, y: 0, width: 1920, height: 1080, pixels: Buffer.alloc(1920 * 1080 * 4) };
    const fc = Buffer.alloc(26);
    [seq++, frame.width, frame.height, frame.x, frame.y].forEach((v, i) => fc.writeUInt32BE(v, i * 4));
    fc.writeUInt16BE(1, 20); fc.writeUInt16BE(30, 22); fc[24] = 1;
    out.push(chunk('fcTL', fc));
    const raw = Buffer.alloc((frame.width * 4 + 1) * frame.height);
    for (let y = 0; y < frame.height; y++) frame.pixels.copy(raw, y * (frame.width * 4 + 1) + 1, y * frame.width * 4, (y + 1) * frame.width * 4);
    const data = deflateSync(raw, { level: 9 });
    if (!index) out.push(chunk('IDAT', data));
    else { const n = Buffer.alloc(4); n.writeUInt32BE(seq++); out.push(chunk('fdAT', Buffer.concat([n, data]))); }
  });
  out.push(chunk('IEND', Buffer.alloc(0)));
  writeFileSync('site/remote/sprites/heart.png', Buffer.concat(out));
}
export async function verifyArt() {
  const results = [];
  for (const name of ['laydown', 'laydown_talk']) {
    const source = png(readFileSync('assets/sprites/' + name + '.png')), packed = Buffer.alloc(source.pixels.length);
    for (let i = 0; i < 60; i++) for (let y = 0; y < 250; y++) source.pixels.copy(packed, ((Math.floor(i / 10) * 250 + y) * 3720 + i % 10 * 372) * 4, (y * 22320 + i * 372) * 4, (y * 22320 + (i + 1) * 372) * 4);
    const actual = png(readFileSync('site/remote/sprites/' + name + '.png'));
    results.push({ name, equal: actual.width === 3720 && actual.height === 1500 && actual.pixels.equals(packed), frames: 60, sha256: createHash('sha256').update(actual.pixels).digest('hex') });
  }
  const source = heartSource(), parts = chunks(readFileSync('site/remote/sprites/heart.png'));
  let frame = -1, control, data = [], equal = true;
  const finish = () => {
    if (frame < 0) return;
    const head = Buffer.from(parts[0].data);
    head.writeUInt32BE(control.readUInt32BE(4)); head.writeUInt32BE(control.readUInt32BE(8), 4);
    const decoded = png(Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]), chunk('IHDR', head), chunk('IDAT', Buffer.concat(data)), chunk('IEND', Buffer.alloc(0))]));
    if (!frame) equal = equal && decoded.pixels.every(b => b === 0);
    else equal = equal && decoded.width === source[frame].width && decoded.height === source[frame].height && control.readUInt32BE(12) === source[frame].x && control.readUInt32BE(16) === source[frame].y && decoded.pixels.equals(source[frame].pixels);
    equal = equal && control.readUInt16BE(20) === 1 && control.readUInt16BE(22) === 30 && control[24] === 1 && control[25] === 0;
  };
  for (const part of parts) {
    if (part.type === 'fcTL') { finish(); frame++; control = part.data; data = []; }
    if (part.type === 'IDAT') data.push(part.data);
    if (part.type === 'fdAT') data.push(part.data.subarray(4));
  }
  finish();
  results.push({name:'heart',equal:equal && frame + 1 === source.length,frames:frame + 1,width:1920,height:1080});
  return results;
}
