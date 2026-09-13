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

/**
 * Brand row and collapse control. The same markup serves both widths - the rail
 * simply clips the title and the close button away - so the logo never moves.
 * In the rail the logo doubles as the expand control: hovering fades the logo
 * out and reveals the panel icon in its place.
 */
export const ShellSidebarHeader: React.FC<ShellSidebarHeaderProps> = ({
  title = "Ivy Tendril",
  version,
  logoUrl,
}) => {
  const { collapsed, toggle } = useShell();
  const shortcut = `${modKeyLabel()}+B`;

  return (
    <div className="tsh-header">
      <div className="tsh-row tsh-header-row">
        <div className="tsh-header-brand">
          <ShellTooltip content="Open sidebar" shortcut={shortcut} enabled={collapsed} side="right">
            <button
              className="tsh-logo-toggle"
              onClick={toggle}
              aria-label="Open sidebar"
              aria-hidden={!collapsed}
              tabIndex={collapsed ? 0 : -1}
            >
              {logoUrl && <img className="tsh-header-logo" src={logoUrl} alt="" />}
              <span className="tsh-logo-toggle-icon">
                <PanelLeftOpen size={16} />
              </span>
            </button>
          </ShellTooltip>
          <div className="tsh-header-text">
            <span className="tsh-header-title">{title}</span>
            {version && <span className="tsh-header-version">{version}</span>}
          </div>
        </div>
        <ShellTooltip content="Close sidebar" shortcut={shortcut} enabled={!collapsed} side="right">
          <button
            className="tsh-header-toggle"
            onClick={toggle}
            aria-label="Close sidebar"
            aria-hidden={collapsed}
            tabIndex={collapsed ? -1 : 0}
          >
            <PanelLeftClose size={16} />
          </button>
        </ShellTooltip>
      </div>
    </div>
  );
};
