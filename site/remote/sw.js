'use strict';

self.addEventListener('install', () => { self.skipWaiting(); });
self.addEventListener('activate', event => { event.waitUntil(self.clients.claim()); });

const idOk = id => typeof id === 'string' && /^[A-Za-z0-9_-]{1,64}$/.test(id);
const clip = (text, max) => typeof text === 'string' ? text.replace(/[\x00-\x1f\x7f]/g, ' ').slice(0, max) : '';

self.addEventListener('push', event => {
  let data = {};
  try { data = event.data ? event.data.json() || {} : {}; } catch (error) { data = {}; }
  if (typeof data !== 'object') data = {};
  const kind = ['reply', 'ask', 'done', 'reminder', 'test', 'checkin'].includes(data.kind) ? data.kind : '';
  const job = idOk(data.job) ? data.job : '';
  const grouped = (kind === 'ask' || kind === 'done') && job;
  const options = {
    body: clip(data.body, 200),
    tag: grouped ? 'job-' + job : idOk(data.id) ? data.id : 'capri',
    icon: 'icons/icon-192.png',
    badge: 'icons/icon-192.png',
    data: { kind, job },
    renotify: kind === 'ask',
    requireInteraction: kind === 'ask'
  };
  event.waitUntil(self.registration.showNotification(clip(data.title, 60) || 'Capri', options));
});

self.addEventListener('notificationclick', event => {
  event.notification.close();
  const job = event.notification.data && idOk(event.notification.data.job) ? event.notification.data.job : '';
  event.waitUntil((async () => {
    const list = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    const page = list.find(client => new URL(client.url).pathname.startsWith(new URL(self.registration.scope).pathname));
    if (page) {
      if (job) page.postMessage({ open: job });
      try { await page.focus(); } catch (error) {}
      return;
    }
    await self.clients.openWindow('./' + (job ? '?job=' + encodeURIComponent(job) : ''));
  })());
});
