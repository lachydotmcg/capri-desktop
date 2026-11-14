(() => {
  'use strict';
  const file = document.getElementById('room-attach-input');
  const input = document.getElementById('msg');
  const preview = document.createElement('div');
  preview.id = 'attach-preview';
  preview.className = 'attach-preview';
  preview.hidden = true;
  const image = document.createElement('img');
  image.alt = 'Picture to send';
  const remove = document.createElement('button');
  remove.type = 'button';
  remove.className = 'nbtn cyan';
  remove.textContent = 'Remove';
  remove.setAttribute('aria-label', 'Remove picture');
  preview.append(image, remove);
  document.getElementById('bar').before(preview);
  const kept = new WeakMap();
  const waiting = new Map();
  let current = null, version = 0, working = false;
  const changed = () => input.dispatchEvent(new Event('input', { bubbles: true }));
  const note = document.createElement('p');
  note.id = 'attach-note';
  note.className = 'notice attach-note';
  note.hidden = true;
  note.setAttribute('role', 'status');
  document.getElementById('bar').before(note);
  let noteTimer = 0;
  const say = text => {
    note.textContent = text;
    note.hidden = false;
    clearTimeout(noteTimer);
    noteTimer = setTimeout(() => { note.hidden = true; }, 3800);
  };
  const supported = () => CapriRemote.info.native || (CapriRemote.info.features || []).includes('pictures');
  const url = picture => {
    const base64 = picture.data.replace(/-/g, '+').replace(/_/g, '/');
    return 'data:' + picture.mime + ';base64,' + base64 + '='.repeat((4 - base64.length % 4) % 4);
  };
  const show = value => {
    current = value;
    preview.hidden = !current;
    if (current) image.src = url(current.picture);
    else image.removeAttribute('src');
    changed();
  };
  const size = (text, id, picture) => new TextEncoder().encode(JSON.stringify({ id, text, picture, type: 'chat.send' })).length;
  const fit = async (canvas, text, id, previous) => {
    if (previous && size(text, id, previous) < 60000) return previous;
    let width = canvas.width, height = canvas.height;
    const small = document.createElement('canvas');
    for (;;) {
      small.width = width;
      small.height = height;
      const paint = small.getContext('2d');
      paint.fillStyle = '#fff';
      paint.fillRect(0, 0, width, height);
      paint.drawImage(canvas, 0, 0, width, height);
      for (const quality of [0.84, 0.65, 0.45, 0.25]) {
        const data = small.toDataURL('image/jpeg', quality).split(',')[1];
        if (!data) throw new Error('picture');
        const picture = { mime: 'image/jpeg', data: data.replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '') };
        if (size(text, id, picture) < 60000) return picture;
      }
      if (width === 1 && height === 1) throw new Error('picture');
      width = Math.max(1, Math.floor(width * 0.8));
      height = Math.max(1, Math.floor(height * 0.8));
      await new Promise(resolve => setTimeout(resolve, 0));
    }
  };
  const read = blob => new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result);
    reader.onerror = reader.onabort = reject;
    reader.readAsDataURL(blob);
  });
  remove.addEventListener('click', () => { version++; working = false; show(null); input.focus(); });
  document.getElementById('room-attach').addEventListener('click', event => {
    if (supported()) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    document.getElementById('room-menu').hidden = true;
    const camera = document.getElementById('room-camera');
    camera.setAttribute('aria-expanded', 'false');
    camera.focus();
    say('Update Capri on your PC to send pictures.');
  }, true);
  file.addEventListener('change', async () => {
    const picked = file.files;
    if (!picked || !picked.length) return;
    const photo = picked[0], count = picked.length;
    file.value = '';
    if (!supported()) { say('Update Capri on your PC to send pictures.'); return; }
    const mine = ++version;
    working = true;
    changed();
    try {
      if (count !== 1) throw new Error('picture');
      let decoded;
      if (window.createImageBitmap) decoded = await createImageBitmap(photo, { resizeWidth: 1568, resizeQuality: 'high', imageOrientation: 'from-image' });
      else {
        decoded = new Image();
        decoded.src = await read(photo);
        await decoded.decode();
      }
      const width = decoded.naturalWidth || decoded.width, height = decoded.naturalHeight || decoded.height;
      const scale = Math.min(1, 1568 / Math.max(width, height));
      const canvas = document.createElement('canvas');
      canvas.width = Math.max(1, Math.round(width * scale));
      canvas.height = Math.max(1, Math.round(height * scale));
      try { canvas.getContext('2d').drawImage(decoded, 0, 0, canvas.width, canvas.height); }
      finally {
        if (decoded.close) decoded.close();
        else decoded.removeAttribute('src');
      }
      const picture = await fit(canvas, '', 'x'.repeat(64));
      if (mine === version) show({ canvas, picture });
    } catch (error) {
      if (mine === version) say("That isn't a picture I can send.");
    } finally {
      if (mine === version) { working = false; changed(); }
    }
  });
  const failed = id => {
    const sent = waiting.get(id);
    if (!sent) return;
    clearTimeout(sent.timer);
    waiting.delete(id);
    if (!current) show(sent.value);
    if (!input.value) input.value = sent.text;
    working = false;
    changed();
  };
  CapriRemote.onMessage(message => {
    const sent = waiting.get(message.re);
    if (!sent) return;
    if (message.type === 'error') { failed(message.re); return; }
    if (message.type === 'chat.reply' || message.type === 'chat.typing' && message.on && !sent.native) {
      clearTimeout(sent.timer);
      if (current === sent.value) show(null);
      if (input.value === sent.text) input.value = '';
      working = false;
      changed();
      if (message.type === 'chat.reply' && message.done) waiting.delete(message.re);
    }
  });
  CapriRemote.onState(state => {
    if (state === 'connected') return;
    for (const id of waiting.keys()) failed(id);
  });
  window.CapriAttach = {
    hasPicture: () => !!current,
    get busy() { return working; },
    prepare: async (text, id) => {
      if (!current || working) return null;
      if (!supported()) { say('Update Capri on your PC to send pictures.'); return null; }
      const value = current, mine = version;
      working = true;
      changed();
      try {
        if (CapriRemote.info.native && !await CapriNative.pictures()) { say("This brain can't see pictures."); return null; }
        const picture = await fit(value.canvas, text, id, value.picture);
        if (mine !== version || value !== current) return null;
        value.picture = picture;
        image.src = url(picture);
        return picture;
      } catch (error) { say("Couldn't send the picture. Try again."); return null; }
      finally { if (mine === version) { working = false; changed(); } }
    },
    sent: (id, text, picture) => {
      waiting.set(id, { text, value: current, native: !!CapriRemote.info.native, timer: setTimeout(() => {
        failed(id);
        say('No reply. Try sending again.');
      }, CapriRemote.info.native ? 125000 : 15000) });
      working = true;
      changed();
    },
    failed,
    say,
    keep: (message, picture) => kept.set(message, picture),
    render: (node, message) => {
      const picture = kept.get(message);
      if (!picture) return;
      const photo = document.createElement('img');
      photo.className = 'attach-picture';
      photo.alt = 'Your picture';
      photo.src = url(picture);
      node.append(photo);
    }
  };
})();
