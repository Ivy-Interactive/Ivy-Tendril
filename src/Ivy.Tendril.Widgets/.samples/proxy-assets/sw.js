// Service Worker proxy.
//
// Registered by the parent app on our origin with scope "/". It intercepts
// every request the iframe makes — relative, root-relative, runtime fetch/XHR,
// webpack chunks, RSC payloads — and routes the ones that belong to proxied
// content through the server proxy endpoint (/__proxy?url=<absolute target>).
//
// Two URL spaces share our origin:
//   * the app itself (parent page, Vite assets) — passed straight through.
//   * "view-space" (/__view/<absolute-url>) — the proxied site. The absolute
//     target is carried unencoded in the path so the browser's own relative-URL
//     resolution keeps working. Root-relative ("/foo") and cross-origin
//     requests are mapped back to the right upstream using the requesting
//     client's view-space URL.

const PROXY = '/__proxy?url='
const VIEW = '/__view/'

// Remember the upstream origin per client, as a fallback for when a client's
// own URL has drifted out of view-space (e.g. a framework pushState to "/foo").
const clientUpstream = new Map()

// Emulated device, set by the parent app (mobile / tablet / null for desktop).
let currentDevice = null

self.addEventListener('install', () => self.skipWaiting())
self.addEventListener('activate', (e) => e.waitUntil(self.clients.claim()))

self.addEventListener('message', (e) => {
  if (e.data && e.data.__proxySetDevice !== undefined) {
    currentDevice = e.data.__proxySetDevice || null
  }
})

// Re-insert the "//" some path normalizers drop after the scheme.
function fixProto(s) {
  return s.replace(/^(https?:)\/(?!\/)/, '$1//')
}

// Extract the absolute target from a view-space URL.
function viewTarget(url) {
  return fixProto(url.pathname.slice(VIEW.length)) + url.search
}

function upstreamOriginFromUrl(href) {
  try {
    const u = new URL(href)
    if (u.pathname.startsWith(VIEW)) {
      return new URL(fixProto(u.pathname.slice(VIEW.length))).origin
    }
  } catch {
    /* ignore */
  }
  return null
}

async function clientUrl(event) {
  const id = event.clientId || event.resultingClientId
  if (id) {
    try {
      const c = await self.clients.get(id)
      if (c) return c.url
    } catch {
      /* ignore */
    }
  }
  return event.request.referrer || ''
}

// ---- HAR network logging -------------------------------------------------

function headersFromObject(obj) {
  return Object.keys(obj || {}).map((k) => ({ name: k, value: String(obj[k]) }))
}
function headersToArray(h) {
  const a = []
  try {
    h.forEach((v, k) => a.push({ name: k, value: v }))
  } catch {
    /* ignore */
  }
  return a
}
function queryString(urlStr) {
  try {
    const a = []
    new URL(urlStr).searchParams.forEach((v, k) => a.push({ name: k, value: v }))
    return a
  } catch {
    return []
  }
}
function decodeMeta(b64) {
  try {
    const bytes = Uint8Array.from(atob(b64), (c) => c.charCodeAt(0))
    return JSON.parse(new TextDecoder().decode(bytes))
  } catch {
    return null
  }
}
async function broadcast(message) {
  const clients = await self.clients.matchAll({ includeUncontrolled: true, type: 'window' })
  clients.forEach((c) => c.postMessage(message))
}

// Build a HAR 1.2 entry and send it to the parent app.
function emitEntry(o) {
  broadcast({
    __proxyNet: true,
    entry: {
      startedDateTime: o.startISO,
      time: Math.round(o.time),
      request: {
        method: o.method,
        url: o.url,
        httpVersion: 'HTTP/1.1',
        cookies: [],
        headers: o.reqHeaders,
        queryString: queryString(o.url),
        headersSize: -1,
        bodySize: o.reqBodySize,
      },
      response: {
        status: o.status,
        statusText: o.statusText || '',
        httpVersion: 'HTTP/1.1',
        cookies: [],
        headers: o.resHeaders,
        content: { size: o.size, mimeType: o.mimeType || '' },
        redirectURL: '',
        headersSize: -1,
        bodySize: o.size,
      },
      cache: {},
      timings: { send: 0, wait: Math.round(o.time), receive: 0 },
      _resourceType: o.resourceType || '',
    },
  })
}

// Fetch an absolute target through the server proxy. Records the upstream
// origin for this client, times the request, and emits a HAR entry.
async function proxyFetch(req, target, event) {
  const absolute = fixProto(target)
  try {
    const id = event.resultingClientId || event.clientId
    if (id) clientUpstream.set(id, new URL(absolute).origin)
  } catch {
    /* ignore */
  }

  const init = { method: req.method, headers: req.headers, redirect: 'follow' }
  let reqBodySize = 0
  if (req.method !== 'GET' && req.method !== 'HEAD') {
    const ab = await req.arrayBuffer()
    reqBodySize = ab.byteLength
    init.body = ab
  }

  const reqHeaders = headersToArray(req.headers)
  const resourceType = req.destination || (req.mode === 'navigate' ? 'document' : '')
  const t0 = Date.now()
  const startISO = new Date(t0).toISOString()

  const devParam = currentDevice ? '&dev=' + encodeURIComponent(currentDevice) : ''
  let res
  try {
    res = await fetch(PROXY + encodeURIComponent(absolute) + devParam, init)
  } catch (err) {
    emitEntry({
      startISO,
      time: Date.now() - t0,
      method: req.method,
      url: absolute,
      reqHeaders,
      reqBodySize,
      status: 0,
      statusText: '(failed) ' + (err && err.message),
      resHeaders: [],
      size: -1,
      mimeType: '',
      resourceType,
    })
    throw err
  }

  const time = Date.now() - t0
  const meta = decodeMeta(res.headers.get('x-proxy-meta') || '')
  const upstreamHeaders = meta && meta.headers ? meta.headers : null
  const base = {
    startISO,
    time,
    method: req.method,
    url: absolute,
    reqHeaders,
    reqBodySize,
    status: (meta && meta.status) || res.status,
    statusText: (meta && meta.statusText) || res.statusText || '',
    resHeaders: upstreamHeaders ? headersFromObject(upstreamHeaders) : headersToArray(res.headers),
    mimeType: res.headers.get('content-type') || '',
    resourceType,
  }

  // Prefer the real upstream content-length; otherwise measure the body.
  const cl = upstreamHeaders && upstreamHeaders['content-length']
  if (cl) {
    emitEntry({ ...base, size: parseInt(cl, 10) })
  } else {
    res
      .clone()
      .arrayBuffer()
      .then((b) => emitEntry({ ...base, size: b.byteLength }))
      .catch(() => emitEntry({ ...base, size: -1 }))
  }

  return res
}

self.addEventListener('fetch', (event) => {
  const req = event.request
  const url = new URL(req.url)
  const sameOrigin = url.origin === self.location.origin

  // Never touch the proxy/capture helper endpoints, the service worker itself, or any
  // of the host Ivy app's own endpoints (/ivy/*). Returning without respondWith leaves
  // these completely to the browser.
  if (
    sameOrigin &&
    (url.pathname.startsWith('/__proxy') ||
      url.pathname.startsWith('/__capture') ||
      url.pathname.startsWith('/__lib') ||
      url.pathname === '/sw.js' ||
      url.pathname.startsWith('/ivy/'))
  )
    return

  // Explicit view-space request (the iframe document, or a resolved relative resource).
  if (sameOrigin && url.pathname.startsWith(VIEW)) {
    event.respondWith(proxyFetch(req, viewTarget(url), event))
    return
  }

  // For everything else, only intervene when the request clearly originates from
  // proxied (view-space) content. This decision is SYNCHRONOUS: we must NOT call
  // event.respondWith() for genuine host-app requests, otherwise a failed passthrough
  // fetch would surface as a network error and break the host app (and re-fetching a
  // navigation request inside the SW can itself fail). When in doubt, do nothing.
  let upstream = upstreamOriginFromUrl(req.referrer || '')
  if (!upstream && event.clientId) upstream = clientUpstream.get(event.clientId) || null
  if (!upstream) return // host-app request -> let the browser handle it natively

  const target = sameOrigin ? upstream + url.pathname + url.search : url.href

  // A navigation (link click / location change) inside proxied content: bounce it into
  // view-space so the document URL keeps carrying the target.
  if (req.mode === 'navigate') {
    event.respondWith(
      Response.redirect(new URL(VIEW + target, self.location.origin).href, 302),
    )
    return
  }
  event.respondWith(proxyFetch(req, target, event))
})
