import React from "react";
import * as TooltipPrimitive from "@radix-ui/react-tooltip";
import "./shell.css";

export interface ShellTooltipProps {
  content?: React.ReactNode;
  children: React.ReactNode;
  side?: "right" | "top" | "bottom" | "left";
  sideOffset?: number;
  shortcut?: string | string[];
  enabled?: boolean;
  delayDuration?: number;
  skipDelayDuration?: number;
  className?: string;
  asChild?: boolean;
  open?: boolean;
  defaultOpen?: boolean;
  onOpenChange?: (open: boolean) => void;
}

export const formatShortcut = (shortcut: string | string[]): string => {
  const parts = Array.isArray(shortcut)
    ? shortcut
    : shortcut
        .split("+")
        .map((s) => s.trim())
        .filter(Boolean);
  if (parts.length === 0) return "";
  const allSingle = parts.every((k) => k.length === 1);
  return allSingle ? parts.join("\u2009") : parts.join("+");
};

export const ShellTooltip: React.FC<ShellTooltipProps> = ({
  content,
  children,
  side = "right",
  sideOffset = 8,
  shortcut,
  enabled = true,
  delayDuration = 500,
  skipDelayDuration = 300,
  className = "",
  asChild = true,
  open,
  defaultOpen,
  onOpenChange,
}) => {
  if (enabled === false || !content) {
    return <>{children}</>;
  }

  const shortcutText = shortcut ? formatShortcut(shortcut) : undefined;

  return (
    <TooltipPrimitive.Provider delayDuration={delayDuration} skipDelayDuration={skipDelayDuration}>
      <TooltipPrimitive.Root open={open} defaultOpen={defaultOpen} onOpenChange={onOpenChange}>
        <TooltipPrimitive.Trigger asChild={asChild}>{children}</TooltipPrimitive.Trigger>
        <TooltipPrimitive.Portal>
          <TooltipPrimitive.Content
            side={side}
            sideOffset={sideOffset}
            className={`tsh-tooltip-content ${className}`.trim()}
          >
            <div className="tsh-tooltip-inner">
              {typeof content === "string" ? (
                <span className="tsh-tooltip-label">{content}</span>
              ) : (
                content
              )}
              {shortcutText && (
                <span className="tsh-tooltip-kbd-wrap">
                  <kbd className="tsh-tooltip-kbd">{shortcutText}</kbd>
                </span>
              )}
            </div>
          </TooltipPrimitive.Content>
        </TooltipPrimitive.Portal>
      </TooltipPrimitive.Root>
    </TooltipPrimitive.Provider>
  );
};
