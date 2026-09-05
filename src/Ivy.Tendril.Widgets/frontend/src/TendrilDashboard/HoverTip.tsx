import React, { useCallback, useRef, useState } from "react";

interface TipState {
  x: number;
  y: number;
  title: string;
  detail: string;
}

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
      });
    },
    [],
  );

  const hideTip = useCallback(() => setTip(null), []);

  return { wrapRef, tip, showTip, hideTip };
};

export const HoverTip: React.FC<{ tip: TipState | null }> = ({ tip }) =>
  tip ? (
    <div className="tdb-chart-tooltip" style={{ left: tip.x, top: tip.y }}>
      <div className="tdb-chart-tooltip-title">{tip.title}</div>
      <div className="tdb-chart-tooltip-row">{tip.detail}</div>
    </div>
  ) : null;
