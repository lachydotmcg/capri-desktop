import * as THREE from './vendor/three/three.module.min.js';
import { GLTFLoader } from './vendor/three/GLTFLoader.js';

const SCALE = { camera: { sit: 0.8, lie: 0.8 }, xr: { sit: 0.5, lie: 0.52 } };
const SPAN = { sit: 0.62, lie: 1.05 };
const FLOAT = 0.12;
const wrap = angle => Math.atan2(Math.sin(angle), Math.cos(angle));

const rim = material => {
  material.onBeforeCompile = shader => {
    shader.fragmentShader = shader.fragmentShader.replace(
      '#include <emissivemap_fragment>',
      '#include <emissivemap_fragment>\n' +
      'float edgeGlow = pow(1.0 - abs(dot(normalize(vNormal), normalize(vViewPosition))), 3.0);\n' +
      'totalEmissiveRadiance += vec3(0.1, 0.85, 1.0) * edgeGlow * 0.5;'
    );
  };
};

const prepare = root => {
  root.traverse(node => {
    if (!node.isMesh) return;
    const material = node.material;
    material.alphaTest = 0.5;
    material.transparent = false;
    material.emissive = new THREE.Color(0xffffff);
    material.emissiveMap = material.map;
    material.emissiveIntensity = 0.7;
    material.roughness = 0.85;
    material.metalness = 0;
    rim(material);
  });
  return root;
};

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
  const loader = new GLTFLoader();
  const [sit, lie] = await Promise.all(['capri.glb', 'capri-lie.glb'].map(name => loader.loadAsync(base + name)));
  const models = { sit: prepare(sit.scene), lie: prepare(lie.scene) };
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
    const hemi = new THREE.HemisphereLight(0xffffff, 0x8a8a9a, 1.3);
    const sun = new THREE.DirectionalLight(0xffffff, 1.5);
    sun.position.set(0.6, 2, 1.2);
    const shade = new THREE.Mesh(
      new THREE.PlaneGeometry(1, 1),
      new THREE.MeshBasicMaterial({ map: shadow, transparent: true, depthWrite: false })
    );
    shade.rotation.x = -Math.PI / 2;
    shade.renderOrder = -1;
    const root = new THREE.Group();
    const body = new THREE.Group();
    const figures = { sit: models.sit.clone(true), lie: models.lie.clone(true) };
    body.add(figures.sit, figures.lie);
    root.add(body);
    scene.add(hemi, sun, shade, root);
    root.visible = shade.visible = false;

    const matrix = new THREE.Matrix4();
    let last = 0, yaw = 0, pose = '', since = 0, fresh = true, width = 0, height = 0;

    const pop = t => {
      const k = Math.min(1, t / 0.5) - 1;
      return 1 + 2.2 * k * k * k + 1.2 * k * k;
    };

    const update = state => {
      const now = state.now, dt = last ? Math.min(0.1, Math.max(0, (now - last) / 1000)) : 0;
      last = now;
      const there = state.position;
      root.visible = shade.visible = !!there;
      if (state.tint) {
        const [r, g, b] = state.tint;
        hemi.color.setRGB(r, g, b, THREE.SRGBColorSpace);
        sun.color.setRGB(r, g, b, THREE.SRGBColorSpace);
      } else {
        hemi.color.set(0xffffff);
        sun.color.set(0xffffff);
      }
      if (!there) { fresh = true; return; }
      const want = state.pose === 'laydown' ? 'lie' : 'sit';
      if (want !== pose) {
        pose = want;
        since = now;
        figures.sit.visible = pose === 'sit';
        figures.lie.visible = pose === 'lie';
      }
      if (fresh) since = now;
      root.position.set(there[0], there[1], there[2]);
      const viewer = state.viewer || [0, 0, 0];
      const face = Math.atan2(viewer[0] - there[0], viewer[2] - there[2]);
      const sway = state.calm ? 0 : Math.sin(now / 1000 * 0.6) * 0.7 + (state.turn || 0);
      if (fresh) yaw = face + sway;
      yaw += wrap(face + sway - yaw) * (1 - Math.exp(-dt * 0.6));
      fresh = false;
      root.rotation.y = yaw;
      const seconds = now / 1000, calm = state.calm ? 0 : 1;
      const lift = FLOAT + calm * (Math.sin(seconds * 1.7) * 0.5 + 0.5) * 0.06;
      const talk = state.talking && !state.calm ? Math.abs(Math.sin(seconds * 9)) * 0.025 : 0;
      const size = SCALE[xr ? 'xr' : 'camera'][pose] * pop((now - since) / 1000);
      body.position.y = lift * size / SCALE[xr ? 'xr' : 'camera'][pose] * (pose === 'lie' ? 1.2 : 1);
      body.scale.set(size, size * (1 + talk), size);
      body.rotation.z = calm * Math.sin(seconds * 0.9 + 1) * 0.03;
      const span = SPAN[pose] * size / SCALE[xr ? 'xr' : 'camera'][pose] * (xr ? 0.8 : 1.0);
      const high = (lift - FLOAT) / 0.06;
      shade.position.set(there[0], there[1] + 0.004, there[2]);
      shade.scale.set(span * (1 - 0.08 * high), span * 0.75 * (1 - 0.08 * high), 1);
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
      renderer.dispose();
      renderer.forceContextLoss();
    };

    return { update, view, draw, attach, loop, dispose, renderer };
  };

  return { stage };
};
