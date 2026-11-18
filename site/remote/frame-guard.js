// GitHub Pages sends no response headers, so the anti-framing headers in site/_headers never reach a reader there. This
// page refuses to sit inside someone else's frame instead: it takes the tab over, and if that is blocked it stays blank
// rather than becoming an invisible click target over her chat and pairing screen.
//
// It lives in a file, not inline, because the page's own CSP (script-src 'self') would refuse an inline script.
(function () {
  "use strict";

  function unframe() {
    if (window.top === window.self) return;
    try {
      window.top.location = window.self.location.href;
    } catch (error) {
      // Cross-origin frames can't be navigated; hiding is the fallback.
    }
    document.documentElement.style.visibility = "hidden";
    var note = document.createElement("p");
    note.textContent = "Capri Remote won't open inside another page. Go to capriai.app/remote directly.";
    note.style.cssText = "padding:24px;font:16px/1.5 sans-serif;color:#ece8f1;background:#0b0914";
    document.addEventListener("DOMContentLoaded", function () {
      if (document.body) document.body.appendChild(note);
    });
  }

  unframe();
  // A frame can be moved after load, so check again once the document settles.
  window.addEventListener("load", unframe);
})();
