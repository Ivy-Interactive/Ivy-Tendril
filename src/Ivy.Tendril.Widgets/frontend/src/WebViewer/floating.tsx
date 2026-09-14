import React, { useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";

export type FloatingAlign = "center" | "end";

interface FloatingProps {
  anchor: HTMLElement;
  align?: FloatingAlign;
  offset?: number;
  className: string;
  role?: string;
  children: React.ReactNode;
}

const EDGE_MARGIN = 8;

export const Floating: React.FC<FloatingProps> = ({
  anchor,
  align = "center",
  offset = 6,
  className,
  role,
  children,
}) => {
  const ref = useRef<HTMLDivElement>(null);
  const [position, setPosition] = useState<{ top: number; left: number } | null>(null);

  useLayoutEffect(() => {
    const element = ref.current;
    if (!element) return;
    const place = () => {
      const rect = anchor.getBoundingClientRect();
      const width = element.offsetWidth;
      const preferred = align === "center" ? rect.left + rect.width / 2 - width / 2 : rect.right - width;
      const maxLeft = Math.max(EDGE_MARGIN, window.innerWidth - width - EDGE_MARGIN);
      setPosition({
        top: rect.bottom + offset,
        left: Math.min(Math.max(EDGE_MARGIN, preferred), maxLeft),
      });
    };
    place();
    window.addEventListener("resize", place);
    window.addEventListener("scroll", place, true);
    return () => {
      window.removeEventListener("resize", place);
      window.removeEventListener("scroll", place, true);
    };
  }, [anchor, align, offset]);

  return createPortal(
    <div
      ref={ref}
      role={role}
      className={`wvr-floating ${className}`}
      style={{
        position: "fixed",
        top: position?.top ?? 0,
        left: position?.left ?? 0,
        visibility: position ? "visible" : "hidden",
      }}
    >
      {children}
    </div>,
    document.body,
  );
};
