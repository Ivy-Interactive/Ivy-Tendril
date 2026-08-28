import React from "react";
import {
  Activity,
  ChartBar,
  Feather,
  GitPullRequest,
  Lightbulb,
  LucideIcon,
  MessageSquare,
  Snowflake,
  ThumbsUp,
  Trash2,
} from "lucide-react";
import { useShell } from "./ShellContext";
import { ShellNavItemDto, ShellWidgetProps } from "./types";
import "./shell.css";

interface ShellNavProps extends ShellWidgetProps {
  items?: ShellNavItemDto[];
  showDivider?: boolean;
}

/* The nav renders the app menu, so only icons used by [App] attributes are
   bundled; unknown names fall back to the label's initial. */
const navIcons: Record<string, LucideIcon> = {
  ChartBar,
  Feather,
  ThumbsUp,
  Lightbulb,
  Activity,
  GitPullRequest,
  Snowflake,
  MessageSquare,
  Trash2,
};

const NavIcon: React.FC<{ icon?: string; label: string }> = ({ icon, label }) => {
  const Icon = icon ? navIcons[icon] : undefined;
  if (Icon) return <Icon size={16} />;
  return <span style={{ fontSize: 12, fontWeight: 500 }}>{label.charAt(0).toUpperCase()}</span>;
};

export const ShellNav: React.FC<ShellNavProps> = ({
  id,
  events = [],
  eventHandler,
  items = [],
  showDivider = false,
}) => {
  const { collapsed } = useShell();

  const select = (itemId: string) => {
    if (events.includes("OnSelect")) eventHandler("OnSelect", id, [itemId]);
  };

  return (
    <nav className="tsh-nav">
      {items.map((item) => (
        <button
          key={item.id}
          className="tsh-nav-item"
          data-active={item.isActive === true}
          data-menu-item={item.id}
          onClick={() => select(item.id)}
          title={collapsed ? item.label : undefined}
        >
          <span className="tsh-nav-item-main">
            <span className="tsh-nav-icon">
              <NavIcon icon={item.icon} label={item.label} />
            </span>
            <span className="tsh-nav-label">{item.label}</span>
          </span>
          {item.badge && <span className="tsh-nav-badge">{item.badge}</span>}
        </button>
      ))}
      {showDivider && <div className="tsh-nav-divider" />}
    </nav>
  );
};
