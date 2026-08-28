import React, { useCallback, useEffect } from "react";
import { Plus } from "lucide-react";
import { useShell } from "./ShellContext";
import { ShellWidgetProps, isModKey, modKeyLabel } from "./types";
import "./shell.css";

interface ShellNewPlanButtonProps extends ShellWidgetProps {
  label?: string;
  shortcutKey?: string;
}

export const ShellNewPlanButton: React.FC<ShellNewPlanButtonProps> = ({
  id,
  events = [],
  eventHandler,
  label = "New Plan",
  shortcutKey = "K",
}) => {
  const { collapsed } = useShell();

  const fire = useCallback(() => {
    if (events.includes("OnClick")) eventHandler("OnClick", id, []);
  }, [events, eventHandler, id]);

  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if (isModKey(e) && !e.shiftKey && !e.altKey && e.key.toLowerCase() === shortcutKey.toLowerCase()) {
        e.preventDefault();
        fire();
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [fire, shortcutKey]);

  return (
    <div className="tsh-newplan-wrap">
      <button
        className="tsh-newplan"
        onClick={fire}
        title={collapsed ? `${label} (${modKeyLabel()}${shortcutKey})` : undefined}
      >
        <span className="tsh-newplan-label-group">
          <Plus size={16} />
          <span className="tsh-newplan-label">{label}</span>
        </span>
        <span className="tsh-kbd">
          <span>{modKeyLabel()}</span>
          <span>{shortcutKey}</span>
        </span>
      </button>
    </div>
  );
};
