// Does the phone page fit an iPhone? For the page tests (lib/cdp.mjs): the three widths from the iPhone SE to the Pro Max,
// in Safari and as a home screen app, and a check that nothing on screen runs past the viewport and that a sheet's frame
// holds all of its words.

// CSS pixels: iPhone SE, iPhone 15 and 16, iPhone Pro Max.
export const IPHONES = [[375, 667], [390, 844], [430, 932]];
// Safari keeps the page clear of the notch and the home bar. A home screen app runs under them (viewport-fit=cover), so
// its safe areas are acted out: a Pro Max's status bar and the home bar.
export const SAFARI = { name: 'safari', insets: { top: 0, bottom: 0, left: 0, right: 0 } };
export const HOME_SCREEN = { name: 'app', insets: { top: 59, bottom: 34, left: 0, right: 0 } };

const wait = ms => new Promise(resolve => setTimeout(resolve, ms));

// A phone's viewport: overlay scrollbars and the page's own viewport meta, like Safari's.
export const setPhone = async (page, [width, height], mode = SAFARI) => {
  await page.send('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 2, mobile: true });
  await page.send('Emulation.setSafeAreaInsetsOverride', { insets: mode.insets });
  // app.js sizes the app on resize, then lays out her and the bar.
  await wait(350);
};

// Runs in the page. Returns what doesn't fit, in words, or [] when everything does.
function misfits(sheetSelector) {
  const problems = [];
  const shown = node => {
    for (let at = node; at && at.nodeType === 1; at = at.parentElement) {
      const style = getComputedStyle(at);
      if (style.display === 'none' || style.visibility === 'hidden') return false;
    }
    return node.getClientRects().length > 0;
  };
  const name = node => node.tagName.toLowerCase() + (node.id ? '#' + node.id : '') + (typeof node.className === 'string' && node.className.trim() ? '.' + node.className.trim().split(/\s+/).join('.') : '') + (node.textContent ? ' "' + node.textContent.trim().slice(0, 30) + '"' : '');
  // Her sprite and her quips go wherever she's thrown, and an svg's insides are its own business.
  const counted = node => !node.closest('#her-layer, .sr-only') && !(node.closest('svg') && node.tagName.toLowerCase() !== 'svg');
  const width = document.documentElement.clientWidth;
  for (const node of document.querySelectorAll('body *')) {
    if (!counted(node) || !shown(node)) continue;
    const box = node.getBoundingClientRect();
    if (!box.width && !box.height) continue;
    if (box.right > width + 0.5) problems.push(name(node) + ' ends at ' + Math.round(box.right) + ', past the viewport\'s ' + width);
    if (box.left < -0.5) problems.push(name(node) + ' starts at ' + Math.round(box.left) + ', left of the viewport');
  }
  if (!sheetSelector) return problems;
  const frame = document.querySelector(sheetSelector);
  if (!frame || !shown(frame)) return problems.concat(sheetSelector + ' is not on screen');
  // Its words scroll in .sheet-scroll. A sheet without one scrolls itself, which the check below catches: the frame, drawn
  // by .nc's ::before and ::after over the sheet's own box, then stops where the first screenful does.
  const words = frame.querySelector('.sheet-scroll') || frame;
  const box = frame.getBoundingClientRect();
  // The safe area as the page sees it.
  const probe = document.createElement('div');
  probe.style.cssText = 'position: fixed; left: 0; width: 1px; top: env(safe-area-inset-top, 0px); bottom: env(safe-area-inset-bottom, 0px); visibility: hidden; pointer-events: none';
  document.body.append(probe);
  const safe = probe.getBoundingClientRect();
  probe.remove();
  if (frame.scrollHeight > frame.clientHeight + 1) problems.push(sheetSelector + ' scrolls itself (' + frame.scrollHeight + ' > ' + frame.clientHeight + '), so the end of its words runs out of its frame');
  if (box.top < safe.top - 0.5 || box.bottom > safe.bottom + 0.5) problems.push(sheetSelector + ' runs from ' + Math.round(box.top) + ' to ' + Math.round(box.bottom) + ', outside the safe area ' + Math.round(safe.top) + ' to ' + Math.round(safe.bottom));
  for (const node of frame.querySelectorAll('*')) {
    if (!counted(node) || !shown(node)) continue;
    const at = node.getBoundingClientRect();
    if (!at.width && !at.height) continue;
    if (at.left < box.left - 0.5 || at.right > box.right + 0.5) problems.push(name(node) + ' sticks out of the side of ' + sheetSelector + ' (' + Math.round(at.left) + ' to ' + Math.round(at.right) + ' in ' + Math.round(box.left) + ' to ' + Math.round(box.right) + ')');
  }
  // Scrolled to the end, the last of its words is inside the frame.
  const last = [...words.children].filter(shown).pop();
  const was = words.scrollTop;
  words.scrollTop = words.scrollHeight;
  const end = last ? last.getBoundingClientRect() : null;
  if (end && (end.bottom > box.bottom + 0.5 || end.top < box.top - 0.5)) problems.push(name(last) + ' is outside ' + sheetSelector + ' when scrolled to the end (' + Math.round(end.top) + ' to ' + Math.round(end.bottom) + ' in ' + Math.round(box.top) + ' to ' + Math.round(box.bottom) + ')');
  words.scrollTop = was;
  return problems;
}

export const misfitsOf = (page, sheetSelector = '') => page.run(`(${misfits.toString()})(${JSON.stringify(sheetSelector)})`);

// Scrolls a sheet's words to the top or the end, for a screenshot.
export const scrollSheet = (page, sheetSelector, toEnd) => page.run(`(() => { const words = document.querySelector(${JSON.stringify(sheetSelector + ' .sheet-scroll')}); if (words) words.scrollTop = ${toEnd ? 'words.scrollHeight' : '0'}; return true; })()`);
