import React from "react";
import { Settings } from "lucide-react";
import { ShellWidgetProps } from "./types";
import "./shell.css";

interface ShellSettingsButtonProps extends ShellWidgetProps {
  label?: string;
}

/** Sidebar footer row. Fires OnClick when used standalone; when hosted as a
    DropDownMenu trigger the framework intercepts the click to open the menu. */
export const ShellSettingsButton: React.FC<ShellSettingsButtonProps> = ({
  id,
  events = [],
  eventHandler,
  label = "Settings",
}) => {
  return (
    <button
      className="tsh-settings"
      title={label}
      onClick={() => {
        if (events.includes("OnClick")) eventHandler("OnClick", id, []);
      }}
    >
      <Settings size={16} />
      <span className="tsh-settings-label">{label}</span>
    </button>
  );
};
