import * as THREE from './vendor/three/three.module.min.js';

const stripe = (url, w, h, cols, count, fps, px) => ({ url, w, h, cols, rows: Math.ceil(count / cols), count, fps, px });
const CLIPS = {
  perch: stripe('room/perch.webp', 196, 384, 20, 60, 30, 70),
  perch_talk: stripe('room/perch_talk.webp', 196, 384, 20, 60, 30, 70),
  perch_blink: stripe('room/perch_blink.png', 196, 384, 8, 8, 30, 70),
  perch_arrive: stripe('room/perch_arrive.png', 288, 384, 14, 14, 15, 70),
  laydown: stripe('laydown.png', 372, 250, 10, 60, 30, 186),
  laydown_talk: stripe('laydown_talk.png', 372, 250, 10, 60, 30, 186),
  laydown_blink: stripe('room/laydown_blink.png', 372, 250, 8, 8, 30, 186)
};
const BLINK = { perch: 28, laydown: 45 };
const HEIGHT = { camera: 0.8, xr: 0.5 };
const BODY = { sit: [0.5, 0.36], lie: [0.98, 0.34] };
const FLOAT = 0.05;

const softShadow = () => {
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = 128;
  const pen = canvas.getContext('2d');
  const fade = pen.createRadialGradient(64, 64, 0, 64, 64, 64);
  fade.addColorStop(0, 'rgba(0,0,0,0.75)');
  fade.addColorStop(0.5, 'rgba(0,0,0,0.38)');
  fade.addColorStop(1, 'rgba(0,0,0,0)');
  pen.fillStyle = fade;
  pen.fillRect(0, 0, 128, 128);
  const texture = new THREE.CanvasTexture(canvas);
  texture.colorSpace = THREE.SRGBColorSpace;
  return texture;
};

export const load = async base => {
  const loader = new THREE.TextureLoader();
  const sheets = {};
  await Promise.all(Object.entries(CLIPS).map(async ([name, clip]) => {
    const texture = await loader.loadAsync(base + clip.url);
    texture.colorSpace = THREE.SRGBColorSpace;
    texture.premultiplyAlpha = true;
    texture.generateMipmaps = false;
    texture.minFilter = texture.magFilter = THREE.LinearFilter;
    sheets[name] = texture;
  }));
  const shadow = softShadow();

  const stage = ({ canvas, xr = false }) => {
    const target = canvas || document.createElement('canvas');
    const renderer = new THREE.WebGLRenderer({ canvas: target, alpha: true, antialias: true, powerPreference: 'high-performance' });
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    renderer.setClearColor(0x000000, 0);
    if (xr) {
      renderer.xr.enabled = true;
      renderer.xr.setReferenceSpaceType('local');
    }
    const scene = new THREE.Scene();
    const camera = new THREE.PerspectiveCamera(60, 1, 0.05, 60);
    const textures = {};
    for (const name in sheets) {
      textures[name] = sheets[name].clone();
      textures[name].needsUpdate = true;
    }
    const material = new THREE.MeshBasicMaterial({
      map: textures.perch, transparent: true, premultipliedAlpha: true, depthWrite: false, alphaTest: 0.01, side: THREE.DoubleSide
    });
    const shade = new THREE.Mesh(
      new THREE.PlaneGeometry(1, 1),
      new THREE.MeshBasicMaterial({ map: shadow, transparent: true, depthWrite: false })
    );
    shade.rotation.order = 'YXZ';
    shade.renderOrder = -1;
    const figure = new THREE.Mesh(new THREE.PlaneGeometry(1, 1), material);
    const root = new THREE.Group();
    const body = new THREE.Group();
    body.add(figure);
    root.add(body);
    scene.add(shade, root);
    root.visible = shade.visible = false;

    const matrix = new THREE.Matrix4();
    let pose = '', since = 0, fresh = true, width = 0, height = 0, once = null, round = -1, blinking = false, shown = '', cell = -1;

    const pop = t => {
      const k = Math.min(1, t / 0.5) - 1;
      return 1 + 2.2 * k * k * k + 1.2 * k * k;
    };

    const frame = (name, index, scale) => {
      const clip = CLIPS[name], texture = textures[name];
      if (name !== shown) {
        shown = name;
        cell = -1;
        material.map = texture;
        figure.scale.set(clip.w * scale, clip.h * scale, 1);
        figure.position.set((clip.w / 2 - clip.px) * scale, clip.h / 2 * scale, 0);
      }
      if (index === cell) return;
      cell = index;
      const col = index % clip.cols, row = Math.floor(index / clip.cols);
      const nudgeX = 0.5 / (clip.w * clip.cols), nudgeY = 0.5 / (clip.h * clip.rows);
      texture.repeat.set(1 / clip.cols - 2 * nudgeX, 1 / clip.rows - 2 * nudgeY);
      texture.offset.set(col / clip.cols + nudgeX, 1 - (row + 1) / clip.rows + nudgeY);
    };

    const update = state => {
      const now = state.now;
      const there = state.position;
      root.visible = shade.visible = !!there;
      if (state.tint) material.color.setRGB(state.tint[0], state.tint[1], state.tint[2], THREE.SRGBColorSpace);
      else material.color.set(0xffffff);
      if (!there) { fresh = true; return; }
      const calm = !!state.calm;
      const want = state.pose === 'laydown' ? 'lie' : 'sit';
      if (want !== pose) {
        pose = want;
        since = now;
        once = null;
        shown = '';
      }
      if (fresh) {
        since = now;
        once = !calm && pose === 'sit' ? { name: 'perch_arrive', from: now } : null;
        shown = '';
      }
      fresh = false;
      const rest = pose === 'lie' ? 'laydown' : 'perch';
      const viewer = state.viewer || [0, 0, 0];
      const yaw = Math.atan2(viewer[0] - there[0], viewer[2] - there[2]);
      root.position.set(there[0], there[1], there[2]);
      root.rotation.set(0, yaw, 0);

      const seconds = now / 1000;
      const metres = HEIGHT[xr ? 'xr' : 'camera'] / 384;
      let name = rest, index = 0;
      if (!calm) {
        if (once) {
          const clip = CLIPS[once.name], step = Math.floor((now - once.from) * clip.fps / 1000);
          if (step >= clip.count) once = null;
          else { name = once.name; index = step; }
        }
        if (!once) {
          const step = Math.max(0, Math.floor((now - since) * 30 / 1000));
          index = step % 60;
          if (Math.floor(step / 60) !== round) {
            round = Math.floor(step / 60);
            blinking = Math.random() < 0.55;
          }
          if (state.talking) name = rest + '_talk';
          else if (blinking && index >= BLINK[rest] && index < BLINK[rest] + 8) {
            name = rest + '_blink';
            index -= BLINK[rest];
          }
        }
      }
      frame(name, index, metres);

      const size = pose === 'lie' ? pop((now - since) / 1000) : 1;
      const bob = calm ? 0 : (Math.sin(seconds * 1.7) * 0.5 + 0.5) * 0.04;
      const lift = (FLOAT + bob) * HEIGHT[xr ? 'xr' : 'camera'] / 0.8;
      body.position.y = lift * size;
      body.scale.set(size, size, 1);

      const high = bob / 0.04, extent = BODY[pose], scale = HEIGHT[xr ? 'xr' : 'camera'] * size;
      shade.position.set(there[0], there[1] + 0.004, there[2]);
      shade.rotation.set(-Math.PI / 2, yaw, 0);
      shade.scale.set(extent[0] * scale * (1 - 0.08 * high), extent[1] * scale * (1 - 0.08 * high), 1);
      shade.material.opacity = 0.62 - 0.12 * high;
      shade.visible = size > 0.01;
    };

    const view = (w, h, focal, rotation) => {
      if (w !== width || h !== height) {
        width = w;
        height = h;
        renderer.setSize(w, h, false);
      }
      camera.aspect = w / h;
      camera.fov = 2 * Math.atan(h / 2 / focal) * 180 / Math.PI;
      camera.updateProjectionMatrix();
      if (rotation) {
        matrix.set(rotation[0], rotation[1], rotation[2], 0, rotation[3], rotation[4], rotation[5], 0,
          rotation[6], rotation[7], rotation[8], 0, 0, 0, 0, 1);
        camera.quaternion.setFromRotationMatrix(matrix);
      } else camera.quaternion.identity();
      camera.position.set(0, 0, 0);
    };

    const draw = () => renderer.render(scene, camera);

    const attach = async session => {
      await renderer.xr.setSession(session);
    };

    const loop = callback => {
      renderer.setAnimationLoop((time, frame) => {
        if (!frame) return;
        callback(time, frame);
        renderer.render(scene, camera);
      });
    };

    const dispose = () => {
      renderer.setAnimationLoop(null);
      for (const name in textures) textures[name].dispose();
      figure.geometry.dispose();
      shade.geometry.dispose();
      material.dispose();
      shade.material.dispose();
      renderer.dispose();
      renderer.forceContextLoss();
    };

    return { update, view, draw, attach, loop, dispose, renderer };
  };

  return { stage };
};
