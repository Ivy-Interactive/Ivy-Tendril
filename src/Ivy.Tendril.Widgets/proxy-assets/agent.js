// Injected into every proxied page by WebViewerProxy. The REAL_URL and DEVICE values
// below are substituted server-side before this script is injected.
(function(){
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

  // ---- protect the proxy's own service worker ----
  // The proxied page runs on the VIEWER's origin, so navigator.serviceWorker hands it
  // control of the very registration the proxy depends on. Plenty of sites ship
  // "unregister the legacy worker" cleanup — getRegistrations().then(r => r.unregister())
  // — which would tear the proxy down mid-session; from then on every root-relative
  // request falls through to the host app as a 404 and nothing recovers it. Give the page
  // an inert view of the API instead: it sees an origin with no service worker, which is
  // exactly what it would see if it were not being proxied.
  try {
    if (navigator.serviceWorker){
      var inertServiceWorker = {
        controller: null,
        // Spec behaviour on an origin with no registration: pending forever, never rejects.
        ready: new Promise(function(){}),
        register: function(){ return Promise.reject(new Error('Service workers are disabled in the WebViewer.')); },
        getRegistration: function(){ return Promise.resolve(undefined); },
        getRegistrations: function(){ return Promise.resolve([]); },
        startMessages: function(){},
        addEventListener: function(){},
        removeEventListener: function(){},
        dispatchEvent: function(){ return false; }
      };
      Object.defineProperty(navigator, 'serviceWorker', {
        configurable: true,
        get: function(){ return inertServiceWorker; }
      });
    }
  } catch(e){}

  function fixProto(s){ return s.replace(/^(https?:)\/(?!\/)/, '$1//'); }

  // View-space is /__view/[@<viewer>[.<device>]/]<absolute-url>. The optional token names
  // which mounted WebViewer this document belongs to and which device it emulates, so two
  // viewers on one Ivy page never answer for each other's traffic. Only the document URL
  // carries it — subresources are rewritten bare and the service worker resolves them
  // through the client that asked. Same grammar in sw.js and WebViewerProxy.cs.
  var VIEW = '/__view/';
  var VIEW_TOKEN_RE = /^@([A-Za-z0-9]{1,16})(?:\.(mobile|tablet))?\//;
  function stripViewToken(rest){ return rest.replace(VIEW_TOKEN_RE, ''); }
  // This document's own token, kept so links we redirect stay inside the same viewer.
  var VIEW_TOKEN = (function(){
    try {
      var p = location.pathname;
      if (p.indexOf(VIEW) !== 0) return '';
      var m = VIEW_TOKEN_RE.exec(p.slice(VIEW.length));
      return m ? m[0] : '';
    } catch(e){ return ''; }
  })();

  // The real (upstream) URL of whatever the iframe currently shows. In
  // view-space the path carries it; we derive it so SPA route changes report
  // the right URL.
  var REAL_ORIGIN = (function(){ try { return new URL(REAL_URL).origin; } catch(e){ return null; } })();

  function currentReal(){
    try {
      var p = location.pathname;
      if (p.indexOf(VIEW) === 0){
        return fixProto(stripViewToken(p.slice(VIEW.length))) + location.search + location.hash;
      }
      if (REAL_ORIGIN) return REAL_ORIGIN + p + location.search + location.hash;
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
  window.addEventListener('hashchange', function(){ setTimeout(report, 0); });

  // ---- runtime-created <link> elements ----
  // The browser issues <link rel=prefetch|modulepreload|preload> fetches from OUTSIDE the
  // service worker, so a root-relative href never gets mapped into view-space and lands on
  // the Ivy origin as a 404. Links present in the served HTML were already rewritten
  // server-side; these are the ones the page's own scripts build at runtime (VitePress
  // route prefetching, webpack chunk preloads). Map the href as it is assigned — before the
  // element is inserted and the fetch starts.
  function toViewSpace(value){
    try {
      var v = String(value == null ? '' : value).trim();
      if (!v || /^(data:|blob:|javascript:|mailto:|tel:|about:|#)/i.test(v)) return value;
      if (v.indexOf('/__view/') === 0) return value;
      var abs = new URL(v, currentReal());
      if (abs.protocol !== 'http:' && abs.protocol !== 'https:') return value;
      if (abs.origin === location.origin) return value; // already ours, or already mapped
      return '/__view/' + abs.href;
    } catch(e){ return value; }
  }
  try {
    var nativeCreateElement = Document.prototype.createElement;
    var linkHref = Object.getOwnPropertyDescriptor(HTMLLinkElement.prototype, 'href');
    if (linkHref && linkHref.set){
      Document.prototype.createElement = function(tagName){
        var el = nativeCreateElement.apply(this, arguments);
        try {
          if (typeof tagName === 'string' && tagName.toLowerCase() === 'link'){
            Object.defineProperty(el, 'href', {
              configurable: true,
              get: function(){ return linkHref.get.call(this); },
              set: function(value){ linkHref.set.call(this, toViewSpace(value)); }
            });
            var nativeSetAttribute = el.setAttribute;
            el.setAttribute = function(name, value){
              if (String(name).toLowerCase() === 'href') value = toViewSpace(value);
              return nativeSetAttribute.call(this, name, value);
            };
          }
        } catch(e){}
        return el;
      };
    }
  } catch(e){}

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
  // An id predicate, or '' when the id cannot be expressed safely as an XPath literal.
  function idPredicate(id){
    if (!id) return '';
    if (id.indexOf("'") === -1) return "[@id='" + id + "']";
    if (id.indexOf('"') === -1) return '[@id="' + id + '"]';
    return '';
  }
  // Full absolute path, with ids folded in as extra predicates rather than replacing the
  // path. Collapsing to //*[@id='x'] as soon as an element has an id is shorter but throws
  // the ancestry away, and the ancestry is what still locates the element when the id is
  // generated, duplicated, or changes between builds. Position comes first so a step reads
  // "the Nth <tag>, which also carries this id" — an id predicate placed first would make
  // [N] index into the id-matched set and select nothing.
  function getXPath(el){
    if (!el || el.nodeType !== 1) return '';
    var parts = [];
    while (el && el.nodeType === 1){
      var tag = el.nodeName.toLowerCase(), idx = 1, sib = el.previousElementSibling;
      while (sib){ if (sib.nodeName.toLowerCase() === tag) idx++; sib = sib.previousElementSibling; }
      parts.unshift(tag + '[' + idx + ']' + idPredicate(el.getAttribute && el.getAttribute('id')));
      if (el === document.documentElement) break;
      el = el.parentElement;
    }
    return '/' + parts.join('/');
  }
  // Short, readable CSS-ish selector for the element.
  var SAFE_IDENT = /^[a-zA-Z_-][a-zA-Z0-9_-]*$/;
  function cssPath(el){
    var parts = [];
    while (el && el.nodeType === 1 && parts.length < 5){
      var s = el.nodeName.toLowerCase();
      // Walk past an id rather than stopping at it. A lone #id is the shortest unique
      // selector, but framework-generated ids (radix-_R_1b5…) are regenerated on every
      // render, and the surrounding chain is what still finds the element when they change.
      if (el.id && SAFE_IDENT.test(el.id)) s += '#' + el.id;
      if (el.className && typeof el.className === 'string'){
        // Skip variant classes (focus:x, md:x, w-1/2): they would need escaping to form a
        // valid selector and say nothing about which element this is.
        var c = el.className.trim().split(/\s+/).filter(function(n){ return SAFE_IDENT.test(n); })
          .slice(0, 2).join('.');
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
    var rect = null;
    try { var r = el.getBoundingClientRect();
          rect = { x: Math.round(r.left), y: Math.round(r.top), w: Math.round(r.width), h: Math.round(r.height) }; } catch(e){}
    // Truncated: the markup itself is often the fastest way for an agent to recognise the
    // element, but a wrapper div can carry a whole page of descendants.
    var outer = '';
    try { outer = (el.outerHTML || '').slice(0, 600); } catch(e){}
    return { tag: el.nodeName.toLowerCase(), id: el.id || null, classes: classes, attrs: attrs,
             text: text, outerHtml: outer, rect: rect };
  }
  // ---- source attribution ------------------------------------------------
  //
  // Goal: give a fixing agent enough to find the code behind a clicked element, on any
  // stack, without the viewed app cooperating. Rather than a lookup per framework, every
  // signal is normalised to one currency — a raw JS frame (url:line:col) — which the proxy
  // resolves through the bundle's source map (see /__resolve). Tiers run best-first and
  // each result carries provenance + confidence so the consumer knows what it is trusting.

  var MAX_STACK_FRAMES = 24;

  function parseLoc(s){
    if (!s) return null;
    var str = String(s).trim();
    // Match from the right: a Windows drive letter or URL scheme also contains ':'.
    var m = str.match(/^(.*):(\d+):(\d+)$/);
    if (m) return { file: m[1], line: +m[2], col: +m[3] };
    var m2 = str.match(/^(.*):(\d+)$/);
    if (m2) return { file: m2[1], line: +m2[2], col: null };
    return { file: str, line: null, col: null };
  }

  // Tier 0 — build-time attributes left by a dev inspector plugin. Highest fidelity: the
  // file/line are already the app's own source, so nothing needs resolving.
  function tier0(el){
    if (!el || !el.closest) return null;
    var host = el.closest('[data-source-loc],[data-v-inspector],[data-inspector-relative-path],'
      + '[data-astro-source-file],[data-locatorjs-id]');
    if (!host) return null;
    if (host.hasAttribute('data-source-loc')){
      return { loc: parseLoc(host.getAttribute('data-source-loc')), provenance: 'attribute:data-source-loc' };
    }
    if (host.hasAttribute('data-v-inspector')){
      return { loc: parseLoc(host.getAttribute('data-v-inspector')), provenance: 'attribute:vue-inspector', framework: 'vue' };
    }
    if (host.hasAttribute('data-inspector-relative-path')){
      return {
        loc: { file: host.getAttribute('data-inspector-relative-path'),
               line: +host.getAttribute('data-inspector-line') || null,
               col: +host.getAttribute('data-inspector-column') || null },
        provenance: 'attribute:react-dev-inspector', framework: 'react'
      };
    }
    if (host.hasAttribute('data-astro-source-file')){
      var astro = parseLoc(host.getAttribute('data-astro-source-loc') || '');
      return {
        loc: { file: host.getAttribute('data-astro-source-file'),
               line: astro ? astro.line : null, col: astro ? astro.col : null },
        provenance: 'attribute:astro', framework: 'astro'
      };
    }
    // LocatorJS encodes as file::line::col.
    var parts = String(host.getAttribute('data-locatorjs-id') || '').split('::');
    if (parts.length >= 2){
      return { loc: { file: parts[0], line: +parts[1] || null, col: +parts[2] || null },
               provenance: 'attribute:locatorjs' };
    }
    return null;
  }

  // ---- Tier 1 adapters ----
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
      if (t.displayName) return t.displayName;                 // memo / forwardRef wrappers
      if (t.render) return t.render.displayName || t.render.name || null;
      if (t.type) return compName(t.type);
    }
    return null;
  }
  // Frames name URLs on the VIEWER's origin — proxied scripts keep the site's own path
  // (see RewriteScriptUrl) and view-space carries it in the path. The resolver must fetch
  // the real script and its source map from upstream, so translate before handing them over.
  function toUpstreamUrl(u){
    try {
      var abs = new URL(u);
      if (abs.origin !== location.origin) return u;
      var path = abs.pathname;
      if (path.indexOf(VIEW) === 0) return fixProto(stripViewToken(path.slice(VIEW.length))) + abs.search;
      return new URL(path + abs.search, currentReal()).href;
    } catch(e){ return u; }
  }
  // Turn an Error's .stack into frames. Reading .stack is what actually costs — V8 builds
  // it lazily on first access — so this only ever runs for the one element clicked.
  function framesFromStack(err){
    var out = [];
    try {
      var lines = String((err && err.stack) || '').split('\n');
      for (var i = 0; i < lines.length && out.length < MAX_STACK_FRAMES; i++){
        var m = lines[i].match(/(https?:\/\/[^\s)]+?):(\d+):(\d+)/);
        if (m) out.push({ url: toUpstreamUrl(m[1]), line: +m[2], col: +m[3] });
      }
    } catch(e){}
    return out;
  }
  function reactAdapter(el){
    var fiber = getFiber(el);
    if (!fiber) return null;
    var out = { framework: 'react', version: (window.React && window.React.version) || null,
                ownerChain: [], treePath: [], frames: [], provenance: null, loc: null };

    // "Who created this element" is the _debugOwner chain. fiber.return is "where it sits"
    // in the tree — a different question, and the one the old code answered by mistake.
    var f = fiber, guard = 0;
    while (f && guard++ < 60){
      var name = compName(f.type);
      if (name) out.ownerChain.push({ name: name });
      // React 19 stores an Error captured at the jsxDEV call site.
      if (f._debugStack && !out.frames.length){
        out.frames = framesFromStack(f._debugStack);
        if (out.frames.length) out.provenance = 'react-owner-stack';
      }
      // React <= 18 carried the location directly.
      if (f._debugSource && !out.loc){
        out.loc = { file: f._debugSource.fileName, line: f._debugSource.lineNumber,
                    col: f._debugSource.columnNumber || null };
        out.provenance = out.provenance || 'react-debug-source';
      }
      f = f._debugOwner;
    }
    var t = fiber, tguard = 0;
    while (t && tguard++ < 60){
      var tname = compName(t.type);
      if (tname && out.treePath[out.treePath.length - 1] !== tname) out.treePath.push(tname);
      t = t.return;
    }
    return out;
  }
  function svelteAdapter(el){
    // Svelte writes __svelte_meta as a JS property, not an attribute, and it is exact.
    var node = el, guard = 0;
    while (node && guard++ < 20){
      var meta = node.__svelte_meta;
      if (meta && meta.loc && meta.loc.file){
        return { framework: 'svelte', provenance: 'svelte-meta',
                 loc: { file: meta.loc.file, line: meta.loc.line || null, col: meta.loc.column || null } };
      }
      node = node.parentElement;
    }
    return null;
  }
  function vueAdapter(el){
    var node = el, guard = 0;
    while (node && guard++ < 20){
      var comp = node.__vueParentComponent;
      var type = comp && comp.type;
      if (type && (type.__file || type.__name)){
        // Vue gives the component file but no line — component-level accuracy only.
        return { framework: 'vue', provenance: 'vue-component',
                 loc: type.__file ? { file: type.__file, line: null, col: null } : null,
                 ownerChain: type.__name ? [{ name: type.__name }] : [] };
      }
      node = node.parentElement;
    }
    return null;
  }
  function preactAdapter(el){
    var vnode = el.__v || el.__preactattr_;
    var src = vnode && (vnode.__source || (vnode.props && vnode.props.__source));
    if (!src || !src.fileName) return null;
    return { framework: 'preact', provenance: 'preact-source',
             loc: { file: src.fileName, line: src.lineNumber || null, col: src.columnNumber || null } };
  }
  function angularAdapter(el){
    try {
      if (!window.ng || typeof window.ng.getComponent !== 'function') return null;
      var node = el, guard = 0;
      while (node && guard++ < 20){
        var comp = window.ng.getComponent(node);
        if (comp){
          var name = comp.constructor && comp.constructor.name;
          return { framework: 'angular', provenance: 'angular-component',
                   ownerChain: name ? [{ name: name }] : [] };
        }
        node = node.parentElement;
      }
    } catch(e){}
    return null;
  }

  // ---- Tier 2 — universal DOM creation stacks ----
  //
  // For compile-to-DOM stacks (Svelte, Solid, Qwik, Lit, Angular templates, jQuery,
  // vanilla) the app's own frame IS on the stack when the element is created, and the
  // agent runs before any page script so it can wrap the constructors. VDOM frameworks
  // are the exception — React creates DOM at commit time, long after the render frame
  // popped — which is exactly why Tier 1 exists.
  var creationStacks = (typeof WeakMap === 'function') ? new WeakMap() : null;
  var captureBudget = 200000;
  function recordCreation(node){
    if (!creationStacks || captureBudget <= 0 || !node || node.nodeType !== 1) return node;
    try { creationStacks.set(node, new Error()); captureBudget--; } catch(e){}
    return node;
  }
  function recordSubtree(root){
    if (!root) return root;
    recordCreation(root);
    try {
      if (root.querySelectorAll){
        var kids = root.querySelectorAll('*');
        for (var i = 0; i < kids.length && captureBudget > 0; i++) recordCreation(kids[i]);
      }
    } catch(e){}
    return root;
  }
  function installCreationTracking(){
    if (!creationStacks) return;
    try {
      // Deeper than the default 10: the app's own frame sits below the framework's.
      if (!Error.stackTraceLimit || Error.stackTraceLimit < 40) Error.stackTraceLimit = 40;

      var createElement = Document.prototype.createElement;
      Document.prototype.createElement = function(){ return recordCreation(createElement.apply(this, arguments)); };
      var createElementNS = Document.prototype.createElementNS;
      Document.prototype.createElementNS = function(){ return recordCreation(createElementNS.apply(this, arguments)); };
      // Svelte 5 and Solid clone <template> content rather than building nodes one by one.
      var cloneNode = Node.prototype.cloneNode;
      Node.prototype.cloneNode = function(){ return recordSubtree(cloneNode.apply(this, arguments)); };
      var importNode = Document.prototype.importNode;
      Document.prototype.importNode = function(){ return recordSubtree(importNode.apply(this, arguments)); };
      var insertAdjacentHTML = Element.prototype.insertAdjacentHTML;
      Element.prototype.insertAdjacentHTML = function(pos, html){
        var before = this.children.length;
        var r = insertAdjacentHTML.call(this, pos, html);
        try {
          for (var i = before; i < this.children.length; i++) recordSubtree(this.children[i]);
        } catch(e){}
        return r;
      };
      var innerHTML = Object.getOwnPropertyDescriptor(Element.prototype, 'innerHTML');
      if (innerHTML && innerHTML.set){
        Object.defineProperty(Element.prototype, 'innerHTML', {
          configurable: true, enumerable: innerHTML.enumerable,
          get: function(){ return innerHTML.get.call(this); },
          set: function(v){ innerHTML.set.call(this, v); recordSubtree(this); }
        });
      }
    } catch(e){}
  }
  installCreationTracking();

  function tier2(el){
    if (!creationStacks) return null;
    var node = el, guard = 0;
    while (node && guard++ < 30){
      var err = creationStacks.get(node);
      if (err){
        var frames = framesFromStack(err);
        if (frames.length) return { provenance: 'dom-creation-stack', frames: frames };
      }
      node = node.parentElement;
    }
    return null;
  }

  // Which React build is running — attribution is only possible on a development build.
  function detectBuild(){
    try {
      var hook = window.__REACT_DEVTOOLS_GLOBAL_HOOK__;
      if (hook && hook.renderers && hook.renderers.size){
        var r = hook.renderers.values().next().value;
        if (r) return { version: r.version || null, build: r.bundleType === 1 ? 'development' : 'production' };
      }
    } catch(e){}
    return {};
  }

  // Everything a fixing agent could use to locate this element's source. Returns raw JS
  // frames when it has them; the proxy turns those into file:line via the source map.
  function collectDebug(el){
    var out = { source: null, frames: [], ownerChain: [], treePath: [], candidates: [],
                provenance: 'none', confidence: 'none', runtime: {} };
    if (!el || el.nodeType !== 1) return out;

    var t0 = tier0(el);
    if (t0 && t0.loc && t0.loc.file){
      out.source = { file: t0.loc.file, line: t0.loc.line, col: t0.loc.col };
      out.provenance = t0.provenance;
      out.confidence = t0.loc.line ? 'high' : 'medium';
      if (t0.framework) out.runtime.framework = t0.framework;
    }

    var t1 = reactAdapter(el) || svelteAdapter(el) || vueAdapter(el)
          || preactAdapter(el) || angularAdapter(el);
    if (t1){
      if (t1.framework) out.runtime.framework = out.runtime.framework || t1.framework;
      if (t1.version) out.runtime.version = t1.version;
      if (t1.ownerChain && t1.ownerChain.length) out.ownerChain = t1.ownerChain;
      if (t1.treePath && t1.treePath.length) out.treePath = t1.treePath;
      if (t1.frames && t1.frames.length && !out.frames.length){
        out.frames = t1.frames;
        if (out.provenance === 'none') out.provenance = t1.provenance;
      }
      if (!out.source && t1.loc && t1.loc.file){
        out.source = { file: t1.loc.file, line: t1.loc.line, col: t1.loc.col };
        out.provenance = t1.provenance;
        out.confidence = t1.loc.line ? 'high' : 'medium';
      }
    }

    if (!out.source && !out.frames.length){
      var t2 = tier2(el);
      if (t2){ out.frames = t2.frames; out.provenance = t2.provenance; }
    }

    // Frames still need resolving; say that rather than implying a located file.
    if (!out.source && out.frames.length) out.confidence = 'unresolved';

    var build = detectBuild();
    if (build.version) out.runtime.version = build.version;
    if (build.build) out.runtime.build = build.build;
    return out;
  }
  function onMove(e){
    var el = e.target;
    if (isMarker(el)) return;
    if (el && el !== selOverlay) moveOverlay(el);
  }
  function onClick(e){
    // A marker sits on top of the element it annotates. Let it open its own comment
    // rather than being picked as a new target.
    if (isMarker(e.target)) return;
    e.preventDefault(); e.stopPropagation();
    if (selOverlay) selOverlay.style.display = 'none';
    var el = document.elementFromPoint(e.clientX, e.clientY) || e.target;
    send({
      type: 'selected',
      xpath: getXPath(el),
      selector: cssPath(el),
      meta: describe(el),
      debug: collectDebug(el)
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

  // ---- comment markers ----
  //
  // One numbered yellow pin per submitted comment, parked on the top-left corner of the
  // element it annotates. The parent owns the LIST (it is the side that talks to Ivy and
  // survives a reload); this side owns PLACEMENT, because only the page can resolve an
  // xpath and only the page knows where the element ended up after a re-render. The parent
  // replaces the whole set with 'markers-set' whenever it changes, and again on every load.
  var MARKER_SIZE = 22;
  var MARKER_CSS = 'position:absolute;box-sizing:border-box;display:flex;align-items:center;'
    + 'justify-content:center;width:' + MARKER_SIZE + 'px;height:' + MARKER_SIZE + 'px;'
    + 'margin:0;padding:0;border-radius:50%;background:#facc15;color:#1c1917;'
    + 'border:2px solid #a16207;'
    + 'font:700 12px/1 ui-sans-serif,system-ui,-apple-system,"Segoe UI",Roboto,sans-serif;'
    + 'cursor:pointer;pointer-events:auto;user-select:none;'
    + 'box-shadow:0 1px 4px rgba(0,0,0,0.35);';

  var markerLayer = null;
  var markers = [];

  // Every listener that reacts to a click has to know a marker when it sees one: the
  // recorder must not report it as a page click, the picker must not select it, and the
  // pen must not attribute a stroke to it.
  function isMarker(el){
    try { return !!(el && el.closest && el.closest('[data-wv-marker]')); } catch(e){ return false; }
  }

  function ensureMarkerLayer(){
    if (!markerLayer){
      var layer = document.createElement('div');
      layer.setAttribute('data-wv-marker-layer', '');
      // Zero-sized and click-through: only the pins inside it take pointer events, and it
      // must never add scrollable area to the page. Below the picker overlay and the pen
      // canvas, both of which own the surface while they are active.
      layer.style.cssText = 'position:absolute;left:0;top:0;width:0;height:0;margin:0;'
        + 'padding:0;border:0;pointer-events:none;z-index:2147483644;';
      markerLayer = layer;
    }
    // A framework that replaces <body> wholesale takes the layer with it; put it back
    // rather than losing every pin to a re-render.
    //
    // Parked outside <body>, as a last child of <html>: an extra <div> inside the body
    // shifts the sibling INDEX of every div the page appends after it, and those indices
    // are what an element's xpath is made of. A pin must not move the thing it points at.
    if (!markerLayer.isConnected){
      var host = document.documentElement || document.body;
      if (host) host.appendChild(markerLayer);
    }
    return markerLayer;
  }

  // Held onto until the node leaves the document, so the repositioning that runs on every
  // scroll frame is a rect read rather than an xpath evaluation per pin.
  function resolveMarkerNode(m){
    if (m.el && m.el.isConnected) return m.el;
    m.el = null;
    try {
      if (m.xpath){
        var r = document.evaluate(m.xpath, document, null, 9 /* FIRST_ORDERED_NODE_TYPE */, null);
        if (r && r.singleNodeValue && r.singleNodeValue.nodeType === 1) m.el = r.singleNodeValue;
      }
    } catch(e){}
    // The xpath is exact but brittle across a re-render that reorders siblings; the CSS
    // path is looser and often still finds it.
    if (!m.el){
      try { if (m.selector) m.el = document.querySelector(m.selector); } catch(e){}
    }
    return m.el;
  }

  function positionMarkers(){
    if (!markers.length) return;
    var layer = ensureMarkerLayer();
    var sx = window.scrollX || 0, sy = window.scrollY || 0;

    // A pin is placed in document coordinates, but it is laid out against the layer's
    // containing block — whichever positioned ancestor the page happens to give it. The
    // layer is a zero-sized box at that block's origin, so its own rect is exactly the
    // offset to take back out. Without this, a page as ordinary as `body{position:relative}`
    // with a margin puts every pin off by that margin.
    var origin = { x: 0, y: 0 };
    try {
      var layerRect = layer.getBoundingClientRect();
      origin = { x: layerRect.left + sx, y: layerRect.top + sy };
    } catch(e){}

    for (var i = 0; i < markers.length; i++){
      var m = markers[i];
      var el = resolveMarkerNode(m);
      var rect = null;
      if (el && el.getBoundingClientRect) { try { rect = el.getBoundingClientRect(); } catch(e){} }
      // The element is gone (a route change, a collapsed section) or has no box. Hide the
      // pin rather than dropping it: the comment is still real, and the element usually
      // comes back.
      if (!rect || (!rect.width && !rect.height)){
        m.node.style.display = 'none';
        continue;
      }
      m.node.style.display = 'flex';
      // Straddling the top-left corner, so the pin sits above the element and covers none of
      // its content. Clamped so a pin on a top-row element is not cut off by the viewport.
      m.node.style.left = Math.max(0, Math.round(rect.left + sx - origin.x - MARKER_SIZE / 2)) + 'px';
      m.node.style.top = Math.max(0, Math.round(rect.top + sy - origin.y - MARKER_SIZE / 2)) + 'px';
    }
  }

  var repositionQueued = false;
  function scheduleReposition(){
    if (repositionQueued || !markers.length) return;
    repositionQueued = true;
    var raf = window.requestAnimationFrame || function(fn){ return setTimeout(fn, 16); };
    raf(function(){ repositionQueued = false; positionMarkers(); });
  }

  function onMarkerClick(e){
    e.preventDefault();
    e.stopPropagation();
    send({ type: 'marker-click', id: this.getAttribute('data-wv-marker') });
  }

  function renderMarkers(list){
    var layer = ensureMarkerLayer();
    for (var i = 0; i < markers.length; i++) markers[i].node.remove();
    markers = [];
    var next = Array.isArray(list) ? list : [];
    for (var j = 0; j < next.length; j++){
      var m = next[j];
      if (!m || !m.id) continue;
      var node = document.createElement('div');
      node.setAttribute('data-wv-marker', String(m.id));
      node.setAttribute('role', 'button');
      node.setAttribute('title', m.comment || 'Comment ' + m.number);
      node.textContent = String(m.number == null ? '' : m.number);
      node.style.cssText = MARKER_CSS;
      node.style.display = 'none';   // shown by positionMarkers once it has a box
      node.addEventListener('click', onMarkerClick);
      layer.appendChild(node);
      markers.push({ id: m.id, number: m.number, xpath: m.xpath, selector: m.selector, node: node, el: null });
    }
    positionMarkers();
    trackLayout();
  }

  // Scroll and resize cover the page moving under the pins; this covers the page moving on
  // its own — an accordion opening, a route change, an image finally arriving — none of which
  // any event we can listen for reliably reports. Runs only while there are pins to move.
  var layoutTimer = null;
  function trackLayout(){
    if (markers.length && layoutTimer === null){
      layoutTimer = setInterval(positionMarkers, 500);
    } else if (!markers.length && layoutTimer !== null){
      clearInterval(layoutTimer);
      layoutTimer = null;
    }
  }

  // Positioned in document coordinates, so scrolling alone does not move a pin — except
  // for one anchored inside a position:fixed region, which is why scroll is watched too.
  window.addEventListener('scroll', scheduleReposition, true);
  window.addEventListener('resize', scheduleReposition);
  window.addEventListener('load', scheduleReposition);
  try {
    if (typeof ResizeObserver === 'function'){
      // Late images and web fonts reflow the page long after load; the pins have to follow.
      new ResizeObserver(scheduleReposition).observe(document.documentElement);
    }
  } catch(e){}
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
    for (var i = 0; i < els.length; i++){
      if (els[i] !== drawCanvas && !isMarker(els[i])) return els[i];
    }
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
      debug: collectDebug(el)
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
    // The whole set, every time: the parent owns the list, and a document that just
    // loaded has none, so a diff would need state neither side can trust across a reload.
    else if (d.__proxyCmd === 'markers-set') renderMarkers(d.markers);
  });

  // ---- keep navigations inside view-space ----
  // The service worker is scoped to /__view/, so a navigation to any OTHER path on this
  // origin falls outside its scope, is never intercepted, and is answered by the host Ivy
  // app — the viewer abruptly shows the app's own shell instead of the site. Links in the
  // served HTML were rewritten already; these are the ones the page's scripts create after
  // hydration, with root-relative hrefs. This runs in the bubble phase and bails out if the
  // page already handled the click, so a client-side router keeps doing its own routing.
  document.addEventListener('click', function(e){
    try {
      if (e.defaultPrevented || e.button !== 0) return;
      if (e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
      var a = e.target && e.target.closest ? e.target.closest('a[href]') : null;
      if (!a) return;
      var linkTarget = a.getAttribute('target');
      if (linkTarget && linkTarget !== '_self') return;
      var href = a.getAttribute('href') || '';
      if (!href || /^(#|javascript:|mailto:|tel:|sms:|blob:|data:)/i.test(href)) return;
      var abs = new URL(a.href, location.href);
      if (abs.origin !== location.origin) return;         // cross-origin: the SW maps it
      if (abs.pathname.indexOf('/__view/') === 0) return;  // already in view-space
      var upstream = new URL(abs.pathname + abs.search + abs.hash, currentReal());
      var here = new URL(currentReal());
      // Only the fragment differs: this is an in-page jump, so let the browser do it
      // natively rather than reloading the whole document through the proxy.
      if (upstream.pathname === here.pathname && upstream.search === here.search) return;
      e.preventDefault();
      // Carry this document's token across, or the next page lands in a viewer-less,
      // device-less view-space and the emulated viewport reverts mid-session.
      location.href = VIEW + VIEW_TOKEN + upstream.href;
    } catch(err){}
  });

  // ---- click recorder ----
  // Always emit a rich click event (same element detail the picker produces);
  // the parent decides whether to store it. Capture phase + no preventDefault,
  // so it sees every click and the page keeps working normally.
  document.addEventListener('click', function(e){
    if (selActive) return; // selection mode owns its own clicks
    if (isMarker(e.target)) return; // a pin is ours, not the page's
    var el = e.target;
    if (!el || el.nodeType !== 1) el = (el && el.parentElement) || document.body;
    send({
      type: 'click',
      button: e.button,
      x: e.clientX, y: e.clientY,
      xpath: getXPath(el),
      selector: cssPath(el),
      meta: describe(el),
      debug: collectDebug(el)
    });
  }, true);

  // ---- injected user code goes here (runs in the page context) ----
  // e.g. window.__PROXY_TARGET__ is the real URL of this page.
})();