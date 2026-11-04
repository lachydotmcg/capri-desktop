(() => {
  const box = document.getElementById('countdown');
  if (!box) return;
  const at = Date.parse(box.dataset.at);
  const time = box.querySelector('.countdown-time');
  const two = n => String(n).padStart(2, '0');
  const tick = () => {
    const left = at - Date.now();
    if (left <= 0) { time.textContent = 'dropping any minute'; return; }
    const s = Math.floor(left / 1000);
    const h = Math.floor(s / 3600);
    time.textContent = 'in ' + h + 'h ' + two(Math.floor(s / 60) % 60) + 'm ' + two(s % 60) + 's';
    setTimeout(tick, 1000 - (Date.now() % 1000));
  };
  const local = new Date(at).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
  box.title = 'Drops ' + new Date(at).toLocaleDateString([], { weekday: 'long' }) + ' at ' + local + ' your time';
  tick();
})();
