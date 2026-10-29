// The 0.7.6 launch countdown under the headline: 9:30 am on 30 Oct 2026 in Sydney (22:30 UTC on the 29th), shown in the visitor's own time.
// It stays hidden without JS. At zero it says 0.7.6 is landing, since this page keeps 0.7.4's downloads until the release deploy.
(() => {
  const box = document.getElementById("countdown");
  if (!box) return;
  const at = Date.parse(box.dataset.at);
  const label = box.querySelector(".countdown-label");
  const clock = box.querySelector(".countdown-clock");
  const when = box.querySelector(".countdown-when");
  const two = (n) => String(n).padStart(2, "0");
  when.textContent = new Date(at).toLocaleString(undefined, { weekday: "short", day: "numeric", month: "short", hour: "numeric", minute: "2-digit" }) + " your time";
  const tick = () => {
    const left = Math.ceil((at - Date.now()) / 1000);
    if (left <= 0) {
      box.classList.add("countdown-now");
      label.textContent = "0.7.6 is landing now";
      clock.textContent = "";
      when.textContent = "";
      return;
    }
    clock.textContent = `${two(Math.floor(left / 3600))}:${two(Math.floor(left / 60) % 60)}:${two(left % 60)}`;
    setTimeout(tick, 1000 - (Date.now() % 1000) + 15);
  };
  tick();
  box.hidden = false;
})();
