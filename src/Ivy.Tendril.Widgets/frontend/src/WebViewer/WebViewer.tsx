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
  xpath: string;
  selector: string;
  meta: { tag?: string; text?: string } | null;
  react: unknown;
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

function toViewUrl(realUrl: string): string {
  return `/__view/${realUrl}`;
}

// Loose comparison so trailing-slash differences don't create dupe history.
function sameUrl(a?: string | null, b?: string | null): boolean {
  if (!a || !b) return false;
  return a.replace(/\/+$/, "") === b.replace(/\/+$/, "");
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
  const [swReady, setSwReady] = useState(false);
  const [pending, setPending] = useState<PendingComment | null>(null);
  const [comment, setComment] = useState("");

  const frameRef = useRef<HTMLIFrameElement>(null);
  const commentRef = useRef<HTMLTextAreaElement>(null);

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
  const applyNav = useCallback((nextHistory: string[], nextIndex: number) => {
    navRef.current = { history: nextHistory, index: nextIndex };
    setHistory(nextHistory);
    setIndex(nextIndex);
  }, []);

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
    navigator.serviceWorker
      .register("/sw.js")
      .then(() => navigator.serviceWorker.ready)
      .then(() => {
        if (!cancelled) setSwReady(true);
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
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Tell the SW which device to emulate, then reload so the new UA takes effect.
  useEffect(() => {
    if (!swReady) return;
    const send = () =>
      navigator.serviceWorker.controller?.postMessage({
        __proxySetDevice: devKey === "desktop" ? null : devKey,
      });
    send();
    navigator.serviceWorker.addEventListener("controllerchange", send);
    return () =>
      navigator.serviceWorker.removeEventListener("controllerchange", send);
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
            reactJson: data.react ? JSON.stringify(data.react) : null,
          });
          return;
        }
        case "draw": {
          const pts = data.points || [];
          if (!pts.length) return;
          emit("draw", { pointCount: pts.length, pointsJson: JSON.stringify(pts) });
          return;
        }
        case "selected":
          // Internal: open the widget's own comment overlay. Not surfaced as an event.
          setPending({
            xpath: data.xpath,
            selector: data.selector,
            meta: data.meta || {},
            react: data.react || null,
          });
          setComment("");
          return;
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
          applyNav(newHistory, newHistory.length - 1);
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

  function submitComment() {
    if (!pending) return;
    const meta = pending.meta || {};
    emit("comment", {
      tag: meta.tag || "",
      xpath: pending.xpath,
      selector: pending.selector,
      comment: comment || "",
      reactJson: pending.react ? JSON.stringify(pending.react) : null,
    });
    setPending(null);
    setComment("");
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
    <div className="wvr-shell" style={shellStyle}>
      <div className={"wvr-stage" + (dev.w ? " wvr-device" : "")}>
        {!currentUrl ? (
          <div className="wvr-empty">No URL — set the Url prop to load a page.</div>
        ) : swReady ? (
          <iframe
            ref={frameRef}
            key={`${currentUrl}#${reloadKey}`}
            className="wvr-frame"
            src={toViewUrl(currentUrl)}
            title="Web content"
            style={iframeStyle}
          />
        ) : (
          <div className="wvr-empty">Starting proxy…</div>
        )}
      </div>

      {pending && (
        <div className="wvr-overlay" onMouseDown={cancelComment}>
          <div className="wvr-comment-box" onMouseDown={(e) => e.stopPropagation()}>
            <div className="wvr-comment-title">Comment on element</div>
            <div className="wvr-comment-xpath">
              {pending.meta?.tag && <span className="wvr-comment-tag">{pending.meta.tag}</span>}
              {pending.xpath}
            </div>
            {pending.selector && (
              <div className="wvr-comment-meta">
                <span className="wvr-comment-key">selector</span>
                {pending.selector}
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
              <button className="wvr-btn" onClick={cancelComment}>
                Cancel
              </button>
              <button className="wvr-btn wvr-btn-primary" onClick={submitComment}>
                Submit
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
