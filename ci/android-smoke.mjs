import { execFileSync } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';

const [apk, out = 'out/android'] = process.argv.slice(2);
const PKG = 'app.capriai.capri.debug';
const MAIN = PKG + '/app.capriai.capri.MainActivity';
mkdirSync(out, { recursive: true });

const adb = (...args) => execFileSync('adb', args, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
const shell = command => adb('shell', command);
const sleep = ms => new Promise(done => setTimeout(done, ms));
const results = [];
const check = (name, ok, detail = '') => {
  results.push({ name, ok: !!ok, detail: String(detail).slice(0, 400) });
  console.log((ok ? 'PASS ' : 'FAIL ') + name + (detail ? ' :: ' + String(detail).slice(0, 200) : ''));
};
const shot = name => {
  const png = execFileSync('adb', ['exec-out', 'screencap', '-p'], { maxBuffer: 64 * 1024 * 1024 });
  writeFileSync(`${out}/${name}.png`, png);
};
const waitFor = async (what, test, ms = 30000) => {
  const end = Date.now() + ms;
  let last;
  while (Date.now() < end) {
    try { last = await test(); if (last) return last; } catch (error) { last = error.message; }
    await sleep(500);
  }
  throw new Error('timed out waiting for ' + what + (last ? ': ' + last : ''));
};

const devtools = async () => {
  const pid = (await waitFor('the app process', () => shell(`pidof ${PKG}`).trim())).split(/\s+/)[0];
  const socket = await waitFor('the webview devtools socket', () => {
    const line = shell('cat /proc/net/unix').split('\n').find(row => row.includes('webview_devtools_remote_' + pid));
    return line && line.trim().split(/\s+/).pop().replace(/^@/, '');
  });
  adb('forward', 'tcp:9229', 'localabstract:' + socket);
  const page = await waitFor('the remote page', async () => {
    const list = await (await fetch('http://127.0.0.1:9229/json')).json();
    return list.find(item => item.type === 'page' && item.url.startsWith('https://capriai.app/remote/'));
  });
  const ws = new WebSocket(page.webSocketDebuggerUrl);
  await new Promise((done, fail) => { ws.onopen = done; ws.onerror = fail; });
  let id = 0;
  const waiting = new Map();
  ws.onmessage = event => {
    const message = JSON.parse(event.data);
    if (waiting.has(message.id)) { waiting.get(message.id)(message); waiting.delete(message.id); }
  };
  const run = expression => new Promise((done, fail) => {
    const ask = ++id;
    waiting.set(ask, message => {
      if (message.error) return fail(new Error(message.error.message));
      if (message.result.exceptionDetails) return fail(new Error(message.result.exceptionDetails.exception?.description || 'script error'));
      done(message.result.result.value);
    });
    ws.send(JSON.stringify({ id: ask, method: 'Runtime.evaluate', params: { expression, returnByValue: true, awaitPromise: true } }));
  });
  return { run, close: () => ws.close() };
};

const main = async () => {
  shell('settings put global window_animation_scale 0; settings put global transition_animation_scale 0; settings put global animator_duration_scale 0');
  adb('install', '-r', '-g', apk);
  check('installs', shell('pm list packages').includes('package:' + PKG));
  const info = shell(`dumpsys package ${PKG}`);
  check('declares the overlay permission', info.includes('android.permission.SYSTEM_ALERT_WINDOW'));
  adb('logcat', '-c');

  shell(`am start -W -n ${MAIN} -a android.intent.action.VIEW -d 'https://capriai.app/remote/?demo'`);
  const page = await devtools();
  const run = page.run;
  await waitFor('the page to load', () => run('document.readyState === "complete"'));
  const href = await run('location.href');
  check('opens her bundled remote page', href.startsWith('https://capriai.app/remote/'), href);
  check('page knows it is in the app', await run('document.documentElement.dataset.android === "app"'));
  const bridge = JSON.parse(await run('CapriAndroid.info()'));
  check('bridge reports the app version', bridge.version === '0.9.4', JSON.stringify(bridge));
  check('bridge has the notification methods', await run(`['notifyInfo', 'notifySet', 'notify'].every(name => typeof CapriAndroid[name] === 'function')`));
  const noteInfo = JSON.parse(await run('CapriAndroid.notifyInfo()'));
  check('notifyInfo reports on and allowed', typeof noteInfo.on === 'boolean' && typeof noteInfo.allowed === 'boolean', JSON.stringify(noteInfo));
  const noteOn = JSON.parse(await run('CapriAndroid.notifySet(true)'));
  const noteShown = await run(`CapriAndroid.notify(JSON.stringify({ id: 'smoke-1', kind: 'test', title: 'Capri', body: 'Smoke test', at: Date.now() }))`);
  check('notify returns a boolean when on', typeof noteShown === 'boolean' && (!noteOn.allowed || noteShown), JSON.stringify(noteOn) + ' ' + noteShown);
  await run('CapriAndroid.notifySet(false), true');
  check('adds the phone section to the menu', await run('!!document.getElementById("d-float")'));
  check('adds Room in AR to the camera menu', await run('!!document.getElementById("room-ar")'));
  const woke = JSON.parse(await run(`CapriAndroid.wake(JSON.stringify([{ mac: '0A:1B:2C:3D:4E:5F', broadcast: '10.0.2.255' }]))`));
  check('sends a wake packet on the local network', woke.sent > 0, JSON.stringify(woke));
  check('refuses a wake with no good MAC', JSON.parse(await run(`CapriAndroid.wake('[{"mac":"nope","broadcast":"10.0.2.255"}]')`)).sent === 0);
  check('fonts and sprites come from the app', await run(`fetch('/assets/sprites/idle.webp').then(r => r.ok && r.headers.get('content-type') === 'image/webp')`));
  check('her video call face comes from the app', await run(`fetch('/remote/models/capri-face.glb').then(r => r.ok && r.arrayBuffer()).then(b => !!b && b.byteLength > 20000)`));
  const drawn = await run(`import('/remote/face3d.js').then(async face => {
    const model = await face.load('/remote/models/capri-face.glb');
    const canvas = document.createElement('canvas');
    canvas.style.cssText = 'position:fixed;left:0;top:0;width:180px;height:360px';
    document.body.append(canvas);
    const stage = face.stage(canvas, model);
    stage.draw({ mouth: { open: 1 } });
    const copy = document.createElement('canvas');
    copy.width = canvas.width; copy.height = canvas.height;
    const pen = copy.getContext('2d');
    pen.drawImage(canvas, 0, 0);
    const pixels = pen.getImageData(0, 0, copy.width, copy.height).data;
    let solid = 0;
    for (let i = 3; i < pixels.length; i += 4) if (pixels[i] > 200) solid++;
    stage.dispose();
    canvas.remove();
    return solid / (pixels.length / 4);
  })`).catch(error => 'failed: ' + error.message);
  check('her video call face draws in the app', typeof drawn === 'number' && drawn > 0.5, String(drawn));
  const camera = await run(`navigator.mediaDevices.getUserMedia({ video: { facingMode: 'user' } }).then(stream => { const live = stream.getVideoTracks().some(track => track.readyState === 'live'); stream.getTracks().forEach(track => track.stop()); return live; }, error => error.name)`);
  check('the camera opens for a video call', camera === true, String(camera));
  check('a file outside the bundle is not served from it', await run(`fetch('/remote/../../secret.txt').then(r => r.status !== 200, () => true)`));

  await waitFor('the demo link', () => run('document.getElementById("app").dataset.link === "connected" && "connected"'), 20000).then(state => check('demo link starts', true, state)).catch(error => check('demo link starts', false, error.message));
  await sleep(1500);
  shot('1-chat');
  await run(`(() => {
    const box = document.getElementById('msg');
    box.value = 'how are you';
    box.dispatchEvent(new Event('input', { bubbles: true }));
    document.getElementById('send').click();
    return true;
  })()`);
  const reply = await waitFor('her reply', () => run(`[...document.querySelectorAll('#log-in .msg.capri.done .words')].map(n => n.textContent).filter(Boolean).pop() || ''`), 20000).catch(() => '');
  check('she answers in the demo', reply, reply);
  shot('2-reply');

  await run('document.getElementById("menu-open").click(), true');
  await sleep(800);
  shot('3-menu');
  check('back closes the menu', await run('CapriAndroidBack() && !document.getElementById("app").classList.contains("menu")'));

  shell(`appops set ${PKG} SYSTEM_ALERT_WINDOW allow`);
  const turned = JSON.parse(await run('(document.getElementById("d-float").click(), CapriAndroid.info())'));
  check('float switch turns on', turned.bubble === true && turned.overlay === true, JSON.stringify(turned));
  shell('input keyevent KEYCODE_HOME');
  const services = await waitFor('the bubble service', () => {
    const text = shell(`dumpsys activity services ${PKG}`);
    return text.includes('BubbleService') && text.includes('isForeground=true') ? text : '';
  }, 15000).catch(error => error.message);
  check('she floats when you leave the app', String(services).includes('isForeground=true'), String(services).split('\n').find(l => l.includes('BubbleService')) || services);
  const overlays = () => shell('dumpsys window windows').split('\n').filter(line => /Window\{.*app\.capriai\.capri/.test(line) || /ty=APPLICATION_OVERLAY/.test(line));
  await sleep(1500);
  shot('4-bubble');
  check('her bubble window is on screen', shell('dumpsys window windows').includes('APPLICATION_OVERLAY'), overlays().join(' | '));
  await run(`CapriAndroid.said('Still here. Your home screen is boring, by the way.'), true`);
  await sleep(1200);
  shot('5-bubble-talks');

  shell(`am start -W -n ${MAIN}`);
  const gone = await waitFor('the bubble to go away', () => !shell(`dumpsys activity services ${PKG}`).includes('BubbleService'), 10000).catch(() => false);
  check('bubble goes away when you open the app', gone);
  check('the page survived the round trip', await run('document.documentElement.dataset.android === "app"'));
  page.close();

  const log = adb('logcat', '-d');
  writeFileSync(`${out}/logcat.txt`, log);
  const crash = log.split('\n').filter(line => /FATAL EXCEPTION|AndroidRuntime: Process: app\.capriai/.test(line));
  check('no crashes', crash.length === 0, crash.join(' | '));
  const scriptErrors = log.split('\n').filter(line => /E CapriWeb|CapriWeb.*Uncaught/.test(line));
  check('no page script errors', scriptErrors.length === 0, scriptErrors.join(' | '));
};

main()
  .catch(error => check('smoke ran to the end', false, error.stack || error.message))
  .finally(() => {
    writeFileSync(`${out}/results.json`, JSON.stringify(results, null, 2));
    const failed = results.filter(item => !item.ok);
    console.log(`${results.length - failed.length}/${results.length} checks passed`);
    process.exit(failed.length ? 1 : 0);
  });
