import React, { useCallback, useEffect, useRef, useState } from "react";
import "./web-viewer.css";
import { getHeight, getWidth } from "../styles";

// ---------------------------------------------------------------------------
// Types

type EventHandler = (eventName: string, id: string, args: unknown[]) => void;
type StreamSubscriber = (
  streamId: string,
  onData: (data: unknown) => void,
) => () => void;

interface WebViewerProps {
  id: string;
  width?: string;
  height?: string;
  url?: string;
  device?: string; // "Desktop" | "Mobile" | "Tablet" (omitted when Desktop)
  commands?: { id: string };
  subscribeToStream?: StreamSubscriber;
  eventHandler?: EventHandler;
  events?: string[];
}

interface PendingComment {
  id: number;
  xpath: string;
  selector: string;
  meta: { tag?: string; text?: string } | null;
  debug: DebugPayload | null;
  resolving: boolean;
}

// Source attribution collected in the page (see proxy-assets/agent.js). `frames` are raw
// JS positions inside the served bundle; /__resolve turns those into original file/line.
interface DebugPayload {
  source?: { file: string; line: number | null; col: number | null } | null;
  frames?: { url: string; line: number; col: number }[];
  provenance?: string;
  confidence?: string;
  codeFrame?: string | null;
  ownerChain?: { name?: string }[];
  [key: string]: unknown;
}

// "src/App.tsx:59:12" — whatever precision the tier that answered could give.
function sourceLabel(debug: DebugPayload | null | undefined): string | null {
  const source = debug?.source;
  if (!source?.file) return null;
  if (source.line == null) return source.file;
  return source.col == null ? `${source.file}:${source.line}` : `${source.file}:${source.line}:${source.col}`;
}

// ---------------------------------------------------------------------------
// Helpers (ported from the original WebViewer2 App.jsx)

const DEVICES: Record<string, { w: number | null; h: number | null }> = {
  desktop: { w: null, h: null },
  mobile: { w: 390, h: 844 },
  tablet: { w: 820, h: 1180 },
};

function normalizeUrl(input: string): string {
  const trimmed = (input || "").trim();
  if (!trimmed) return "";
  if (/^https?:\/\//i.test(trimmed)) return trimmed;
  return "https://" + trimmed;
}

const VIEW_PREFIX = "/__view/";

function toViewUrl(realUrl: string): string {
  return `/__view/${realUrl}`;
}

// Short quoted snippet of an element's text, for identifying it at a glance.
function quote(text: string, max = 60): string {
  const collapsed = text.replace(/\s+/g, " ").trim();
  if (!collapsed) return "";
  return `“${collapsed.length > max ? collapsed.slice(0, max) + "…" : collapsed}”`;
}

// Loose comparison so trailing-slash differences don't create dupe history.
function sameUrl(a?: string | null, b?: string | null): boolean {
  if (!a || !b) return false;
  return a.replace(/\/+$/, "") === b.replace(/\/+$/, "");
}

// Resolve when an element is PICKED, not on hover: selection only happens in Select mode,
// so this is still one round trip per comment — and it lands while the comment box is open,
// which is what lets the box show where the element came from. The collector only has
// positions inside the served bundle; the proxy owns the source maps, so it is the only
// side that can turn those into a file an agent can open.
async function resolveSource(debug: DebugPayload | null): Promise<DebugPayload | null> {
  if (!debug?.frames?.length || debug.source) return debug;
  try {
    const response = await fetch("/__resolve", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ frames: debug.frames }),
    });
    if (!response.ok) return debug;
    const resolved = await response.json();
    if (!resolved?.source) return debug;
    return {
      ...debug,
      source: resolved.source,
      codeFrame: resolved.codeFrame,
      candidates: resolved.candidates,
      resolvedFrames: resolved.frames,
      confidence: resolved.confidence ?? debug.confidence,
    };
  } catch {
    return debug; // attribution is a bonus; never block the comment on it
  }
}

// ---------------------------------------------------------------------------
// Proxy service worker.
//
// Scoped to view-space, so it controls the proxied iframe and nothing else — the host
// app's own requests never reach it. The consequence is that THIS page is never
// controlled: navigator.serviceWorker.ready never resolves and .controller stays null,
// so both readiness and messaging have to go through the registration object.
//
// The registration is shared by every mounted WebViewer on the page and torn down when
// the last one unmounts, so a viewer never leaves a worker behind on the origin.

const SW_URL = "/sw.js";
const SW_SCOPE = "/__view/";

let proxyWorker: Promise<ServiceWorkerRegistration> | null = null;
let proxyWorkerUsers = 0;

function workerActivated(registration: ServiceWorkerRegistration): Promise<void> {
  if (registration.active) return Promise.resolve();
  const worker = registration.installing ?? registration.waiting;
  if (!worker) return Promise.resolve();
  return new Promise((resolve) => {
    const onStateChange = () => {
      if (worker.state === "activated" || worker.state === "redundant") {
        worker.removeEventListener("statechange", onStateChange);
        resolve();
      }
    };
    worker.addEventListener("statechange", onStateChange);
  });
}

// Earlier builds registered this same script at the origin root, where it intercepted
// every host-app request. That registration outlives an upgrade, so retire it — matched
// on our own script URL so a host app's unrelated worker is left alone.
async function removeRootScopedWorker(): Promise<void> {
  try {
    const registrations = await navigator.serviceWorker.getRegistrations();
    await Promise.all(
      registrations
        .filter((r) => {
          const script = r.active?.scriptURL ?? r.waiting?.scriptURL ?? r.installing?.scriptURL;
          return (
            new URL(r.scope).pathname === "/" &&
            !!script &&
            new URL(script).pathname === SW_URL
          );
        })
        .map((r) => r.unregister()),
    );
  } catch {
    // Nothing to clean up, or the browser refused — the new registration still stands.
  }
}

let releaseTimer: ReturnType<typeof setTimeout> | null = null;

function acquireProxyWorker(): Promise<ServiceWorkerRegistration> {
  proxyWorkerUsers++;
  if (releaseTimer !== null) {
    clearTimeout(releaseTimer);
    releaseTimer = null;
  }
  proxyWorker ??= removeRootScopedWorker()
    .then(() => navigator.serviceWorker.register(SW_URL, { scope: SW_SCOPE }))
    .then(async (registration) => {
      await workerActivated(registration);
      return registration;
    });
  return proxyWorker;
}

function releaseProxyWorker(): void {
  proxyWorkerUsers = Math.max(0, proxyWorkerUsers - 1);
  if (proxyWorkerUsers > 0) return;

  // Tear down on a delay. A remount — Ivy re-creating the view — releases and re-acquires
  // within the same tick, and unregistering immediately would kill the very registration
  // the remount just adopted. The unregister is async, so it lands *after* the new mount
  // and leaves the viewer with no proxy at all: every root-relative request from the
  // proxied page then falls through to the host app as a 404.
  if (releaseTimer !== null) clearTimeout(releaseTimer);
  releaseTimer = setTimeout(() => {
    releaseTimer = null;
    if (proxyWorkerUsers > 0) return;
    const pending = proxyWorker;
    proxyWorker = null;
    pending?.then((registration) => registration.unregister()).catch(() => {});
  }, 5000);
}

// ---------------------------------------------------------------------------

export const WebViewer: React.FC<WebViewerProps> = ({
  id,
  width,
  height,
  url,
  device,
  commands,
  subscribeToStream,
  eventHandler,
  events = [],
}) => {
  const devKey = (device || "desktop").toLowerCase();
  const dev = DEVICES[devKey] || DEVICES.desktop;

  const initialUrl = url ? normalizeUrl(url) : null;
  const [history, setHistory] = useState<string[]>(initialUrl ? [initialUrl] : []);
  const [index, setIndex] = useState(initialUrl ? 0 : -1);
  const [reloadKey, setReloadKey] = useState(0);
  // What the iframe is actually pointed at. Kept apart from the history entry so that a
  // location the PAGE reports (an in-page anchor, a client-side route change) updates the
  // address bar and history without re-pointing the iframe — re-pointing would remount it
  // and reload the whole document, throwing away the very navigation being reported.
  const [frameSrc, setFrameSrc] = useState<string | null>(initialUrl);
  const [swReady, setSwReady] = useState(false);
  const [pending, setPending] = useState<PendingComment | null>(null);
  const [comment, setComment] = useState("");

  const frameRef = useRef<HTMLIFrameElement>(null);
  const commentRef = useRef<HTMLTextAreaElement>(null);
  const registrationRef = useRef<ServiceWorkerRegistration | null>(null);
  const selectionSeq = useRef(0);
  const codeRef = useRef<HTMLPreElement>(null);

  const currentUrl = index >= 0 ? history[index] : null;
  const canGoBack = index > 0;
  const canGoForward = index < history.length - 1;

  // Canonical navigation state for the mount-once handlers. The mutators below are the
  // only writers; component state mirrors it purely to trigger re-renders.
  const navRef = useRef<{ history: string[]; index: number }>({
    history: initialUrl ? [initialUrl] : [],
    index: initialUrl ? 0 : -1,
  });

  // Keep the latest callback props in a ref so `emit` is STABLE. Ivy passes a fresh
  // eventHandler/events identity every render; if emit changed each render, the
  // navigate-emit effect below would re-fire on every render and flood NavigateEvents
  // (which the host echoes back into Url, stomping any typed URL).
  const cbRef = useRef({ eventHandler, events, id });
  cbRef.current = { eventHandler, events, id };
  const emit = useCallback((kind: string, fields: Record<string, unknown>) => {
    const { eventHandler: eh, events: ev, id: wid } = cbRef.current;
    if (!eh) return;
    if (ev.length && !ev.includes("OnEvent")) return;
    // `kind` first so System.Text.Json reads the polymorphic discriminator before
    // materializing the derived type.
    eh("OnEvent", wid, [{ kind, ...fields }]);
  }, []);

  // ---- navigation ---------------------------------------------------------
  const applyNav = useCallback(
    (nextHistory: string[], nextIndex: number, loadFrame = true) => {
      navRef.current = { history: nextHistory, index: nextIndex };
      setHistory(nextHistory);
      setIndex(nextIndex);
      if (loadFrame) setFrameSrc(nextIndex >= 0 ? nextHistory[nextIndex] : null);
    },
    [],
  );

  const navigate = useCallback(
    (raw: string) => {
      const next = normalizeUrl(raw);
      if (!next) return;
      const { history: h, index: i } = navRef.current;
      const cur = i >= 0 ? h[i] : null;
      if (sameUrl(next, cur)) return;
      const newHistory = h.slice(0, i + 1).concat(next);
      applyNav(newHistory, newHistory.length - 1);
    },
    [applyNav],
  );

  const goBack = useCallback(() => {
    const { history: h, index: i } = navRef.current;
    if (i > 0) applyNav(h, i - 1);
  }, [applyNav]);

  const goForward = useCallback(() => {
    const { history: h, index: i } = navRef.current;
    if (i < h.length - 1) applyNav(h, i + 1);
  }, [applyNav]);

  const reload = useCallback(() => setReloadKey((k) => k + 1), []);

  const postToFrame = useCallback((msg: unknown) => {
    frameRef.current?.contentWindow?.postMessage(msg, "*");
  }, []);

  // A hydrated page can navigate itself with script — a nav button calling location.assign,
  // a router falling back to a hard navigation — to a path outside view-space. The worker is
  // scoped to /__view/, so such a navigation is out of its scope entirely, never intercepted,
  // and answered by the host Ivy app: the viewer abruptly shows the app's own shell instead
  // of the site. View-space is same-origin, so we can see where the frame ended up and put it
  // back. Costs one extra load on the rare escape, and self-heals whatever caused it.
  const healEscapedFrame = useCallback(() => {
    try {
      const location = frameRef.current?.contentWindow?.location;
      if (!location) return;
      if (location.protocol === "about:") return; // the blank frame before the first load
      if (location.pathname.startsWith(VIEW_PREFIX)) return;

      const { history: h, index: i } = navRef.current;
      const current = i >= 0 ? h[i] : null;
      if (!current) return;

      const upstream = new URL(location.pathname + location.search + location.hash, current);
      location.replace(toViewUrl(upstream.href));
    } catch {
      // Reading the frame threw, so it is no longer on our origin: the page navigated itself
      // somewhere else entirely and there is nothing left to read or repair. Some sites do
      // this on purpose — nextjs.org ships an "enforceVercelOrigin" guard that rewrites
      // location.hostname whenever the page is not served from its own domain — and the
      // browser then usually blocks the framed result, leaving a blank error page. Say so,
      // rather than letting the viewer sit there looking broken.
      emit("console", {
        level: "error",
        text:
          "The page navigated itself to another origin and can no longer be proxied. " +
          "Some sites enforce their own domain and refuse to run anywhere else.",
        stack: null,
      });
    }
  }, [emit]);

  // Navigate when the `url` prop changes to something we're not already showing
  // (this also makes syncing the prop from NavigateEvent a no-op — no loop).
  useEffect(() => {
    if (!url) return;
    navigate(normalizeUrl(url)); // navigate() dedupes against the current page
  }, [url, navigate]);

  // Report URL/back-forward state to Ivy whenever the visible entry changes.
  useEffect(() => {
    if (currentUrl) {
      emit("navigate", { url: currentUrl, canGoBack, canGoForward });
    }
  }, [currentUrl, canGoBack, canGoForward, emit]);

  // ---- service worker -----------------------------------------------------
  useEffect(() => {
    if (!("serviceWorker" in navigator)) {
      emit("console", {
        level: "error",
        text: "Service Worker not supported — the proxy cannot run.",
        stack: null,
      });
      return;
    }
    let cancelled = false;
    acquireProxyWorker()
      .then((registration) => {
        if (cancelled) return;
        registrationRef.current = registration;
        setSwReady(true);
      })
      .catch((err) =>
        emit("console", {
          level: "error",
          text: "SW registration failed: " + (err?.message || String(err)),
          stack: null,
        }),
      );
    return () => {
      cancelled = true;
      registrationRef.current = null;
      releaseProxyWorker();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Tell the SW which device to emulate, then reload so the new UA takes effect. The
  // worker does not control this page, so the message goes to the registration's worker
  // rather than to navigator.serviceWorker.controller (which is always null here).
  useEffect(() => {
    if (!swReady) return;
    const registration = registrationRef.current;
    const worker = registration?.active ?? registration?.waiting;
    worker?.postMessage({
      __proxySetDevice: devKey === "desktop" ? null : devKey,
    });
  }, [devKey, swReady]);

  const deviceInit = useRef(true);
  useEffect(() => {
    if (deviceInit.current) {
      deviceInit.current = false;
      return;
    }
    setReloadKey((k) => k + 1);
  }, [devKey]);

  // ---- screenshot save ----------------------------------------------------
  const saveCapture = useCallback(
    async (dataUrl: string, mode: string, w: number, h: number) => {
      try {
        const res = await fetch("/__capture", {
          method: "POST",
          headers: { "content-type": "application/json" },
          body: JSON.stringify({ dataUrl, name: mode }),
        });
        if (!res.ok) throw new Error(await res.text());
        const saved = await res.json();
        emit("capture", {
          url: saved.url,
          path: saved.path,
          width: w,
          height: h,
          mode,
        });
      } catch (err) {
        emit("console", {
          level: "error",
          text: "Capture save failed: " + ((err as Error)?.message || String(err)),
          stack: null,
        });
      }
    },
    [emit],
  );

  // ---- messages from the iframe agent ------------------------------------
  useEffect(() => {
    function onMessage(e: MessageEvent) {
      const data = e.data;
      if (!data || data.__proxy !== true) return;

      switch (data.type) {
        case "console":
          emit("console", {
            level: data.level || "log",
            text: data.text || "",
            stack: data.stack ?? null,
          });
          return;
        case "click": {
          const meta = data.meta || {};
          emit("click", {
            tag: meta.tag || "",
            text: meta.text || null,
            xpath: data.xpath || "",
            selector: data.selector || "",
            button: data.button ?? 0,
            x: data.x ?? 0,
            y: data.y ?? 0,
            debugJson: data.debug ? JSON.stringify(data.debug) : null,
          });
          return;
        }
        case "draw": {
          const pts = data.points || [];
          if (!pts.length) return;
          emit("draw", { pointCount: pts.length, pointsJson: JSON.stringify(pts) });
          return;
        }
        case "selected": {
          // Internal: open the widget's own comment overlay. Not surfaced as an event.
          const selectionId = ++selectionSeq.current;
          const picked = (data.debug as DebugPayload) || null;
          const needsResolve = !!picked?.frames?.length && !picked.source;
          setPending({
            id: selectionId,
            xpath: data.xpath,
            selector: data.selector,
            meta: data.meta || {},
            debug: picked,
            resolving: needsResolve,
          });
          setComment("");
          if (needsResolve) {
            // Discard a late answer if the user has already picked something else.
            void resolveSource(picked).then((enriched) =>
              setPending((prev) =>
                prev && prev.id === selectionId
                  ? { ...prev, debug: enriched, resolving: false }
                  : prev,
              ),
            );
          }
          return;
        }
        case "select-cancelled":
          setPending(null);
          return;
        case "capture-result":
          saveCapture(data.dataUrl, data.mode, data.w, data.h);
          return;
        case "capture-error":
          emit("console", {
            level: "error",
            text: "Capture failed: " + (data.message || "unknown error"),
            stack: null,
          });
          return;
        case "location": {
          const reported = data.url;
          const { history: h, index: i } = navRef.current;
          const cur = i >= 0 ? h[i] : null;
          if (!reported || sameUrl(reported, cur)) return;
          const newHistory = h.slice(0, i + 1).concat(reported);
          // The page is already showing this; only record it.
          applyNav(newHistory, newHistory.length - 1, false);
          return;
        }
        default:
          return;
      }
    }
    window.addEventListener("message", onMessage);
    return () => window.removeEventListener("message", onMessage);
  }, [emit, saveCapture, applyNav]);

  // ---- HAR network entries broadcast by the service worker ----------------
  useEffect(() => {
    if (!("serviceWorker" in navigator)) return;
    function onMsg(e: MessageEvent) {
      const d = e.data;
      if (!d || !d.__proxyNet || !d.entry) return;
      const entry = d.entry;
      emit("http", {
        url: entry.request?.url || "",
        method: entry.request?.method || "",
        status: entry.response?.status ?? 0,
        resourceType: entry._resourceType || "",
        size: entry.response?.content?.size ?? -1,
        time: entry.time ?? 0,
      });
    }
    navigator.serviceWorker.addEventListener("message", onMsg);
    return () => navigator.serviceWorker.removeEventListener("message", onMsg);
  }, [emit]);

  // ---- imperative command stream (Ivy -> widget) --------------------------
  const actionsRef = useRef({ reload, goBack, goForward, postToFrame });
  actionsRef.current = { reload, goBack, goForward, postToFrame };

  useEffect(() => {
    if (!commands?.id || !subscribeToStream) return;
    const unsubscribe = subscribeToStream(commands.id, (raw) => {
      const cmd = (raw || {}) as { action?: string; mode?: string; enabled?: boolean };
      const a = actionsRef.current;
      switch (cmd.action) {
        case "reload":
          a.reload();
          break;
        case "back":
          a.goBack();
          break;
        case "forward":
          a.goForward();
          break;
        case "capture":
          a.postToFrame({ __proxyCmd: "capture", mode: cmd.mode || "page" });
          break;
        case "select":
          a.postToFrame({ __proxyCmd: cmd.enabled ? "select-start" : "select-stop" });
          break;
        case "draw":
          a.postToFrame({ __proxyCmd: cmd.enabled ? "draw-start" : "draw-stop" });
          break;
      }
    });
    return unsubscribe;
  }, [commands?.id, subscribeToStream]);

  // ---- comment overlay ----------------------------------------------------
  useEffect(() => {
    if (pending) commentRef.current?.focus();
  }, [pending]);

  // Put the marked line in the middle of the code frame rather than leaving it wherever it
  // happens to fall — often just off the bottom edge. scrollIntoView would drag every
  // scrollable ancestor with it, including the comment box, so move only the <pre>.
  useEffect(() => {
    const pre = codeRef.current;
    const hit = pre?.querySelector<HTMLElement>(".wvr-code-hit");
    if (!pre || !hit) return;
    pre.scrollTop = Math.max(0, hit.offsetTop - pre.clientHeight / 2 + hit.offsetHeight / 2);
  }, [pending?.debug?.codeFrame, pending?.resolving]);

  function submitComment() {
    if (!pending) return;
    const meta = pending.meta || {};
    const { xpath, selector, debug } = pending;
    const text = comment || "";
    setPending(null);
    setComment("");

    void resolveSource(debug).then((enriched) =>
      emit("comment", {
        tag: meta.tag || "",
        xpath,
        selector,
        comment: text,
        debugJson: enriched ? JSON.stringify(enriched) : null,
      }),
    );
  }

  function cancelComment() {
    setPending(null);
    setComment("");
  }

  // ---- render -------------------------------------------------------------
  const shellStyle: React.CSSProperties = {
    position: "relative",
    boxSizing: "border-box",
    overflow: "hidden",
    ...getWidth(width),
    ...getHeight(height),
  };

  const iframeStyle: React.CSSProperties =
    dev.w && dev.h ? { width: dev.w, height: dev.h } : { width: "100%", height: "100%" };

  return (
    // remove-parent-padding is Ivy's opt-out for full-bleed widgets: the host layout zeroes
    // its own padding when a child carries it, so the viewport reaches the container edges.
    <div className="wvr-shell remove-parent-padding" style={shellStyle}>
      <div className={"wvr-stage" + (dev.w ? " wvr-device" : "")}>
        {!currentUrl ? (
          <div className="wvr-empty">No URL — set the Url prop to load a page.</div>
        ) : swReady && frameSrc ? (
          <iframe
            ref={frameRef}
            key={`${frameSrc}#${reloadKey}`}
            className="wvr-frame"
            src={toViewUrl(frameSrc)}
            title="Web content"
            style={iframeStyle}
            onLoad={healEscapedFrame}
          />
        ) : (
          <div className="wvr-empty">Starting proxy…</div>
        )}
      </div>

      {pending && (
        <div className="wvr-overlay" onMouseDown={cancelComment}>
          <div className="wvr-comment-box" onMouseDown={(e) => e.stopPropagation()}>
            <div className="wvr-comment-title">
              Comment on
              {pending.meta?.tag && <span className="wvr-comment-tag">{pending.meta.tag}</span>}
              {pending.meta?.text && (
                <span className="wvr-comment-snippet">{quote(pending.meta.text)}</span>
              )}
            </div>
            {pending.resolving && (
              <div className="wvr-comment-field">
                <div className="wvr-comment-label">source</div>
                <div className="wvr-comment-value wvr-comment-muted">resolving source map…</div>
              </div>
            )}
            {!pending.resolving && sourceLabel(pending.debug) && (
              <div className="wvr-comment-field">
                <div className="wvr-comment-label">source</div>
                <div className="wvr-comment-value wvr-comment-source">{sourceLabel(pending.debug)}</div>
                <div className="wvr-comment-note">
                  {[pending.debug?.provenance, pending.debug?.confidence]
                    .filter(Boolean)
                    .join(" · ")}
                </div>
              </div>
            )}
            {(pending.debug?.ownerChain?.length ?? 0) > 0 && (
              <div className="wvr-comment-field">
                <div className="wvr-comment-label">components</div>
                <div className="wvr-comment-value">
                  {(pending.debug?.ownerChain ?? [])
                    .map((owner) => owner.name)
                    .filter(Boolean)
                    .join(" › ")}
                </div>
              </div>
            )}
            {!pending.resolving && pending.debug?.codeFrame && (
              <pre className="wvr-comment-code" ref={codeRef}>
                {pending.debug.codeFrame
                  .trimEnd()
                  .split("\n")
                  .map((line, i) => (
                    <div key={i} className={line.startsWith(">") ? "wvr-code-hit" : undefined}>
                      {line}
                    </div>
                  ))}
              </pre>
            )}
            {pending.xpath && (
              <div className="wvr-comment-field">
                <div className="wvr-comment-label">xpath</div>
                <div className="wvr-comment-value">{pending.xpath}</div>
              </div>
            )}
            {pending.selector && (
              <div className="wvr-comment-field">
                <div className="wvr-comment-label">selector</div>
                <div className="wvr-comment-value">{pending.selector}</div>
              </div>
            )}
            <textarea
              ref={commentRef}
              className="wvr-comment-input"
              value={comment}
              onChange={(e) => setComment(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Escape") cancelComment();
                if (e.key === "Enter" && (e.metaKey || e.ctrlKey)) submitComment();
              }}
              placeholder="Type a comment… (Ctrl+Enter to submit)"
              rows={4}
            />
            <div className="wvr-comment-actions">
              <button type="button" className="wvr-btn wvr-btn--ghost" onClick={cancelComment}>
                Cancel
              </button>
              <button type="button" className="wvr-btn wvr-btn--primary" onClick={submitComment}>
                Add
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
