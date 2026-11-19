import * as THREE from './vendor/three/three.module.min.js';

const KINDS = { 5120: Int8Array, 5121: Uint8Array, 5122: Int16Array, 5123: Uint16Array, 5125: Uint32Array, 5126: Float32Array };
const WIDTH = { SCALAR: 1, VEC2: 2, VEC3: 3, VEC4: 4 };

const readGlb = buffer => {
  const view = new DataView(buffer);
  if (view.getUint32(0, true) !== 0x46546c67) throw new Error('not a glb');
  let at = 12, json = null, bin = null;
  while (at < buffer.byteLength) {
    const length = view.getUint32(at, true), type = view.getUint32(at + 4, true);
    if (type === 0x4e4f534a) json = JSON.parse(new TextDecoder().decode(new Uint8Array(buffer, at + 8, length)));
    else if (type === 0x004e4942) bin = buffer.slice(at + 8, at + 8 + length);
    at += 8 + length;
  }
  if (!json || !bin) throw new Error('glb without its parts');
  const accessor = index => {
    const a = json.accessors[index], v = json.bufferViews[a.bufferView];
    const Kind = KINDS[a.componentType], width = WIDTH[a.type];
    return new Kind(bin, (v.byteOffset || 0) + (a.byteOffset || 0), a.count * width);
  };
  const colours = json.materials ? json.materials.map(m => (/^c_([0-9a-f]{6})/i.exec(m.name || '') || [0, 'ffffff'])[1]) : [];
  const materials = new Map();
  const material = hex => {
    if (!materials.has(hex)) {
      const made = new THREE.MeshBasicMaterial({ side: THREE.DoubleSide });
      made.color.setStyle('#' + hex, THREE.SRGBColorSpace);
      materials.set(hex, made);
    }
    return materials.get(hex);
  };
  const named = {};
  const build = index => {
    const node = json.nodes[index];
    let object;
    if (node.mesh != null) {
      const mesh = json.meshes[node.mesh];
      object = new THREE.Group();
      for (const primitive of mesh.primitives) {
        const geometry = new THREE.BufferGeometry();
        geometry.setAttribute('position', new THREE.BufferAttribute(new Float32Array(accessor(primitive.attributes.POSITION)), 3));
        if (primitive.indices != null) geometry.setIndex(new THREE.BufferAttribute(new Uint32Array(accessor(primitive.indices)), 1));
        const keys = primitive.targets || [];
        if (keys.length) {
          geometry.morphAttributes.position = keys.map(target => new THREE.BufferAttribute(new Float32Array(accessor(target.POSITION)), 3));
          geometry.morphTargetsRelative = true;
        }
        const part = new THREE.Mesh(geometry, material(colours[primitive.material] || 'ffffff'));
        if (keys.length) {
          part.morphTargetInfluences = keys.map(() => 0);
          const names = (mesh.extras && mesh.extras.targetNames) || [];
          part.morphTargetDictionary = {};
          names.forEach((name, i) => { part.morphTargetDictionary[name] = i; });
        }
        object.add(part);
      }
    } else object = new THREE.Group();
    object.name = node.name || '';
    if (node.translation) object.position.fromArray(node.translation);
    if (node.rotation) object.quaternion.fromArray(node.rotation);
    if (node.scale) object.scale.fromArray(node.scale);
    if (object.name) named[object.name] = object;
    for (const child of node.children || []) object.add(build(child));
    return object;
  };
  const scene = new THREE.Group();
  for (const index of json.scenes[json.scene || 0].nodes) scene.add(build(index));
  return { scene, named };
};

export const load = async url => {
  const response = await fetch(url);
  if (!response.ok) throw new Error('model ' + response.status);
  return readGlb(await response.arrayBuffer());
};

const keyed = (named, name) => {
  const holder = named[name];
  return holder ? holder.children.filter(part => part.morphTargetDictionary) : [];
};

export const stage = (canvas, model) => {
  const renderer = new THREE.WebGLRenderer({ canvas, alpha: true, antialias: true, powerPreference: 'low-power' });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2.5));
  renderer.setClearColor(0x000000, 0);
  const scene = new THREE.Scene();
  const camera = new THREE.PerspectiveCamera(24, 1, 0.5, 60);
  const { named } = model;
  const root = named.capri || model.scene;
  const head = named.head || root;
  const eyes = ['r', 'l'].map(side => ({ eye: named['eye_' + side], iris: named['iris_' + side] })).filter(one => one.eye);
  scene.add(model.scene);
  const mouths = ['mouth', 'teeth_top', 'teeth_low', 'tongue'].flatMap(name => keyed(named, name));
  const FACE = new THREE.Vector3(-0.06, -0.05, 0);
  let width = 0, height = 0;

  const fit = () => {
    const w = canvas.clientWidth, h = canvas.clientHeight;
    if (!w || !h || (w === width && h === height)) return;
    width = w;
    height = h;
    renderer.setSize(w, h, false);
    const aspect = w / h;
    camera.aspect = aspect;
    const across = aspect < 1 ? Math.max(1.45, Math.min(2.4, 1.45 + (aspect - 0.46) * 1.8)) : Math.min(3.2, 2.3 * aspect);
    const tall = across / aspect;
    const distance = tall / 2 / Math.tan(THREE.MathUtils.degToRad(camera.fov / 2));
    const lift = aspect < 1 ? tall * 0.1 : tall * 0.04;
    camera.position.set(FACE.x, FACE.y - lift, distance);
    camera.lookAt(FACE.x, FACE.y - lift, 0);
    camera.updateProjectionMatrix();
  };

  const set = (parts, name, value) => {
    for (const part of parts) {
      const at = part.morphTargetDictionary[name];
      if (at != null) part.morphTargetInfluences[at] = value;
    }
  };

  const draw = state => {
    fit();
    if (!width) return;
    const mouth = state.mouth || {};
    set(mouths, 'open', mouth.open || 0);
    set(mouths, 'wide', mouth.wide || 0);
    set(mouths, 'round', mouth.round || 0);
    const look = state.look || [0, 0], shut = Math.max(0, Math.min(1, state.blink || 0));
    for (const { eye, iris } of eyes) {
      eye.scale.y = 1 - 0.9 * shut;
      if (iris) iris.position.set(look[0] * 0.018, look[1] * 0.014, 0);
    }
    const turn = state.turn || [0, 0];
    root.rotation.set(turn[1], turn[0], state.roll || 0);
    root.position.set(0, state.bob || 0, 0);
    head.rotation.z = state.nod || 0;
    renderer.render(scene, camera);
  };

  return {
    draw,
    resize: () => { width = height = 0; },
    dispose: () => renderer.dispose()
  };
};
