import React from "react";
import { PanelLeftClose, PanelLeftOpen } from "lucide-react";
import { useShell } from "./ShellContext";
import { ShellWidgetProps, modKeyLabel } from "./types";
import { ShellTooltip } from "./ShellTooltip";
import "./shell.css";

interface ShellSidebarHeaderProps extends ShellWidgetProps {
  title?: string;
  version?: string;
  logoUrl?: string;
}

export const ShellSidebarHeader: React.FC<ShellSidebarHeaderProps> = ({
  title = "Ivy Tendril",
  version,
  logoUrl,
}) => {
  const { collapsed, toggle } = useShell();

  if (collapsed) {
    // In the rail the logo doubles as the expand control: hovering fades the
    // logo out and reveals the panel icon in its place.
    return (
      <div className="tsh-header">
        <ShellTooltip content="Open sidebar" shortcut={`${modKeyLabel()}+B`} side="right">
          <button className="tsh-logo-toggle" onClick={toggle} aria-label="Open sidebar">
            {logoUrl && <img className="tsh-header-logo" src={logoUrl} alt="" />}
            <span className="tsh-logo-toggle-icon">
              <PanelLeftOpen size={16} />
            </span>
          </button>
        </ShellTooltip>
      </div>
    );
  }

  return (
    <div className="tsh-header">
      <div className="tsh-header-brand">
        {logoUrl && <img className="tsh-header-logo" src={logoUrl} alt="" />}
        <div className="tsh-header-text">
          <span className="tsh-header-title">{title}</span>
          {version && <span className="tsh-header-version">{version}</span>}
        </div>
      </div>
      <ShellTooltip content="Close sidebar" shortcut={`${modKeyLabel()}+B`} side="right">
        <button className="tsh-header-toggle" onClick={toggle} aria-label="Close sidebar">
          <PanelLeftClose size={16} />
        </button>
      </ShellTooltip>
    </div>
  );
};
