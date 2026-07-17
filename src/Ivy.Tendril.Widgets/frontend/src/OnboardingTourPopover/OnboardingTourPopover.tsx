import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { X } from "lucide-react";
import "./onboarding-tour.css";

type IvyEventHandler = (eventName: string, widgetId: string, args: unknown[]) => void;

type Placement = "right" | "left" | "top" | "bottom";

interface OnboardingTourPopoverProps {
  id: string;
  anchorSelector?: string;
  title?: string;
  description?: string;
  stepIndex?: number;
  stepCount?: number;
  placement?: string;
  highlightAnchor?: boolean;
  events?: string[];
  eventHandler?: IvyEventHandler;
}

interface Rect {
  top: number;
  left: number;
  width: number;
  height: number;
}

const CARD_WIDTH = 320;
const GAP = 14; // distance between anchor and card (leaves room for the arrow)
const MARGIN = 8; // minimum distance from the viewport edges
const ARROW = 12; // arrow square size
const RING_PAD = 4;

function rectsDiffer(a: Rect | null, b: Rect | null): boolean {
  if (!a || !b) return a !== b;
  return (
    Math.abs(a.top - b.top) > 0.5 ||
    Math.abs(a.left - b.left) > 0.5 ||
    Math.abs(a.width - b.width) > 0.5 ||
    Math.abs(a.height - b.height) > 0.5
  );
}

function resolvePlacement(anchor: Rect, preferred: Placement, cardHeight: number): Placement {
  const vw = window.innerWidth;
  const vh = window.innerHeight;
  const fits: Record<Placement, boolean> = {
    right: anchor.left + anchor.width + GAP + CARD_WIDTH + MARGIN <= vw,
    left: anchor.left - GAP - CARD_WIDTH - MARGIN >= 0,
    bottom: anchor.top + anchor.height + GAP + cardHeight + MARGIN <= vh,
    top: anchor.top - GAP - cardHeight - MARGIN >= 0,
  };
  if (fits[preferred]) return preferred;
  const fallbacks: Record<Placement, Placement[]> = {
    right: ["left", "bottom", "top"],
    left: ["right", "bottom", "top"],
    bottom: ["top", "right", "left"],
    top: ["bottom", "right", "left"],
  };
  return fallbacks[preferred].find((p) => fits[p]) ?? preferred;
}

export const OnboardingTourPopover: React.FC<OnboardingTourPopoverProps> = ({
  id,
  anchorSelector = "",
  title = "",
  description = "",
  stepIndex = 0,
  stepCount = 1,
  placement = "right",
  highlightAnchor = true,
  eventHandler,
}) => {
  const [anchorRect, setAnchorRect] = useState<Rect | null>(null);
  const cardRef = useRef<HTMLDivElement>(null);
  const [cardHeight, setCardHeight] = useState(180);

  const updateAnchor = useCallback(() => {
    const el = anchorSelector ? document.querySelector(anchorSelector) : null;
    if (!el) {
      setAnchorRect((prev) => (prev === null ? prev : null));
      return;
    }
    const r = el.getBoundingClientRect();
    const next: Rect = { top: r.top, left: r.left, width: r.width, height: r.height };
    if (r.width === 0 && r.height === 0) {
      setAnchorRect((prev) => (prev === null ? prev : null));
      return;
    }
    setAnchorRect((prev) => (rectsDiffer(prev, next) ? next : prev));
  }, [anchorSelector]);

  // Track the anchor: react to scroll/resize immediately and poll at a low
  // frequency to catch the anchor mounting, unmounting, or moving for reasons
  // that fire no event (e.g. sidebar content changes).
  useEffect(() => {
    updateAnchor();
    const interval = window.setInterval(updateAnchor, 250);
    window.addEventListener("resize", updateAnchor);
    document.addEventListener("scroll", updateAnchor, true);
    return () => {
      window.clearInterval(interval);
      window.removeEventListener("resize", updateAnchor);
      document.removeEventListener("scroll", updateAnchor, true);
    };
  }, [updateAnchor]);

  useEffect(() => {
    if (cardRef.current) setCardHeight(cardRef.current.offsetHeight);
  }, [title, description, anchorRect === null]);

  const fire = useCallback(
    (event: "OnNext" | "OnBack" | "OnDismiss") => () => {
      eventHandler?.(event, id, []);
    },
    [eventHandler, id],
  );

  const layout = useMemo(() => {
    if (!anchorRect) return null;
    const side = resolvePlacement(anchorRect, (placement as Placement) || "right", cardHeight);
    const anchorCx = anchorRect.left + anchorRect.width / 2;
    const anchorCy = anchorRect.top + anchorRect.height / 2;

    let top: number;
    let left: number;
    if (side === "right" || side === "left") {
      top = anchorCy - cardHeight / 2;
      left = side === "right" ? anchorRect.left + anchorRect.width + GAP : anchorRect.left - GAP - CARD_WIDTH;
    } else {
      left = anchorCx - CARD_WIDTH / 2;
      top = side === "bottom" ? anchorRect.top + anchorRect.height + GAP : anchorRect.top - GAP - cardHeight;
    }
    top = Math.max(MARGIN, Math.min(top, window.innerHeight - cardHeight - MARGIN));
    left = Math.max(MARGIN, Math.min(left, window.innerWidth - CARD_WIDTH - MARGIN));

    // Arrow sits on the card edge facing the anchor, aimed at the anchor center
    // (clamped so it stays within the card's rounded corners).
    const arrow: React.CSSProperties = {};
    const arrowHalf = ARROW / 2;
    if (side === "right" || side === "left") {
      const y = Math.max(18, Math.min(anchorCy - top, cardHeight - 18));
      arrow.top = y - arrowHalf;
      if (side === "right") arrow.left = -arrowHalf;
      else arrow.right = -arrowHalf;
    } else {
      const x = Math.max(18, Math.min(anchorCx - left, CARD_WIDTH - 18));
      arrow.left = x - arrowHalf;
      if (side === "bottom") arrow.top = -arrowHalf;
      else arrow.bottom = -arrowHalf;
    }

    return { side, top, left, arrow };
  }, [anchorRect, placement, cardHeight]);

  if (!anchorRect || !layout) return null;

  const isFirst = stepIndex <= 0;
  const isLast = stepIndex >= stepCount - 1;

  return createPortal(
    <>
      {highlightAnchor && (
        <div
          className="otp-ring"
          style={{
            top: anchorRect.top - RING_PAD,
            left: anchorRect.left - RING_PAD,
            width: anchorRect.width + RING_PAD * 2,
            height: anchorRect.height + RING_PAD * 2,
          }}
        />
      )}
      <div
        ref={cardRef}
        className="otp-card"
        data-side={layout.side}
        role="dialog"
        aria-label={title}
        style={{ top: layout.top, left: layout.left, width: CARD_WIDTH }}
      >
        <div className="otp-arrow" data-side={layout.side} style={layout.arrow} />
        <div className="otp-header">
          <h3 className="otp-title">{title}</h3>
          <button className="otp-close" aria-label="Close tour" onClick={fire("OnDismiss")}>
            <X size={15} />
          </button>
        </div>
        <p className="otp-description">{description}</p>
        <div className="otp-footer">
          <span className="otp-counter">
            {stepIndex + 1} of {stepCount}
          </span>
          <div className="otp-buttons">
            {isFirst ? (
              <button className="otp-btn otp-btn-secondary" onClick={fire("OnDismiss")}>
                Skip
              </button>
            ) : (
              <button className="otp-btn otp-btn-secondary" onClick={fire("OnBack")}>
                Back
              </button>
            )}
            <button className="otp-btn otp-btn-primary" onClick={fire("OnNext")}>
              {isLast ? "Done" : "Continue"}
            </button>
          </div>
        </div>
      </div>
    </>,
    document.body,
  );
};
