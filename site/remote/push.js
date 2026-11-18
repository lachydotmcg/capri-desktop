/* Capri Remote's phone notifications (PROTOCOL.md 8.7): when the PC offers push, a switch in the menu lets this phone hear
   about her answers, jobs that need a yes, finished jobs and reminders while the page isn't open. In a browser that's a
   web push through sw.js; in Capri's Android app it's the app's own notification, fed by notify messages. */
(() => {
  'use strict';
  const SAVED = 'capri-remote-push';
  const remote = window.CapriRemote;
  if (!remote) return;
  const byId = id => document.getElementById(id);
  const idOk = id => typeof id === 'string' && /^[A-Za-z0-9_-]{1,64}$/.test(id);
  const make = (tag, className, text) => {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text != null) node.textContent = text;
    return node;
  };
  const read = raw => { try { return JSON.parse(raw) || {}; } catch (error) { return {}; } };
  const newId = () => [...crypto.getRandomValues(new Uint8Array(8))].map(byte => byte.toString(16).padStart(2, '0')).join('');
  const fromB64u = text => {
    const raw = atob(String(text).replace(/-/g, '+').replace(/_/g, '/'));
    return Uint8Array.from(raw, char => char.charCodeAt(0));
  };
  const toB64u = buffer => btoa(String.fromCharCode(...new Uint8Array(buffer))).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');

  const wished = () => { try { return localStorage.getItem(SAVED) === 'on'; } catch (error) { return false; } };
  const wish = on => {
    try {
      if (on) localStorage.setItem(SAVED, 'on');
      else localStorage.removeItem(SAVED);
    } catch (error) {}
  };

  const bridge = () => {
    const app = window.CapriAndroid;
    return app && typeof app.notifyInfo === 'function' ? app : null;
  };
  const apple = /iPhone|iPad/.test(navigator.userAgent) || (/Macintosh/.test(navigator.userAgent) && navigator.maxTouchPoints > 1);
  const standalone = () => {
    try { return navigator.standalone === true || matchMedia('(display-mode: standalone)').matches; } catch (error) { return false; }
  };
  const webOk = () => !!(navigator.serviceWorker && window.PushManager && window.Notification && window.isSecureContext);
  const blocked = () => {
    if (bridge()) return '';
    if (apple && !standalone()) return 'On iPhone and iPad, add Capri to your Home Screen first (iOS 16.4 or newer), then open it from there to turn this on.';
    if (!webOk()) return 'This browser can\'t show notifications from Capri.';
    return '';
  };

  const part = make('section', 'drawer-part');
  part.id = 'd-push';
  part.hidden = true;
  part.setAttribute('aria-labelledby', 'd-push-head');
  const head = make('h3', 'nhead', 'Notifications');
  head.id = 'd-push-head';
  const toggle = make('button', 'nbtn cyan wide');
  toggle.id = 'd-push-on';
  toggle.type = 'button';
  const test = make('button', 'nbtn wide', 'Send a test');
  test.id = 'd-push-test';
  test.type = 'button';
  test.hidden = true;
  const note = make('p', 'hint');
  note.id = 'd-push-note';
  note.setAttribute('role', 'status');
  part.append(head, toggle, test, note);
  const phonePart = byId('d-phone-head')?.closest('.drawer-part');
  if (phonePart) phonePart.after(part);
  else byId('drawer')?.querySelector('.drawer-scroll')?.prepend(part);

  let session = false, status = '', busy = false, synced = false, pcState = null, pendingJob = '';
  const waits = new Map();
  const offered = () => session && remote.state === 'connected' && Array.isArray(remote.info.features) && remote.info.features.includes('push');

  const render = () => {
    const on = wished();
    const why = blocked();
    part.hidden = !session;
    toggle.setAttribute('aria-pressed', String(on));
    toggle.textContent = on ? 'Notifications: on' : 'Notifications: off';
    toggle.disabled = busy || (!!why && !on);
    test.hidden = !on;
    test.disabled = busy;
    note.textContent = status || (why && !on ? why : on ? 'This phone gets a notification when Capri answers, a job needs your yes, or a job ends.' : 'Hear from Capri when this page isn\'t open: her answers, jobs that need your yes, and reminders.');
    window.CapriKeepAlive = !!(bridge() && on);
  };
  const say = words => { status = words; render(); };

  const expect = (id, ms = 10000) => new Promise(resolve => {
    const timer = setTimeout(() => { waits.delete(id); resolve(null); }, ms);
    waits.set(id, message => { clearTimeout(timer); waits.delete(id); resolve(message); });
  });
  const getState = () => new Promise(resolve => {
    const timer = setTimeout(() => { waits.delete('state'); resolve(null); }, 8000);
    waits.set('state', message => { clearTimeout(timer); waits.delete('state'); resolve(message); });
    if (!remote.send('push.get', {})) { clearTimeout(timer); waits.delete('state'); resolve(null); }
  });
  const sendSub = async body => {
    const id = newId();
    const result = expect(id);
    if (!remote.send('push.sub', Object.assign({ id }, body))) { waits.delete(id); return null; }
    return result;
  };

  const registration = async () => {
    await navigator.serviceWorker.register('sw.js', { scope: './' });
    return navigator.serviceWorker.ready;
  };
  const subscription = async () => {
    try {
      if (!webOk()) return null;
      const found = await navigator.serviceWorker.getRegistration('./');
      return found ? await found.pushManager.getSubscription() : null;
    } catch (error) { return null; }
  };
  const keyOf = sub => {
    try { return sub && sub.options && sub.options.applicationServerKey ? toB64u(sub.options.applicationServerKey) : ''; } catch (error) { return ''; }
  };

  const webOn = async quiet => {
    const asked = !quiet && Notification.permission === 'default' ? Notification.requestPermission() : null;
    const permission = asked ? await asked : Notification.permission;
    if (permission !== 'granted') {
      if (!quiet) say(permission === 'denied' ? 'Notifications are blocked for Capri. Allow them in this browser\'s site settings, then turn this on again.' : 'Notifications weren\'t allowed. Turn this on again and choose Allow.');
      return false;
    }
    const state = pcState && pcState.key ? pcState : await getState();
    if (!state || typeof state.key !== 'string' || !state.key) {
      if (!quiet) say('Couldn\'t reach your PC. Try again.');
      return false;
    }
    const reg = await registration();
    let sub = await reg.pushManager.getSubscription();
    if (sub && keyOf(sub) !== state.key) {
      try { await sub.unsubscribe(); } catch (error) {}
      sub = null;
    }
    if (!sub) sub = await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: fromB64u(state.key) });
    const json = sub.toJSON();
    const keys = json.keys || {};
    const result = await sendSub({ on: true, web: { endpoint: json.endpoint, p256dh: keys.p256dh, auth: keys.auth } });
    if (!result) {
      if (!quiet) say('No answer from your PC. Try again.');
      return false;
    }
    if (result.code !== 'saved') {
      if (!quiet) say('Your PC wouldn\'t take this phone\'s subscription. Try again.');
      return false;
    }
    return true;
  };
  const appOn = async quiet => {
    const app = bridge();
    const info = read(app.notifySet(true));
    const result = await sendSub({ on: true, app: true });
    if (!result || result.code !== 'saved') {
      if (!quiet) say(result ? 'Your PC wouldn\'t turn this on. Try again.' : 'No answer from your PC. Try again.');
      return false;
    }
    if (!quiet && info.allowed === false) say('Allow notifications for Capri in Android\'s settings to see them.');
    return true;
  };

  const enable = async quiet => {
    try { return bridge() ? await appOn(quiet) : await webOn(quiet); } catch (error) {
      if (!quiet) say('Couldn\'t turn notifications on here.');
      return false;
    }
  };
  const disable = async () => {
    const app = bridge();
    if (app) { try { app.notifySet(false); } catch (error) {} }
    else {
      const sub = await subscription();
      if (sub) { try { await sub.unsubscribe(); } catch (error) {} }
    }
  };

  toggle.addEventListener('click', async () => {
    if (busy) return;
    if (wished()) {
      wish(false);
      status = '';
      busy = true;
      render();
      if (offered()) remote.send('push.sub', { id: newId(), on: false });
      await disable();
      busy = false;
      say('Notifications are off.');
      return;
    }
    if (blocked() || !offered()) { render(); return; }
    busy = true;
    status = '';
    render();
    const ok = await enable(false);
    busy = false;
    if (ok) {
      wish(true);
      say('Notifications are on.');
    } else {
      if (bridge()) { try { bridge().notifySet(false); } catch (error) {} }
      render();
    }
  });
  test.addEventListener('click', async () => {
    if (busy || !offered()) return;
    busy = true;
    say('Sending a test');
    const id = newId();
    const result = expect(id);
    if (!remote.send('push.test', { id })) { waits.delete(id); busy = false; say('Couldn\'t reach your PC. Try again.'); return; }
    const answer = await result;
    busy = false;
    if (!answer) say('No answer from your PC. Try again.');
    else if (answer.code === 'sent') say('Sent. It should arrive in a moment.');
    else if (answer.code === 'none') say('Your PC has no way to reach this phone yet. Turn notifications off and on again.');
    else say('The test didn\'t go through. Try again.');
  });

  const reconcile = async () => {
    if (synced || !wished() || !pcState || !offered()) return;
    synced = true;
    let need = pcState.on !== true;
    if (!need && bridge()) need = pcState.app === false;
    if (!need && !bridge()) {
      const sub = await subscription();
      need = !!sub && (pcState.web === false || keyOf(sub) !== pcState.key);
      if (!sub && Notification.permission === 'granted') need = true;
    }
    if (need) await enable(true);
  };

  const sendAway = () => { if (offered()) remote.send('push.away', { away: document.hidden }); };
  document.addEventListener('visibilitychange', sendAway);

  const jobs = () => window.CapriJobs;
  const jobsSay = words => {
    const line = byId('jobs')?.querySelector('.jobs-note');
    if (line) { line.textContent = words; line.hidden = !words; }
  };
  const openJob = id => {
    if (!idOk(id)) return;
    if (jobs() && jobs().supported()) { pendingJob = ''; jobs().show(id); return true; }
    pendingJob = id;
    return false;
  };

  window.CapriAndroidAnswer = raw => {
    const item = typeof raw === 'string' ? read(raw) : raw || {};
    if (!idOk(item.job) || !jobs()) return false;
    const askId = idOk(item.ask_id) ? item.ask_id : null;
    const now = jobs().state(item.job);
    if (now && now.state === 'asking' && (askId || null) === (now.ask_id || null) && jobs().answer(item.job, item.yes === true, askId)) {
      jobsSay('');
      return true;
    }
    openJob(item.job);
    jobsSay('That one was already answered.');
    return false;
  };
  window.CapriAndroidOpen = raw => {
    const item = typeof raw === 'string' ? read(raw) : raw || {};
    if ((item.kind === 'ask' || item.kind === 'done') && idOk(item.job)) openJob(item.job);
    else if (jobs()) jobs().close();
    return true;
  };

  const params = new URLSearchParams(location.search);
  if (idOk(params.get('job'))) {
    pendingJob = params.get('job');
    params.delete('job');
    const rest = params.toString();
    try { history.replaceState(null, '', location.pathname + (rest ? '?' + rest : '') + location.hash); } catch (error) {}
  }
  if (navigator.serviceWorker) {
    navigator.serviceWorker.addEventListener('message', event => {
      if (event.data && idOk(event.data.open)) openJob(event.data.open);
    });
  }

  remote.onMessage(message => {
    if (message.type === 'push.state') {
      pcState = { key: typeof message.key === 'string' ? message.key : '', on: message.on === true, web: message.web === true, app: message.app === true };
      if (waits.has('state')) waits.get('state')(pcState);
      reconcile();
    } else if (message.type === 'push.result') {
      if (typeof message.re === 'string' && waits.has(message.re)) waits.get(message.re)({ ok: message.ok === true, code: String(message.code || '') });
    } else if (message.type === 'notify') {
      const app = window.CapriAndroid;
      if (app && typeof app.notify === 'function') { try { app.notify(JSON.stringify(message)); } catch (error) {} }
    } else if (message.type === 'job.state' || message.type === 'job.recent') {
      if (pendingJob) openJob(pendingJob);
    }
  });

  let was = false, held = false;
  remote.onState((state, info) => {
    if (state !== 'unpaired' && state !== 'pairing') held = true;
    if ((state === 'unpaired' && held) || state === 'pairing') {
      held = false;
      const had = wished();
      wish(false);
      if (had) disable();
    }
    const now = state === 'connected' && Array.isArray(info.features) && info.features.includes('push');
    if (now && !was) {
      synced = false;
      pcState = null;
      status = '';
      session = true;
      remote.send('push.get', {});
      if (document.hidden) sendAway();
      if (pendingJob) openJob(pendingJob);
    } else if (state === 'connected' && !now && !was && pendingJob) openJob(pendingJob);
    if (!now) {
      session = false;
      for (const resolve of [...waits.values()]) resolve(null);
    }
    was = now;
    render();
  });

  window.addEventListener('capri-android', () => {
    if (!bridge() || !wished()) return;
    const info = read(bridge().notifyInfo());
    status = info.allowed === false ? 'Allow notifications for Capri in Android\'s settings to see them.' : '';
    render();
  });
  render();
})();
