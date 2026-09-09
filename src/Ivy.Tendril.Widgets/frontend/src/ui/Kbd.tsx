import React from "react";
import "./ui.css";

export type KbdVariant = "bare" | "boxed";

export interface KbdProps {
  /** "⌘+K", ["Ctrl", "K"] or a single key. */
  keys: string | string[];
  /** "boxed" draws a key cap (tooltips, toolbars); "bare" is the letters alone. */
  variant?: KbdVariant;
  className?: string;
}

/** Splits "⌘+K" into its keys; an array passes through unchanged. */
export const splitKeys = (keys: string | string[]): string[] =>
  (Array.isArray(keys) ? keys : keys.split("+")).map((k) => k.trim()).filter(Boolean);

/**
 * One line of shortcut text: single-character keys are glued with thin spaces (⌘⌥N), named
 * keys keep their plus signs (Ctrl+Alt+N).
 */
export const formatShortcut = (keys: string | string[]): string => {
  const parts = splitKeys(keys);
  if (parts.length === 0) return "";
  return parts.every((k) => k.length === 1) ? parts.join("\u2009") : parts.join("+");
};

/**
 * The bundle's one keyboard hint. Bare renders a span per key so a caller can space or hide
 * them; boxed renders the formatted line inside a single key cap.
 */
export const Kbd: React.FC<KbdProps> = ({ keys, variant = "boxed", className = "" }) => {
  const parts = splitKeys(keys);
  if (parts.length === 0) return null;

  const classes = `tui-kbd tui-kbd--${variant} ${className}`.trim();

  if (variant === "bare") {
    return (
      <span className={classes}>
        {parts.map((key, index) => (
          <span key={`${key}-${index}`}>{key}</span>
        ))}
      </span>
    );
  }

  return <kbd className={classes}>{formatShortcut(parts)}</kbd>;
};
