import React, { useCallback, useEffect, useRef, useState } from "react";
import { Floating } from "./floating";

interface TooltipProps {
  label: string;
  disabled?: boolean;
  children: React.ReactNode;
}

const SHOW_DELAY_MS = 400;

function isKeyboardFocus(element: HTMLElement): boolean {
  try {
    return element.matches(":focus-visible");
  } catch {
    return true;
  }
}

export const Tooltip: React.FC<TooltipProps> = ({ label, disabled = false, children }) => {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const [open, setOpen] = useState(false);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const cancel = useCallback(() => {
    if (timer.current !== null) {
      clearTimeout(timer.current);
      timer.current = null;
    }
  }, []);

  const show = useCallback(() => {
    cancel();
    if (disabled) return;
    timer.current = setTimeout(() => setOpen(true), SHOW_DELAY_MS);
  }, [cancel, disabled]);

  const hide = useCallback(() => {
    cancel();
    setOpen(false);
  }, [cancel]);

  useEffect(() => cancel, [cancel]);

  useEffect(() => {
    if (disabled) hide();
  }, [disabled, hide]);

  return (
    <span
      ref={setAnchor}
      className="wvr-tip-anchor"
      onMouseEnter={show}
      onMouseLeave={hide}
      onFocus={(e) => {
        if (isKeyboardFocus(e.target as HTMLElement)) show();
      }}
      onBlur={hide}
      onMouseDown={hide}
      onClick={hide}
    >
      {children}
      {open && anchor && (
        <Floating anchor={anchor} className="wvr-tip" role="tooltip">
          {label}
        </Floating>
      )}
    </span>
  );
};
