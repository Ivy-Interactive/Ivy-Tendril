import React, { useCallback, useEffect, useRef, useState } from "react";
import { ShellContext } from "./ShellContext";
import { ShellWidgetProps, isModKey } from "./types";
import "./shell.css";

interface TendrilShellProps extends ShellWidgetProps {
  collapsed?: boolean;
  activeSessionIndex?: number | null;
  slots?: {
    SidebarHeader?: React.ReactNode;
    SidebarBody?: React.ReactNode;
    SidebarFooter?: React.ReactNode;
    Content?: React.ReactNode;
    SessionContents?: React.ReactNode;
    Tabs?: React.ReactNode;
  };
}

/**
 * The Tendril app chrome: sidebar (expanded / icon rail), the rounded content
 * frame, and the session tab strip below it. Collapse is client-side for a
 * smooth animation; the server is notified through OnCollapsedChanged so the
 * state can be persisted. Session panes all stay mounted — only the active one
 * is visible — so agent terminals keep their buffers when switching tabs.
 */
export const TendrilShell: React.FC<TendrilShellProps> = ({
  id,
  events = [],
  eventHandler,
  collapsed: collapsedProp = false,
  activeSessionIndex,
  slots,
}) => {
  const [collapsed, setCollapsed] = useState(collapsedProp);
  const prevPropRef = useRef(collapsedProp);
  if (collapsedProp !== prevPropRef.current) {
    prevPropRef.current = collapsedProp;
    if (collapsedProp !== collapsed) setCollapsed(collapsedProp);
  }

  const toggle = useCallback(() => {
    setCollapsed((prev) => {
      const next = !prev;
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

  const sessionPanes = React.Children.toArray(slots?.SessionContents ?? []);
  const hasActiveSession =
    activeSessionIndex != null && activeSessionIndex >= 0 && activeSessionIndex < sessionPanes.length;

  return (
    <ShellContext.Provider value={{ collapsed, toggle }}>
      <div className="tsh-root" data-collapsed={collapsed}>
        <div className="tsh-sidebar">
          <div className="tsh-sidebar-header">{slots?.SidebarHeader}</div>
          <div className="tsh-sidebar-body">{slots?.SidebarBody}</div>
          <div className="tsh-sidebar-footer">{slots?.SidebarFooter}</div>
        </div>
        <div className="tsh-main">
          <div className="tsh-frame">
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
          {slots?.Tabs && <div className="tsh-tabs-row">{slots.Tabs}</div>}
          <div className="tsh-bottom-spacer" />
        </div>
      </div>
    </ShellContext.Provider>
  );
};
