import React, { useCallback, useLayoutEffect, useRef, useState } from "react";

export interface TipState {
  x: number;
  y: number;
  title: string;
  detail: string;
  containerWidth?: number;
}

/**
 * Clamps horizontal tooltip position within container margins [pad, containerWidth - pad].
 * Tooltip CSS centers horizontally via transform: translate(-50%, ...), so the center coordinate
 * must stay between tipWidth / 2 + pad and containerWidth - tipWidth / 2 - pad.
 */
export const clampTipX = (
  x: number,
  tipWidth: number,
  containerWidth: number,
  pad = 8,
): number => {
  if (containerWidth <= 0 || tipWidth <= 0) return x;
  if (tipWidth + pad * 2 >= containerWidth) return containerWidth / 2;
  const minX = tipWidth / 2 + pad;
  const maxX = containerWidth - tipWidth / 2 - pad;
  return Math.max(minX, Math.min(x, maxX));
};

/** Anchored tooltip state for chart cells/bars. The wrapper element must be
    position: relative; coordinates are relative to it, so scrolled content
    inside the wrapper stays accurate. */
export const useHoverTip = () => {
  const wrapRef = useRef<HTMLDivElement>(null);
  const [tip, setTip] = useState<TipState | null>(null);

  const showTip = useCallback(
    (title: string, detail: string) => (e: React.MouseEvent<HTMLElement>) => {
      const wrap = wrapRef.current;
      if (!wrap) return;
      const wrapRect = wrap.getBoundingClientRect();
      const rect = e.currentTarget.getBoundingClientRect();
      setTip({
        x: rect.left + rect.width / 2 - wrapRect.left,
        y: rect.top - wrapRect.top,
        title,
        detail,
        containerWidth: wrapRect.width,
      });
    },
    [],
  );

  const hideTip = useCallback(() => setTip(null), []);

  return { wrapRef, tip, showTip, hideTip };
};

export const HoverTip: React.FC<{ tip: TipState | null }> = ({ tip }) => {
  const tipRef = useRef<HTMLDivElement>(null);
  const [clampedX, setClampedX] = useState<number | null>(null);

  useLayoutEffect(() => {
    if (!tip || !tipRef.current) {
      setClampedX(null);
      return;
    }
    const tipWidth = tipRef.current.offsetWidth;
    const containerWidth =
      tip.containerWidth ?? tipRef.current.parentElement?.clientWidth ?? 0;
    if (tipWidth > 0 && containerWidth > 0) {
      const calculated = clampTipX(tip.x, tipWidth, containerWidth);
      setClampedX(calculated);
      tipRef.current.style.left = `${calculated}px`;
    } else {
      setClampedX(null);
    }
  }, [tip]);

  if (!tip) return null;

  return (
    <div
      ref={tipRef}
      className="tdb-chart-tooltip"
      style={{ left: clampedX ?? tip.x, top: tip.y }}
    >
      <div className="tdb-chart-tooltip-title">{tip.title}</div>
      <div className="tdb-chart-tooltip-row">{tip.detail}</div>
    </div>
  );
};
