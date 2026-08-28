import React from "react";
import { Plus } from "lucide-react";
import { useShell } from "./ShellContext";
import { ShellWidgetProps, isMac } from "./types";
import "./shell.css";

interface ShellNewPlanButtonProps extends ShellWidgetProps {
  label?: string;
}

/**
 * The primary New Plan CTA. The keyboard shortcut (Ctrl+Alt+N) is bound by
 * the server through the framework's ShortcutKey mechanism — this widget only
 * displays the hint and fires OnClick.
 */
export const ShellNewPlanButton: React.FC<ShellNewPlanButtonProps> = ({
  id,
  events = [],
  eventHandler,
  label = "New Plan",
}) => {
  const { collapsed } = useShell();
  const hintKeys = isMac() ? ["⌘", "⌥", "N"] : ["Ctrl", "Alt", "N"];

  const fire = () => {
    if (events.includes("OnClick")) eventHandler("OnClick", id, []);
  };

  return (
    <div className="tsh-newplan-wrap">
      <button
        className="tsh-newplan"
        onClick={fire}
        title={collapsed ? `${label} (${hintKeys.join("+")})` : undefined}
      >
        <span className="tsh-newplan-label-group">
          <Plus size={16} />
          <span className="tsh-newplan-label">{label}</span>
        </span>
        <span className="tsh-kbd">
          {hintKeys.map((key) => (
            <span key={key}>{key}</span>
          ))}
        </span>
      </button>
    </div>
  );
};
