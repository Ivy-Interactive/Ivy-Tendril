import React, { useContext } from "react";
import * as TooltipPrimitive from "@radix-ui/react-tooltip";
import { Kbd, formatShortcut } from "./Kbd";
import "./ui.css";

export { formatShortcut };

/* Radix arbitrates hover hand-off (and the "one tooltip at a time" rule) inside a Provider, so
   a Provider per tooltip means every button in a row waits out the full delay again. A widget
   root wraps its subtree in TooltipScope once; tooltips inside it then share that Provider, and
   a tooltip mounted outside any scope still brings its own. */
const InScope = React.createContext(false);

export interface TooltipScopeProps {
  children: React.ReactNode;
  delayDuration?: number;
  skipDelayDuration?: number;
}

export const TooltipScope: React.FC<TooltipScopeProps> = ({
  children,
  delayDuration = 500,
  skipDelayDuration = 300,
}) => {
  /* A widget hosted inside another widget's scope reuses it rather than nesting a second one. */
  if (useContext(InScope)) return <>{children}</>;

  return (
    <TooltipPrimitive.Provider delayDuration={delayDuration} skipDelayDuration={skipDelayDuration}>
      <InScope.Provider value={true}>{children}</InScope.Provider>
    </TooltipPrimitive.Provider>
  );
};

export type TooltipSide = "top" | "right" | "bottom" | "left";

export interface TooltipProps {
  /** The tooltip body. Nothing renders when it is empty. */
  content?: React.ReactNode;
  children: React.ReactNode;
  side?: TooltipSide;
  sideOffset?: number;
  /** Shortcut hint shown as a key cap next to the label, e.g. "⌘+K" or ["Ctrl", "K"]. */
  shortcut?: string | string[];
  /** False renders the trigger alone — for a label that is only a tooltip while collapsed. */
  enabled?: boolean;
  /**
   * Anchors the tooltip on a wrapper instead of the trigger itself. Pass it for a control that
   * can be disabled: a disabled button emits no pointer events, which is exactly when its
   * tooltip matters most, since it says why the control cannot be used. It is a property of the
   * control, not of its current state — flipping it would remount the trigger, so pass
   * `triggerDisabled` for the state.
   */
  wrapTrigger?: boolean;
  /** Whether the wrapped control is disabled right now; only then is the wrapper focusable. */
  triggerDisabled?: boolean;
  delayDuration?: number;
  skipDelayDuration?: number;
  className?: string;
  asChild?: boolean;
  open?: boolean;
  defaultOpen?: boolean;
  onOpenChange?: (open: boolean) => void;
}

/**
 * The bundle's one tooltip: a portaled Radix tooltip with the shared card styling and an
 * optional key cap. Every widget uses it instead of a native `title`, so hover timing, styling
 * and placement match wherever a control lives.
 */
export const Tooltip: React.FC<TooltipProps> = ({
  content,
  children,
  side = "top",
  sideOffset = 8,
  shortcut,
  enabled = true,
  wrapTrigger = false,
  triggerDisabled = false,
  delayDuration = 500,
  skipDelayDuration = 300,
  className = "",
  asChild = true,
  open,
  defaultOpen,
  onOpenChange,
}) => {
  const scoped = useContext(InScope);

  if (enabled === false || !content) {
    return <>{children}</>;
  }

  const root = (
    <TooltipPrimitive.Root open={open} defaultOpen={defaultOpen} onOpenChange={onOpenChange}>
      <TooltipPrimitive.Trigger asChild={asChild || wrapTrigger}>
        {wrapTrigger ? (
          /* Focusable only while the control is: otherwise the wrapper would be a tab stop
             in front of a perfectly reachable button. */
          <span className="tui-tooltip-trigger-wrap" tabIndex={triggerDisabled ? 0 : undefined}>
            {children}
          </span>
        ) : (
          children
        )}
      </TooltipPrimitive.Trigger>
      <TooltipPrimitive.Portal>
        <TooltipPrimitive.Content
          side={side}
          sideOffset={sideOffset}
          className={`tui-tooltip-content ${className}`.trim()}
        >
          <div className="tui-tooltip-inner">
            {typeof content === "string" ? (
              <span className="tui-tooltip-label">{content}</span>
            ) : (
              content
            )}
            {shortcut && <Kbd keys={shortcut} variant="boxed" />}
          </div>
        </TooltipPrimitive.Content>
      </TooltipPrimitive.Portal>
    </TooltipPrimitive.Root>
  );

  if (scoped) return root;

  return (
    <TooltipPrimitive.Provider delayDuration={delayDuration} skipDelayDuration={skipDelayDuration}>
      {root}
    </TooltipPrimitive.Provider>
  );
};
