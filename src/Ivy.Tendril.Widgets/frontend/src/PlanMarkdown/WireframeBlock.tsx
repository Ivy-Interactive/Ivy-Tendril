import React, { useContext, useEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { Maximize2, X } from "lucide-react";
import { IconButton } from "../ui/IconButton";
import { WireframeBaseContext } from "./wireframeContext";
import { parseWireframeFence, VIEWPORT_WIDTHS } from "./wireframeSource";
import type { WireframeSpec } from "./wireframeSource";

/**
 * Renders a `wireframe` fence as a live preview of the plan's wireframe.
 *
 * Only the wireframe itself is shown: never its source and never a screenshot. Screenshots exist
 * for the agent that made it to check its own work; the reviewer sees the real thing, and it hot
 * reloads while the agent edits it.
 */
export const WireframeBlock: React.FC<{ content: string }> = ({ content }) => {
  const base = useContext(WireframeBaseContext);
  const parsed = useMemo(() => parseWireframeFence(content), [content]);

  if (!parsed.ok) {
    return (
      <div className="pmv-wireframe pmv-wireframe-invalid" role="note">
        <div className="pmv-wireframe-state">
          <span>This wireframe block is not valid.</span>
          <span className="pmv-wireframe-detail">{parsed.error}</span>
        </div>
      </div>
    );
  }

  if (!base) {
    return (
      <div className="pmv-wireframe-placeholder" data-wireframe={parsed.spec.name}>
        Wireframe: <strong>{parsed.spec.name}</strong>. Open the plan to view it.
      </div>
    );
  }

  return <LiveWireframe base={base} spec={parsed.spec} />;
};

type Phase = "checking" | "running" | "failed" | "missing" | "unreachable";

/** Used until the framed page reports its real height. */
const INITIAL_HEIGHT = 360;

interface WireframeMessage {
  source: "tendril-wireframe";
  type: "size" | "build-failed" | "build-ok" | "stopped";
  height?: number;
  width?: number;
}

const isWireframeMessage = (data: unknown): data is WireframeMessage =>
  !!data && typeof data === "object" && (data as { source?: unknown }).source === "tendril-wireframe";

const LiveWireframe: React.FC<{ base: string; spec: WireframeSpec }> = ({ base, spec }) => {
  const src = `${base.endsWith("/") ? base : `${base}/`}${spec.name}/`;
  // Names the frame for assistive technology; nothing on screen shows it.
  const label = spec.name;

  const [phase, setPhase] = useState<Phase>("checking");
  const [message, setMessage] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  const [pageHeight, setPageHeight] = useState<number | null>(null);
  const [pageWidth, setPageWidth] = useState<number | null>(null);
  const [latestFailed, setLatestFailed] = useState(false);
  const [paused, setPaused] = useState(false);
  const [fullSize, setFullSize] = useState(false);

  const frameRef = useRef<HTMLIFrameElement>(null);

  // The status request waits for the first build, so "checking" doubles as "building".
  useEffect(() => {
    let cancelled = false;
    setPhase("checking");
    setMessage(null);

    fetch(`${src}__wireframe/status`, { cache: "no-store" })
      .then((response) => (response.ok ? response.json() : Promise.reject(new Error(String(response.status)))))
      .then((status: { phase?: string; message?: string | null }) => {
        if (cancelled) return;
        const next: Phase =
          status.phase === "running" || status.phase === "failed" || status.phase === "missing"
            ? status.phase
            : "unreachable";
        setPhase(next);
        setMessage(status.message ?? null);
      })
      .catch(() => {
        if (!cancelled) setPhase("unreachable");
      });

    return () => {
      cancelled = true;
    };
  }, [src, attempt]);

  // The framed page reports its content size and build state. Several frames can share this
  // window, so a message counts only when it comes from this block's own inline frame.
  useEffect(() => {
    const onMessage = (event: MessageEvent) => {
      if (event.origin !== window.location.origin) return;
      if (!frameRef.current || event.source !== frameRef.current.contentWindow) return;
      if (!isWireframeMessage(event.data)) return;

      switch (event.data.type) {
        case "size":
          if (typeof event.data.height === "number" && Number.isFinite(event.data.height)) {
            setPageHeight(event.data.height);
          }
          if (typeof event.data.width === "number" && Number.isFinite(event.data.width)) {
            setPageWidth(event.data.width);
          }
          break;
        case "build-failed":
          setLatestFailed(true);
          break;
        case "build-ok":
          setLatestFailed(false);
          break;
        case "stopped":
          setPaused(true);
          break;
      }
    };

    window.addEventListener("message", onMessage);
    return () => window.removeEventListener("message", onMessage);
  }, []);

  // Full size is an overlay inside the page, not a new window. In the desktop window (Rustino) the
  // page cannot open one, and an external browser does not trust the desktop app's certificate for
  // this address. The overlay loads the same same-origin URL as the inline frame, so it works in the
  // desktop window, in a browser and through a share link alike.
  useEffect(() => {
    if (!fullSize) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") setFullSize(false);
    };
    document.addEventListener("keydown", onKey);
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => {
      document.removeEventListener("keydown", onKey);
      document.body.style.overflow = previousOverflow;
    };
  }, [fullSize]);

  const retry = () => {
    setLatestFailed(false);
    setPaused(false);
    setPageHeight(null);
    setPageWidth(null);
    setAttempt((n) => n + 1);
  };

  const frame = {
    src,
    title: label,
    viewportWidth: spec.viewport ? VIEWPORT_WIDTHS[spec.viewport] : undefined,
    contentWidth: pageWidth ?? undefined,
    contentHeight: pageHeight ?? INITIAL_HEIGHT,
    fixedHeight: spec.height,
  };

  let note: string | null = null;
  if (phase === "running" && latestFailed) note = "Showing the last version that built";
  else if (phase === "running" && paused) note = "Live updates paused";

  return (
    <figure className="pmv-wireframe" data-wireframe={spec.name} data-phase={phase}>
      {/* No title bar: the actions float over the frame's top-right corner. */}
      {(note || phase === "running") && (
        <div className="pmv-wireframe-actions">
          {note && <span className="pmv-wireframe-note">{note}</span>}
          {phase === "running" && (
            <IconButton label="Open Full Size" size="sm" onClick={() => setFullSize(true)}>
              <Maximize2 size={14} aria-hidden="true" />
            </IconButton>
          )}
        </div>
      )}

      {phase === "checking" && <div className="pmv-wireframe-state">Building wireframe...</div>}

      {phase === "missing" && (
        <div className="pmv-wireframe-state">
          <span>
            This plan has no wireframe named <strong>{spec.name}</strong>.
          </span>
        </div>
      )}

      {(phase === "failed" || phase === "unreachable") && (
        <div className="pmv-wireframe-state">
          <span>
            {phase === "failed"
              ? message ?? "This wireframe does not build."
              : "The wireframe preview is not available right now."}
          </span>
          <button type="button" className="pmv-wireframe-button" onClick={retry}>
            Retry
          </button>
        </div>
      )}

      {phase === "running" && <ScaledFrame key={attempt} ref={frameRef} {...frame} />}

      {fullSize &&
        createPortal(
          <div
            className="pmv-wireframe-overlay"
            role="dialog"
            aria-modal="true"
            aria-label={label}
            onClick={(e) => {
              if (e.target === e.currentTarget) setFullSize(false);
            }}
          >
            <div className="pmv-wireframe-overlay-panel">
              <div className="pmv-wireframe-bar pmv-wireframe-overlay-bar">
                <IconButton label="Close" size="sm" onClick={() => setFullSize(false)}>
                  <X size={14} aria-hidden="true" />
                </IconButton>
              </div>
              <div className="pmv-wireframe-overlay-body">
                <ScaledFrame {...frame} fill />
              </div>
            </div>
          </div>,
          document.body,
        )}
    </figure>
  );
};

/** The tallest an inline frame gets on screen; a longer page scrolls inside it. */
const MAX_VISIBLE_HEIGHT = 800;

interface ScaledFrameProps {
  src: string;
  title: string;
  /** A width the fence asked for (a Mobile or Tablet viewport). Otherwise the frame takes its box's width. */
  viewportWidth?: number;
  /** The page's content width, as reported from inside the frame. */
  contentWidth?: number;
  /** The page's content height, as reported from inside the frame. */
  contentHeight: number;
  /** A page height the fence asked for. */
  fixedHeight?: number;
  /** Fill the box's height (the full-size overlay) rather than fitting the content up to a cap. */
  fill?: boolean;
}

/**
 * A frame zoomed on width only.
 *
 * The page renders at the width it is given, unscaled, so a wireframe reads at its natural size.
 * Only when it needs more width than that (content that overflows, or a fence that names a wider
 * viewport) does the frame render at the needed width and zoom out to fit. Height is never zoomed
 * to fit: the frame is as tall as the page up to a cap, and a longer page scrolls inside it.
 *
 * Two boxes when zoomed, as in Studio's preview: a CSS transform does not change an element's
 * layout size, so the outer box takes the scaled footprint and the iframe inside keeps its real
 * size, shrunk from its top-left corner.
 */
const ScaledFrame = React.forwardRef<HTMLIFrameElement, ScaledFrameProps>(function ScaledFrame(
  { src, title, viewportWidth, contentWidth, contentHeight, fixedHeight, fill },
  ref,
) {
  const boxRef = useRef<HTMLDivElement>(null);
  const [box, setBox] = useState({ width: 0, height: 0 });

  useEffect(() => {
    const el = boxRef.current;
    if (!el || typeof ResizeObserver === "undefined") return;
    const observer = new ResizeObserver((entries) => {
      const rect = entries[0]?.contentRect;
      if (rect) setBox({ width: rect.width, height: rect.height });
    });
    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  const available = box.width;

  // A width to render at, when the box's own width is not enough. Content that fits reports the
  // frame's own width back, which is not a reason to zoom.
  const needed =
    viewportWidth ??
    (available > 0 && contentWidth !== undefined && contentWidth > available + 1 ? contentWidth : undefined);

  const scale = needed !== undefined && available > 0 ? Math.min(1, available / needed) : 1;

  const pageHeight = fixedHeight ?? contentHeight;
  const visibleHeight = fill
    ? box.height
    : fixedHeight !== undefined
      ? pageHeight * scale
      : Math.min(pageHeight * scale, MAX_VISIBLE_HEIGHT);

  return (
    <div ref={boxRef} className={fill ? "pmv-wireframe-frame-box pmv-wireframe-frame-fill" : "pmv-wireframe-frame-box"}>
      <div
        className="pmv-wireframe-frame-scaled"
        style={{
          width: needed !== undefined ? Math.round(needed * scale) : "100%",
          height: Math.round(visibleHeight),
        }}
      >
        <iframe
          ref={ref}
          src={src}
          title={title}
          referrerPolicy="same-origin"
          style={{
            width: needed !== undefined ? `${needed}px` : "100%",
            height: `${Math.round(visibleHeight / scale)}px`,
            transform: scale < 1 ? `scale(${scale})` : undefined,
          }}
        />
      </div>
    </div>
  );
});
