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
        aria-label={collapsed ? "Open sidebar" : "Close sidebar"}
        title={collapsed ? "Open sidebar (Ctrl+B)" : "Close sidebar (Ctrl+B)"}
      >
        {collapsed ? <PanelLeftOpen size={16} /> : <PanelLeftClose size={16} />}
      </button>
    </div>
  );
};
