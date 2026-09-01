import React from "react";
import { PanelLeftClose, PanelLeftOpen } from "lucide-react";
import { useShell } from "./ShellContext";
import { ShellWidgetProps } from "./types";
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
        <button
          className="tsh-logo-toggle"
          onClick={toggle}
          aria-label="Open sidebar"
          title="Open sidebar (Ctrl+B)"
        >
          {logoUrl && <img className="tsh-header-logo" src={logoUrl} alt="" />}
          <span className="tsh-logo-toggle-icon">
            <PanelLeftOpen size={16} />
          </span>
        </button>
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
      <button
        className="tsh-header-toggle"
        onClick={toggle}
        aria-label="Close sidebar"
        title="Close sidebar (Ctrl+B)"
      >
        <PanelLeftClose size={16} />
      </button>
    </div>
  );
};
