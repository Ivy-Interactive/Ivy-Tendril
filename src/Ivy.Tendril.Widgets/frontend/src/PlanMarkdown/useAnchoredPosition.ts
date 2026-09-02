import { useEffect, useLayoutEffect, useState, type RefObject } from "react";
import { getOffsetRect } from "./annotationUtils";

export interface Position {
  top: number;
  left: number;
}

interface AnchorOffsets {
  startOffset: number;
  endOffset: number;
}

interface AnchoredPosition {
  position: Position;
  visible: boolean;
}

/**
 * Tracks the viewport position of a plain-text offset range inside
 * containerRef, re-measuring on scroll/resize so a `position: fixed` element
 * anchored to it (a selection toolbar or annotation popover) stays lined up
 * with the text as the scrollable shell scrolls. Mirrors the
 * scroll(capture)/resize pattern used by BadgeSelect, ChatWidget and
 * PlanDiffView.
 *
 * `visible` becomes false once the anchor scrolls outside shellRef's bounds,
 * so callers can hide (not unmount) the element and preserve its state.
 */
export function useAnchoredPosition(
  containerRef: RefObject<HTMLElement | null>,
  shellRef: RefObject<HTMLElement | null>,
  anchor: AnchorOffsets | null,
): AnchoredPosition | null {
  const [result, setResult] = useState<AnchoredPosition | null>(null);
  const { startOffset, endOffset } = anchor ?? {};

  useLayoutEffect(() => {
    if (startOffset === undefined || endOffset === undefined) {
      setResult(null);
      return;
    }
    recompute(containerRef, shellRef, startOffset, endOffset, setResult);
  }, [containerRef, shellRef, startOffset, endOffset]);

  useEffect(() => {
    if (startOffset === undefined || endOffset === undefined) return;
    const onReposition = () => recompute(containerRef, shellRef, startOffset, endOffset, setResult);
    window.addEventListener("scroll", onReposition, true);
    window.addEventListener("resize", onReposition);
    return () => {
      window.removeEventListener("scroll", onReposition, true);
      window.removeEventListener("resize", onReposition);
    };
  }, [containerRef, shellRef, startOffset, endOffset]);

  return result;
}

function recompute(
  containerRef: RefObject<HTMLElement | null>,
  shellRef: RefObject<HTMLElement | null>,
  startOffset: number,
  endOffset: number,
  setResult: (result: AnchoredPosition | null) => void,
): void {
  const container = containerRef.current;
  if (!container) {
    setResult(null);
    return;
  }

  const rect = getOffsetRect(container, startOffset, endOffset);
  if (!rect) {
    setResult(null);
    return;
  }

  const shellRect = shellRef.current?.getBoundingClientRect();
  const visible = !shellRect || !(rect.bottom < shellRect.top || rect.top > shellRect.bottom);

  setResult({
    position: { top: rect.bottom + 4, left: rect.left },
    visible,
  });
}
