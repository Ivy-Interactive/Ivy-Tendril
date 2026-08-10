<script>(function(){
  var REAL_URL = @@REAL_URL@@;
  window.__PROXY_TARGET__ = REAL_URL;

  // Client-side device emulation: align navigator.* with the UA we sent
  // upstream so JS feature/device detection matches. Runs before page scripts.
  var DEVICE = @@DEVICE@@;
  if (DEVICE){
    try {
      Object.defineProperty(navigator, 'userAgent', { get: function(){ return DEVICE.ua; }, configurable: true });
      Object.defineProperty(navigator, 'platform', { get: function(){ return DEVICE.platform; }, configurable: true });
      if (DEVICE.mobile){
        Object.defineProperty(navigator, 'maxTouchPoints', { get: function(){ return 5; }, configurable: true });
        if (!('ontouchstart' in window)){ try { window.ontouchstart = null; } catch(e){} }
      }
    } catch(e){}
  }

  function fixProto(s){ return s.replace(/^(https?:)\/(?!\/)/, '$1//'); }
  // The real (upstream) URL of whatever the iframe currently shows. In
  // view-space the path carries it; we derive it so SPA route changes report
  // the right URL.
  function currentReal(){
    try {
      var p = location.pathname;
      if (p.indexOf('/__view/') === 0) return fixProto(p.slice(8)) + location.search;
    } catch(e){}
    return REAL_URL;
  }
  function send(msg){
    try { parent.postMessage(Object.assign({ __proxy: true, url: currentReal() }, msg), '*'); } catch(e){}
  }

  function report(){ send({ type: 'location' }); }
  report();
  document.addEventListener('DOMContentLoaded', report);

  // Follow SPA (client-side) navigations so the parent address bar updates.
  ['pushState','replaceState'].forEach(function(m){
    var orig = history[m];
    history[m] = function(){ var r = orig.apply(this, arguments); setTimeout(report, 0); return r; };
  });
  window.addEventListener('popstate', function(){ setTimeout(report, 0); });

  function stringify(v){
    if (typeof v === 'string') return v;
    if (v instanceof Error) return (v.stack || (v.name + ': ' + v.message));
    try {
      var seen = new WeakSet();
      return JSON.stringify(v, function(k, val){
        if (typeof val === 'object' && val !== null){
          if (seen.has(val)) return '[Circular]';
          seen.add(val);
        }
        if (typeof val === 'function') return '[Function ' + (val.name || 'anonymous') + ']';
        if (typeof val === 'undefined') return '[undefined]';
        return val;
      });
    } catch(e){ try { return String(v); } catch(_) { return '[Unserializable]'; } }
  }
  function formatArgs(args){ return Array.prototype.map.call(args, stringify).join(' '); }

  ['log','info','warn','error','debug'].forEach(function(level){
    var original = console[level];
    console[level] = function(){
      send({ type: 'console', level: level, text: formatArgs(arguments) });
      if (original) try { original.apply(console, arguments); } catch(e){}
    };
  });
  window.addEventListener('error', function(e){
    if (e && e.message){
      send({ type: 'console', level: 'error',
        text: e.message + (e.filename ? ' (' + e.filename + ':' + e.lineno + ':' + e.colno + ')' : ''),
        stack: e.error && e.error.stack });
    }
  }, true);
  window.addEventListener('unhandledrejection', function(e){
    var r = e && e.reason;
    send({ type: 'console', level: 'error', text: 'Unhandled rejection: ' + stringify(r), stack: r && r.stack });
  });

  // ---- element selection (driven by the parent "Select" button) ----
  var selOverlay = null, selActive = false;
  function ensureOverlay(){
    if (selOverlay) return selOverlay;
    var o = document.createElement('div');
    o.style.cssText = 'position:fixed;z-index:2147483647;pointer-events:none;'
      + 'background:rgba(66,133,244,0.25);border:2px solid #1a73e8;border-radius:2px;display:none;';
    (document.body || document.documentElement).appendChild(o);
    selOverlay = o; return o;
  }
  function moveOverlay(el){
    var o = ensureOverlay(), r = el.getBoundingClientRect();
    o.style.display = 'block';
    o.style.left = r.left + 'px'; o.style.top = r.top + 'px';
    o.style.width = r.width + 'px'; o.style.height = r.height + 'px';
  }
  function getXPath(el){
    if (!el || el.nodeType !== 1) return '';
    if (el.id) return '//*[@id=\'' + el.id + '\']';
    var parts = [];
    while (el && el.nodeType === 1){
      var tag = el.nodeName.toLowerCase(), idx = 1, sib = el.previousElementSibling;
      while (sib){ if (sib.nodeName.toLowerCase() === tag) idx++; sib = sib.previousElementSibling; }
      parts.unshift(tag + '[' + idx + ']');
      if (el === document.documentElement) break;
      el = el.parentElement;
    }
    return '/' + parts.join('/');
  }
  // Short, readable CSS-ish selector for the element.
  function cssPath(el){
    var parts = [];
    while (el && el.nodeType === 1 && parts.length < 5){
      var s = el.nodeName.toLowerCase();
      if (el.id){ parts.unshift(s + '#' + el.id); break; }
      if (el.className && typeof el.className === 'string'){
        var c = el.className.trim().split(/\s+/).slice(0, 2).join('.');
        if (c) s += '.' + c;
      }
      parts.unshift(s);
      el = el.parentElement;
    }
    return parts.join(' > ');
  }
  // Useful attributes + a text snippet.
  function describe(el){
    var attrs = {};
    ['id','name','type','role','href','title','placeholder','data-testid','data-test-id','aria-label'].forEach(function(a){
      var v = el.getAttribute && el.getAttribute(a);
      if (v) attrs[a] = v;
    });
    var classes = (el.className && typeof el.className === 'string') ? el.className.trim().split(/\s+/) : [];
    var text = (el.textContent || '').trim().replace(/\s+/g, ' ').slice(0, 80);
    return { tag: el.nodeName.toLowerCase(), id: el.id || null, classes: classes, attrs: attrs, text: text };
  }
  // React fiber -> component path + dev source location (if a React dev build).
  function getFiber(el){
    for (var k in el){
      if (k.indexOf('__reactFiber$') === 0 || k.indexOf('__reactInternalInstance$') === 0) return el[k];
    }
    return null;
  }
  function compName(t){
    if (!t) return null;
    if (typeof t === 'function') return t.displayName || t.name || null;
    if (typeof t === 'object'){
      // memo / forwardRef wrappers
      if (t.displayName) return t.displayName;
      if (t.render) return t.render.displayName || t.render.name || null;
      if (t.type) return compName(t.type);
    }
    return null;
  }
  // Parse "path:line:col" (or "path:line"). Match from the right so Windows
  // drive letters / URL schemes inside the path don't confuse the line/col grab.
  function parseLoc(s){
    if (!s) return null;
    var str = String(s).trim();
    var m = str.match(/^(.*):(\d+):(\d+)$/);
    if (m) return { fileName: m[1], lineNumber: +m[2], columnNumber: +m[3] };
    var m2 = str.match(/^(.*):(\d+)$/);
    if (m2) return { fileName: m2[1], lineNumber: +m2[2], columnNumber: null };
    return { fileName: str, lineNumber: null, columnNumber: null };
  }
  // Framework-agnostic source + component info for a DOM element.
  //  * Source location is read from a build-time data attribute that the viewed
  //    app's dev inspector plugin injects — works for React/Vue/Svelte/etc.
  //    without touching any framework runtime internals. Conventions understood:
  //      data-source-loc="path:line:col"   (our convention)
  //      data-v-inspector="path:line:col"  (vite-plugin-vue-inspector / vue devtools)
  //      data-inspector-relative-path + -line + -column (react-dev-inspector)
  //  * Component names + a React<=18 _debugSource fallback come from the fiber.
  function getSourceInfo(el){
    var source = null, framework = null;
    var locEl = el && el.closest
      ? el.closest('[data-source-loc],[data-v-inspector],[data-inspector-relative-path]')
      : null;
    if (locEl){
      if (locEl.hasAttribute('data-source-loc')){
        source = parseLoc(locEl.getAttribute('data-source-loc'));
      } else if (locEl.hasAttribute('data-v-inspector')){
        source = parseLoc(locEl.getAttribute('data-v-inspector'));
        framework = 'vue';
      } else {
        source = {
          fileName: locEl.getAttribute('data-inspector-relative-path'),
          lineNumber: +locEl.getAttribute('data-inspector-line') || null,
          columnNumber: +locEl.getAttribute('data-inspector-column') || null
        };
        framework = 'react';
      }
    }
    var components = [], version = null, fiber = getFiber(el);
    if (fiber){
      framework = framework || 'react';
      version = (window.React && window.React.version) || null;
      var f = fiber;
      while (f){
        var name = compName(f.type);
        if (name && components[components.length - 1] !== name) components.push(name);
        if (!source && f._debugSource) source = f._debugSource;       // {fileName,lineNumber,columnNumber}
        f = f.return;
      }
    }
    if (!source && !components.length && !framework) return null;
    return { components: components, source: source, version: version, framework: framework };
  }
  function onMove(e){ var el = e.target; if (el && el !== selOverlay) moveOverlay(el); }
  function onClick(e){
    e.preventDefault(); e.stopPropagation();
    if (selOverlay) selOverlay.style.display = 'none';
    var el = document.elementFromPoint(e.clientX, e.clientY) || e.target;
    send({
      type: 'selected',
      xpath: getXPath(el),
      selector: cssPath(el),
      meta: describe(el),
      react: getSourceInfo(el)
    });
    stopSelect();
  }
  function onKey(e){ if (e.key === 'Escape'){ stopSelect(); send({ type: 'select-cancelled' }); } }
  function startSelect(){
    if (selActive) return; selActive = true; ensureOverlay();
    document.addEventListener('mousemove', onMove, true);
    document.addEventListener('click', onClick, true);
    document.addEventListener('keydown', onKey, true);
    if (document.body) document.body.style.cursor = 'crosshair';
  }
  function stopSelect(){
    selActive = false;
    document.removeEventListener('mousemove', onMove, true);
    document.removeEventListener('click', onClick, true);
    document.removeEventListener('keydown', onKey, true);
    if (selOverlay) selOverlay.style.display = 'none';
    if (document.body) document.body.style.cursor = '';
  }
  // ---- screenshot capture (runs inside the iframe, same realm as the DOM) ----
  // Uses snapDOM (much faster than html-to-image, which froze the shared main thread on
  // heavy pages). Loaded once as an ES module from the same-origin /__lib endpoint.
  var snapdomPromise = null;
  function loadSnapdom(){
    if (snapdomPromise) return snapdomPromise;
    snapdomPromise = import(location.origin + '/__lib/snapdom.mjs').then(function(m){ return m.snapdom; });
    return snapdomPromise;
  }
  function doCapture(mode){
    loadSnapdom().then(function(snapdom){
      var docEl = document.documentElement;
      var vw = docEl.clientWidth, vh = docEl.clientHeight;
      var opts = { fast: true, dpr: 1, embedFonts: false, backgroundColor: '#ffffff' };

      // 'viewport' (default): only the visible area. The synchronous cost of serializing
      // the DOM is ~linear in node count, so on huge pages we prune everything outside
      // the viewport first (filterMode 'remove') — that's what keeps the main thread from
      // freezing — then crop the rasterized canvas to the exact visible rectangle.
      if (mode !== 'page'){
        // Drop only what's entirely BELOW the viewport (the usual bulk of a long page).
        // We keep everything from the document top down through the viewport so removing
        // nodes never collapses the layout above — that keeps the scroll-offset crop
        // correct at any scroll position.
        opts.filter = function(el){
          try {
            if (el === document.body || el === docEl) return true;
            var r = el.getBoundingClientRect();
            if (!r.width && !r.height) return true;      // keep zero-box wrappers
            return r.top < vh;                            // remove only nodes at/below the fold
          } catch(e){ return true; }
        };
        opts.filterMode = 'remove';
      }

      return snapdom(docEl, opts)
        .then(function(result){ return result.toCanvas(); })
        .then(function(full){
          var out = full;
          if (mode !== 'page'){
            // The full canvas is in document coordinates (dpr:1), so the visible region
            // starts at the scroll offset.
            out = document.createElement('canvas');
            out.width = vw; out.height = vh;
            var g = out.getContext('2d');
            g.fillStyle = '#ffffff'; g.fillRect(0, 0, vw, vh);
            g.drawImage(full, window.scrollX || 0, window.scrollY || 0, vw, vh, 0, 0, vw, vh);
          }
          var dataUrl = out.toDataURL('image/png');
          send({ type: 'capture-result', mode: mode, dataUrl: dataUrl, w: out.width, h: out.height });
        });
    }).catch(function(err){
      send({ type: 'capture-error', mode: mode, message: (err && err.message) || 'capture failed' });
    });
  }

  // ---- red-pen drawing ----
  // A full-document canvas overlay. Mouse-drag draws; wheel still scrolls (we
  // don't preventDefault wheel). Each sampled point records the element beneath
  // it (same detail as clicks) plus its page position. On mouse-up the whole
  // stroke is sent to the parent as a 'draw' event.
  var drawCanvas = null, drawCtx = null, drawing = false, stroke = null, lastPt = null;
  function docSize(){
    var b = document.body, e = document.documentElement;
    return {
      w: Math.max(b.scrollWidth, e.scrollWidth, e.clientWidth),
      h: Math.max(b.scrollHeight, e.scrollHeight, e.clientHeight)
    };
  }
  function elementAt(cx, cy){
    var els = document.elementsFromPoint(cx, cy) || [];
    for (var i = 0; i < els.length; i++){ if (els[i] !== drawCanvas) return els[i]; }
    return document.body;
  }
  function pointInfo(cx, cy){
    var el = elementAt(cx, cy);
    return {
      x: Math.round(cx + (window.scrollX || 0)),
      y: Math.round(cy + (window.scrollY || 0)),
      xpath: getXPath(el),
      selector: cssPath(el),
      meta: { tag: el.nodeName.toLowerCase(), id: el.id || null },
      react: getSourceInfo(el)
    };
  }
  function onDrawDown(e){
    if (e.button !== 0) return;
    e.preventDefault();
    drawing = true;
    stroke = { points: [] };
    var px = e.clientX + (window.scrollX || 0), py = e.clientY + (window.scrollY || 0);
    lastPt = { x: px, y: py };
    drawCtx.beginPath(); drawCtx.moveTo(px, py);
    stroke.points.push(pointInfo(e.clientX, e.clientY));
    try { drawCanvas.setPointerCapture(e.pointerId); } catch(_){}
  }
  function onDrawMove(e){
    if (!drawing) return;
    e.preventDefault();
    var px = e.clientX + (window.scrollX || 0), py = e.clientY + (window.scrollY || 0);
    if (lastPt && Math.abs(px - lastPt.x) + Math.abs(py - lastPt.y) < 3) return;
    drawCtx.lineTo(px, py); drawCtx.stroke();
    drawCtx.beginPath(); drawCtx.moveTo(px, py);
    lastPt = { x: px, y: py };
    stroke.points.push(pointInfo(e.clientX, e.clientY));
  }
  function onDrawUp(){
    if (!drawing) return;
    drawing = false;
    if (stroke && stroke.points.length) send({ type: 'draw', points: stroke.points });
    stroke = null; lastPt = null;
  }
  function startDraw(){
    if (drawCanvas) return;
    var c = document.createElement('canvas');
    var sz = docSize();
    c.width = sz.w; c.height = sz.h;
    c.style.cssText = 'position:absolute;left:0;top:0;z-index:2147483646;'
      + 'width:' + sz.w + 'px;height:' + sz.h + 'px;cursor:crosshair;touch-action:pan-y;';
    (document.body || document.documentElement).appendChild(c);
    drawCtx = c.getContext('2d');
    drawCtx.strokeStyle = '#e60000'; drawCtx.lineWidth = 3;
    drawCtx.lineCap = 'round'; drawCtx.lineJoin = 'round';
    drawCanvas = c;
    c.addEventListener('pointerdown', onDrawDown);
    c.addEventListener('pointermove', onDrawMove);
    window.addEventListener('pointerup', onDrawUp);
  }
  function stopDraw(){
    if (drawCanvas){
      drawCanvas.removeEventListener('pointerdown', onDrawDown);
      drawCanvas.removeEventListener('pointermove', onDrawMove);
      window.removeEventListener('pointerup', onDrawUp);
      drawCanvas.remove();
      drawCanvas = null; drawCtx = null;
    }
    drawing = false; stroke = null; lastPt = null;
  }

  window.addEventListener('message', function(e){
    var d = e.data;
    if (!d) return;
    if (d.__proxyCmd === 'select-start') startSelect();
    else if (d.__proxyCmd === 'select-stop') stopSelect();
    else if (d.__proxyCmd === 'capture') doCapture(d.mode);
    else if (d.__proxyCmd === 'draw-start') startDraw();
    else if (d.__proxyCmd === 'draw-stop') stopDraw();
  });

  // ---- click recorder ----
  // Always emit a rich click event (same element detail the picker produces);
  // the parent decides whether to store it. Capture phase + no preventDefault,
  // so it sees every click and the page keeps working normally.
  document.addEventListener('click', function(e){
    if (selActive) return; // selection mode owns its own clicks
    var el = e.target;
    if (!el || el.nodeType !== 1) el = (el && el.parentElement) || document.body;
    send({
      type: 'click',
      button: e.button,
      x: e.clientX, y: e.clientY,
      xpath: getXPath(el),
      selector: cssPath(el),
      meta: describe(el),
      react: getSourceInfo(el)
    });
  }, true);

  // ---- injected user code goes here (runs in the page context) ----
  // e.g. window.__PROXY_TARGET__ is the real URL of this page.
})();</script>