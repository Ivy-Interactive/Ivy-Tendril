import React from "react";
import { Inbox, LucideIcon, Settings } from "lucide-react";
import { useShell } from "./ShellContext";
import { ShellWidgetProps } from "./types";
import { ShellTooltip } from "./ShellTooltip";
import "./shell.css";

interface ShellSettingsButtonProps extends ShellWidgetProps {
  label?: string;
  icon?: string;
  showLabel?: boolean;
  isActive?: boolean;
}

/* Only the icons the sidebar footer hosts are bundled; unknown names fall back
   to the settings cog. */
const footerIcons: Record<string, LucideIcon> = {
  Settings,
  Inbox,
};

/** Sidebar footer button. Fires OnClick when used standalone; when hosted as a
    DropDownMenu trigger the framework intercepts the click to open the menu.
    With the label hidden it is icon-only and names itself in a tooltip. */
export const ShellSettingsButton: React.FC<ShellSettingsButtonProps> = ({
  id,
  events = [],
  eventHandler,
  label = "Settings",
  icon = "Settings",
  showLabel = true,
  isActive = false,
}) => {
  const { collapsed } = useShell();
  const Icon = footerIcons[icon] ?? Settings;

  return (
    <ShellTooltip content={label} enabled={collapsed || !showLabel} side={collapsed ? "right" : "top"}>
      <button
        className="tsh-settings"
        data-icon-only={!showLabel}
        data-active={isActive}
        aria-label={label}
        onClick={() => {
          if (events.includes("OnClick")) eventHandler("OnClick", id, []);
        }}
      >
        <Icon size={16} />
        {showLabel && <span className="tsh-settings-label">{label}</span>}
      </button>
    </ShellTooltip>
  );
};
