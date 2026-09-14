import React, { useCallback, useEffect, useRef, useState } from "react";
import { ShellContext } from "./ShellContext";
import { ShellWidgetProps, isModKey } from "./types";
import "./shell.css";

export const SIDEBAR_COLLAPSED_STORAGE_KEY = "tendril.shell.sidebarCollapsed";
export const SIDEBAR_WIDTH_STORAGE_KEY = "tendril.shell.sidebarWidth";
export const DEFAULT_SIDEBAR_WIDTH = 320;
export const MIN_SIDEBAR_WIDTH = 200;
export const MAX_SIDEBAR_WIDTH = 640;

const readStoredCollapsed = (): boolean | null => {
  try {
    const raw = window.localStorage.getItem(SIDEBAR_COLLAPSED_STORAGE_KEY);
    if (raw === "true") return true;
    if (raw === "false") return false;
    return null;
  } catch {
    return null;
  }
};

const writeStoredCollapsed = (collapsed: boolean) => {
  try {
    window.localStorage.setItem(SIDEBAR_COLLAPSED_STORAGE_KEY, String(collapsed));
  } catch {
    /* storage unavailable (private mode, sandboxed host): the state just doesn't persist */
  }
};

export const readStoredWidth = (): number | null => {
  try {
    const raw = window.localStorage.getItem(SIDEBAR_WIDTH_STORAGE_KEY);
    if (raw == null) return null;
    const parsed = Number(raw);
    if (Number.isFinite(parsed) && parsed >= MIN_SIDEBAR_WIDTH) {
      return parsed;
    }
    return null;
  } catch {
    return null;
  }
};

export const writeStoredWidth = (width: number | null) => {
  try {
    if (width == null) {
      window.localStorage.removeItem(SIDEBAR_WIDTH_STORAGE_KEY);
    } else {
      window.localStorage.setItem(SIDEBAR_WIDTH_STORAGE_KEY, String(Math.round(width)));
    }
  } catch {
    /* storage unavailable (private mode, sandboxed host): the state just doesn't persist */
  }
};

interface TendrilShellProps extends ShellWidgetProps {
  collapsed?: boolean;
  activeSessionIndex?: number | null;
  hasTabs?: boolean;
  slots?: {
    SidebarHeader?: React.ReactNode;
    SidebarBody?: React.ReactNode;
    SidebarFooter?: React.ReactNode;
    Content?: React.ReactNode;
    SessionContents?: React.ReactNode;
    Tabs?: React.ReactNode;
    Hidden?: React.ReactNode;
  };
}

/**
 * The Tendril app chrome: sidebar (expanded / icon rail) and one rounded,
 * bordered container holding the white content surface with the session tab
 * strip inside its bottom edge. Collapse is client-side for a smooth
 * animation; the server is notified through OnCollapsedChanged so the state
 * can be persisted in session. Session panes all stay mounted - only the active one is
 * visible - so agent terminals keep their buffers when switching tabs. The
 * Hidden slot hosts zero-size utility widgets (shortcut ghosts, chunk
 * warm-ups) without letting them paint.
 */
export const TendrilShell: React.FC<TendrilShellProps> = ({
  id,
  events = [],
  eventHandler,
  collapsed: collapsedProp = false,
  activeSessionIndex,
  hasTabs = false,
  slots,
}) => {
  const [collapsed, setCollapsed] = useState(() => {
    const stored = readStoredCollapsed();
    return stored != null ? stored : collapsedProp;
  });
  const prevPropRef = useRef(collapsedProp);
  if (collapsedProp !== prevPropRef.current) {
    prevPropRef.current = collapsedProp;
    if (collapsedProp !== collapsed) setCollapsed(collapsedProp);
  }

  const toggle = useCallback(() => {
    setCollapsed((prev) => {
      const next = !prev;
      writeStoredCollapsed(next);
      if (events.includes("OnCollapsedChanged")) {
        eventHandler("OnCollapsedChanged", id, [next]);
      }
      return next;
    });
  }, [events, eventHandler, id]);

  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if (isModKey(e) && !e.shiftKey && !e.altKey && e.key.toLowerCase() === "b") {
        e.preventDefault();
        toggle();
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [toggle]);

  const [sidebarWidth, setSidebarWidth] = useState<number>(
    () => readStoredWidth() ?? DEFAULT_SIDEBAR_WIDTH
  );
  const [isResizing, setIsResizing] = useState(false);
  const dragRef = useRef<{ startX: number; startWidth: number; currentWidth?: number } | null>(null);

  const onResizerPointerDown = useCallback(
    (e: React.PointerEvent<HTMLDivElement>) => {
      if (e.button !== 0 || collapsed) return;
      e.preventDefault();
      e.currentTarget.setPointerCapture?.(e.pointerId);
      dragRef.current = { startX: e.clientX, startWidth: sidebarWidth, currentWidth: sidebarWidth };
      setIsResizing(true);
    },
    [collapsed, sidebarWidth]
  );

  const onResizerPointerMove = useCallback((e: React.PointerEvent<HTMLDivElement>) => {
    if (!dragRef.current) return;
    const delta = e.clientX - dragRef.current.startX;
    const maxWidth = Math.min(MAX_SIDEBAR_WIDTH, Math.max(MIN_SIDEBAR_WIDTH, window.innerWidth - 300));
    const nextWidth = Math.max(MIN_SIDEBAR_WIDTH, Math.min(maxWidth, dragRef.current.startWidth + delta));
    dragRef.current.currentWidth = nextWidth;
    setSidebarWidth(nextWidth);
  }, []);

  const onResizerPointerUp = useCallback(
    (e: React.PointerEvent<HTMLDivElement>) => {
      if (!dragRef.current) return;
      e.currentTarget.releasePointerCapture?.(e.pointerId);
      const finalWidth = dragRef.current.currentWidth ?? sidebarWidth;
      dragRef.current = null;
      setIsResizing(false);
      writeStoredWidth(finalWidth);
    },
    [sidebarWidth]
  );

  const onResizerDoubleClick = useCallback(() => {
    setSidebarWidth(DEFAULT_SIDEBAR_WIDTH);
    writeStoredWidth(null);
  }, []);

  const sessionPanes = React.Children.toArray(slots?.SessionContents ?? []);
  const hasActiveSession =
    activeSessionIndex != null && activeSessionIndex >= 0 && activeSessionIndex < sessionPanes.length;

  return (
    <div
      className="tsh-root remove-parent-padding"
      data-collapsed={collapsed}
      data-resizing={isResizing}
      style={
        {
          "--tsh-sidebar-width": `${sidebarWidth}px`,
        } as React.CSSProperties
      }
    >
      <ShellContext.Provider value={{ collapsed, toggle }}>
        <div className="tsh-sidebar">
          <div className="tsh-sidebar-header">{slots?.SidebarHeader}</div>
          <div className="tsh-sidebar-body">{slots?.SidebarBody}</div>
          <div className="tsh-sidebar-footer">{slots?.SidebarFooter}</div>
          {!collapsed && (
            <div
              className="tsh-sidebar-resizer"
              role="separator"
              aria-orientation="vertical"
              aria-label="Resize sidebar"
              onPointerDown={onResizerPointerDown}
              onPointerMove={onResizerPointerMove}
              onPointerUp={onResizerPointerUp}
              onPointerCancel={onResizerPointerUp}
              onDoubleClick={onResizerDoubleClick}
            />
          )}
        </div>
      </ShellContext.Provider>
      <ShellContext.Provider value={{ collapsed: false, toggle }}>
        <div className="tsh-main">
          <div className="tsh-container">
            <div className="tsh-frame" data-has-tabs={hasTabs}>
              <div className="tsh-frame-pane" data-active={!hasActiveSession}>
                {slots?.Content}
              </div>
              {sessionPanes.map((pane, index) => (
                <div
                  className="tsh-frame-pane"
                  data-active={hasActiveSession && index === activeSessionIndex}
                  key={(React.isValidElement(pane) && pane.key) || index}
                >
                  {pane}
                </div>
              ))}
            </div>
            {hasTabs && slots?.Tabs && <div className="tsh-tabs-row">{slots.Tabs}</div>}
          </div>
        </div>
      </ShellContext.Provider>
      {slots?.Hidden && <div style={{ display: "none" }}>{slots.Hidden}</div>}
    </div>
  );
};
