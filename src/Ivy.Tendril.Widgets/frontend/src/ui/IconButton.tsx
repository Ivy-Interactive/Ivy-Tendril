import React from "react";
import { Tooltip, TooltipSide } from "./Tooltip";
import "./ui.css";

export type IconButtonSize = "sm" | "md" | "lg";
export type IconButtonVariant = "ghost" | "danger" | "solid";

export interface IconButtonProps
  extends Omit<React.ButtonHTMLAttributes<HTMLButtonElement>, "title" | "children"> {
  /** Accessible name, and the tooltip text unless `tooltip` overrides it. */
  label: string;
  /** The icon. */
  children: React.ReactNode;
  /** Replaces the tooltip text; false suppresses the tooltip entirely. */
  tooltip?: React.ReactNode | false;
  shortcut?: string | string[];
  tooltipSide?: TooltipSide;
  size?: IconButtonSize;
  variant?: IconButtonVariant;
  /** Held-open look for a button whose panel is showing. */
  active?: boolean;
  className?: string;
}

/**
 * The bundle's one icon button: a square hit target with the shared hover surface and a shared
 * tooltip instead of a native `title`, so every icon control in the app behaves the same.
 */
export const IconButton: React.FC<IconButtonProps> = ({
  label,
  children,
  tooltip,
  shortcut,
  tooltipSide = "top",
  size = "lg",
  variant = "ghost",
  active,
  className = "",
  type = "button",
  ...rest
}) => {
  const button = (
    <button
      {...rest}
      type={type}
      className={`tui-icon-btn ${className}`.trim()}
      data-size={size}
      data-variant={variant}
      data-active={active ? "true" : undefined}
      aria-label={label}
    >
      {children}
    </button>
  );

  if (tooltip === false) return button;

  return (
    <Tooltip
      content={tooltip ?? label}
      shortcut={shortcut}
      side={tooltipSide}
      wrapTrigger={"disabled" in rest}
      triggerDisabled={Boolean(rest.disabled)}
    >
      {button}
    </Tooltip>
  );
};
