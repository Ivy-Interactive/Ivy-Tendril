import React from "react";
import { Tooltip, TooltipProps, formatShortcut } from "../ui/Tooltip";
import "./shell.css";

export type ShellTooltipProps = TooltipProps;
export { formatShortcut };

/**
 * The shared tooltip with the shell's default placement. Everything else about it — styling,
 * hover timing, the shortcut key cap — comes from `ui/Tooltip`, so a shell tooltip and a chat
 * tooltip are the same tooltip.
 */
export const ShellTooltip: React.FC<ShellTooltipProps> = ({ side = "right", ...rest }) => (
  <Tooltip side={side} {...rest} />
);
