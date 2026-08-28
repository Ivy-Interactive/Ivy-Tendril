// Service Worker proxy.
//
// Registered by the parent app with scope "/__view/", so it controls the proxied
// iframe and NOTHING else — the host Ivy app's own page is out of scope and its
// requests never reach this worker. Every request the iframe makes (relative,
// root-relative, runtime fetch/XHR, webpack chunks, RSC payloads) is intercepted,
// because a worker sees the traffic of the clients it controls whatever the URL,
// and routed through the server proxy endpoint (/__proxy?url=<absolute target>).
//
// "View-space" is /__view/<absolute-url>: the absolute target is carried unencoded
// in the path so the browser's own relative-URL resolution keeps working. Root-
// relative ("/foo") and cross-origin requests are mapped back to the right upstream
// using the requesting client's view-space URL.

const PROXY = '/__proxy?url='
const VIEW = '/__view/'

// ---- durable state -------------------------------------------------------
//
// A service worker is torn down whenever it goes idle (~30s) and restarted on the
// next event with a fresh module scope. Anything left in a plain variable reverts
// silently on that restart: device emulation drops back to desktop mid-session, and
// requests from proxied content lose the upstream they belong to. So the state that
// has to outlive a restart lives in the Cache API, which does.

const STATE_CACHE = 'webviewer-proxy-state'
const STATE_KEY = '/__proxy-state'

// device:     mobile / tablet / null for desktop.
// clients:    clientId -> the document's own upstream origin. Per document rather than one
//             global "last origin used", so a page pulling assets from a second host never
//             has its requests answered by whichever host was proxied most recently.
// assetHosts: document origin -> other origins that document has loaded assets from, keyed
//             by SITE rather than by client so it outlives navigation, client-id churn and
//             worker restarts. This is what lets a root-relative bundle URL be recovered.
const state = { device: null, clients: {}, assetHosts: {} }

const clientInfo = new Map()
const assetHosts = new Map()
const MAX_TRACKED_CLIENTS = 64
const MAX_TRACKED_SITES = 32
const MAX_HOSTS_PER_SITE = 8

// Every origin proxied recently, newest last. Site-keyed knowledge is the precise answer,
// but its bookkeeping churns as the viewer is navigated around; this survives that and is
// only ever consulted after a 404 that would otherwise be returned as-is.
const recentOrigins = []
const MAX_RECENT_ORIGINS = 12

function rememberOrigin(origin) {
  if (!origin) return
  const at = recentOrigins.indexOf(origin)
  if (at !== -1) recentOrigins.splice(at, 1)
  recentOrigins.push(origin)
  if (recentOrigins.length > MAX_RECENT_ORIGINS) recentOrigins.shift()
}

function rememberClient(id, origin) {
  if (!id || !origin || clientInfo.get(id) === origin) return
  // The first origin seen for a client is its document's: that navigation created it.
  if (!clientInfo.has(id)) {
    clientInfo.set(id, origin)
    while (clientInfo.size > MAX_TRACKED_CLIENTS) {
      clientInfo.delete(clientInfo.keys().next().value)
    }
    state.clients = Object.fromEntries(clientInfo)
    persistState()
  }
}

function rememberAssetHost(documentOrigin, assetOrigin) {
  if (!documentOrigin || !assetOrigin || documentOrigin === assetOrigin) return
  let hosts = assetHosts.get(documentOrigin)
  if (!hosts) {
    hosts = []
    assetHosts.set(documentOrigin, hosts)
    while (assetHosts.size > MAX_TRACKED_SITES) {
      assetHosts.delete(assetHosts.keys().next().value)
    }
  }
  if (hosts.includes(assetOrigin)) return
  hosts.push(assetOrigin)
  if (hosts.length > MAX_HOSTS_PER_SITE) hosts.shift()
  state.assetHosts = Object.fromEntries(assetHosts)
  persistState()
}

function clientOrigin(id) {
  return (id && clientInfo.get(id)) || null
}

let deviceSetThisRun = false
let restorePromise = null

function restoreState() {
  if (!restorePromise) {
    restorePromise = caches
      .open(STATE_CACHE)
      .then((cache) => cache.match(STATE_KEY))
      .then((res) => (res ? res.json() : null))
      .then((saved) => {
        if (!saved) return
        // Anything set since this restart wins over what was persisted before it.
        if (!deviceSetThisRun) state.device = saved.device ?? null
        for (const [id, origin] of Object.entries(saved.clients || {})) {
          if (!clientInfo.has(id) && typeof origin === 'string') clientInfo.set(id, origin)
        }
        for (const [site, hosts] of Object.entries(saved.assetHosts || {})) {
          if (!assetHosts.has(site) && Array.isArray(hosts)) assetHosts.set(site, hosts.slice())
        }
        state.clients = Object.fromEntries(clientInfo)
        state.assetHosts = Object.fromEntries(assetHosts)
      })
      .catch(() => {
        /* a cold cache is not an error; carry on with defaults */
      })
  }
  return restorePromise
}
restoreState()

let persistQueued = false
function persistState() {
  if (persistQueued) return
  persistQueued = true
  Promise.resolve().then(async () => {
    persistQueued = false
    try {
      const cache = await caches.open(STATE_CACHE)
      await cache.put(
        STATE_KEY,
        new Response(JSON.stringify(state), { headers: { 'content-type': 'application/json' } }),
      )
    } catch {
      /* best effort — a lost write only costs us one restart's worth of state */
    }
  })
}

self.addEventListener('install', () => self.skipWaiting())
self.addEventListener('activate', (e) => e.waitUntil(self.clients.claim()))

self.addEventListener('message', (e) => {
  if (e.data && e.data.__proxySetDevice !== undefined) {
    state.device = e.data.__proxySetDevice || null
    deviceSetThisRun = true
    persistState()
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

// Statuses the fetch spec forbids from carrying a body.
const NULL_BODY_STATUS = [101, 103, 204, 205, 304]

// Hand the response back under the URL the page ASKED for rather than the one we
// fetched from.
//
// A module script's imports resolve against the URL of the response that delivered it.
// Returning the /__proxy?url=… response verbatim makes every ES module believe it lives
// at /__proxy — a path with no directory — so `import './chunks/x.js'` collapses to the
// origin root and the upstream's own path is lost. Re-wrapping the body produces a
// synthetic response with no URL of its own, and the browser then falls back to the
// request URL (/__view/<absolute>), which resolves correctly. It also keeps /__proxy out
// of the Referer header, so the upstream lookup above sees a view-space URL.
function asViewSpaceResponse(res) {
  const body = NULL_BODY_STATUS.includes(res.status) ? null : res.body
  return new Response(body, {
    status: res.status,
    statusText: res.statusText,
    headers: res.headers,
  })
}

// Fetch an absolute target through the server proxy. Records the upstream
// origin for this client, times the request, and emits a HAR entry.
async function proxyFetch(req, target, event) {
  // Safe to await here — this already runs inside respondWith's promise, so the
  // emulated device is whatever the parent last set even across a worker restart.
  await restoreState()

  const absolute = fixProto(target)
  try {
    const id = event.resultingClientId || event.clientId
    const origin = new URL(absolute).origin
    rememberClient(id, origin)
    rememberAssetHost(clientOrigin(id), origin)
    rememberOrigin(origin)
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

  const devParam = state.device ? '&dev=' + encodeURIComponent(state.device) : ''
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

  return asViewSpaceResponse(res)
}

// Turn a request from proxied content into its absolute upstream target.
function upstreamTarget(url, upstream) {
  return url.origin === self.location.origin ? upstream + url.pathname + url.search : url.href
}

async function respondForUpstream(req, url, upstream, event) {
  // A navigation (link click / location change) inside proxied content: bounce it into
  // view-space so the document URL keeps carrying the target.
  if (req.mode === 'navigate') {
    const target = upstreamTarget(url, upstream)
    return Response.redirect(new URL(VIEW + target, self.location.origin).href, 302)
  }

  const retryable = url.origin === self.location.origin && (req.method === 'GET' || req.method === 'HEAD')
  const first = await proxyFetch(retryable ? req.clone() : req, upstreamTarget(url, upstream), event)
  if (first.status !== 404 || !retryable) return first

  // Sites that serve their bundle from a second host (theguardian.com + assets.guim.co.uk,
  // github.com + githubassets.com) emit root-relative chunk URLs that only resolve against
  // that other host — the bundler's public path is baked in and means nothing here. We
  // already know which hosts this site loads assets from, so try those before failing.
  const siteHosts = assetHosts.get(upstream) || []
  const alternates = []
  for (const origin of [...siteHosts, ...[...recentOrigins].reverse()]) {
    if (origin !== upstream && !alternates.includes(origin)) alternates.push(origin)
  }
  for (const origin of alternates) {
    const retry = await proxyFetch(req.clone(), upstreamTarget(url, origin), event)
    if (retry.status < 400) return retry
  }
  return first
}

// Nothing synchronous identified the upstream. Only proxied documents are in this
// worker's scope, so this IS proxied content whose referrer was stripped — wait for the
// persisted state to come back rather than guessing, and pass through if it never does.
// Last resort: read the upstream off the documents this worker actually controls. Only
// proxied pages are in scope, so any controlled window sitting in view-space names the
// site being viewed. This is what rescues speculative requests — <link rel=prefetch> and
// friends arrive with an empty clientId and a referrer that referrer-policy has already
// trimmed down to the bare origin, so nothing else identifies where they belong.
async function upstreamFromControlledClients(event) {
  try {
    const clients = await self.clients.matchAll({ type: 'window' })

    // The requesting client is the authoritative answer whenever we can identify it. Without
    // this, a link click in a proxied page that we cannot otherwise place falls through to the
    // host app and the viewer silently replaces the site with the Ivy shell.
    const own = clients.find((c) => c.id === event.clientId || c.id === event.resultingClientId)
    if (own) {
      const ownOrigin = upstreamOriginFromUrl(own.url)
      if (ownOrigin) return ownOrigin
    }

    const origins = new Set()
    for (const client of clients) {
      const origin = upstreamOriginFromUrl(client.url)
      if (origin) origins.add(origin)
    }
    // Otherwise answer only when it is unambiguous. During a navigation the outgoing document
    // is briefly still alive, and two viewers can be open at once — picking one of several
    // origins would hand one page's request to a different site.
    return origins.size === 1 ? [...origins][0] : null
  } catch {
    return null
  }
}

async function resolveThenFetch(req, url, event) {
  await restoreState()
  const upstream =
    clientOrigin(event.clientId) ||
    clientOrigin(event.resultingClientId) ||
    (await upstreamFromControlledClients(event))
  if (!upstream) return fetch(req)
  return respondForUpstream(req, url, upstream, event)
}

self.addEventListener('fetch', (event) => {
  const req = event.request
  const url = new URL(req.url)
  const sameOrigin = url.origin === self.location.origin

  // The proxy's own helper endpoints are served by the Ivy app, not by upstream. The
  // proxied page asks for /__lib itself (the agent imports snapDOM from there), so this
  // guard is load-bearing. Returning without respondWith leaves them to the browser.
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

  // Everything else comes from a proxied document — the host app is out of scope — so it
  // only remains to work out which upstream it belongs to. The referrer is checked first
  // because it names the origin of the *importing resource*, which is what a root-relative
  // URL in a second-host asset bundle actually means.
  const upstream = upstreamOriginFromUrl(req.referrer || '') || clientOrigin(event.clientId)

  event.respondWith(
    upstream ? respondForUpstream(req, url, upstream, event) : resolveThenFetch(req, url, event),
  )
})
