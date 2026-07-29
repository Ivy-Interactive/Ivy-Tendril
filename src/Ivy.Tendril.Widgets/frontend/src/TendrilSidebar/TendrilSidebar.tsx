import { useState, useEffect, useRef } from "react";
import {
  PanelLeft,
  Plus,
  Sparkles,
  LayoutDashboard,
  Feather,
  ThumbsUp,
  Lightbulb,
  Activity,
  ChevronDown,
  ChevronUp,
  GitPullRequest,
  Snowflake,
  HelpCircle,
  Settings,
} from "lucide-react";
import type { TendrilSidebarProps } from "./types";
import "./tendril-sidebar.css";

export function TendrilSidebar({
  id,
  version = "v 1.0.20",
  agentName = "Claude Code",
  agentShortcut = "⌘ A",
  newPlanShortcut = "⌘ K",
  activeItem = "dashboard",
  draftCount,
  reviewCount,
  recommendationsCount,
  jobs = [],
  pullRequestCount,
  iceboxCount,
  helpRequestCount,
  collapsed = false,
  showCollapseButton = false,
  eventHandler,
}: TendrilSidebarProps) {
  const [jobsExpanded, setJobsExpanded] = useState(true);
  const containerRef = useRef<HTMLDivElement>(null);
  const [isNarrow, setIsNarrow] = useState(false);

  const isMac = typeof navigator !== "undefined" && (
    navigator.platform?.toUpperCase().indexOf("MAC") >= 0 ||
    navigator.userAgent?.toUpperCase().indexOf("MAC") >= 0
  );

  const formatShortcut = (shortcut?: string) => {
    if (!shortcut) return "";
    if (!isMac) {
      return shortcut.replace(/⌘/g, "Ctrl");
    }
    return shortcut;
  };

  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;

    const observer = new ResizeObserver((entries) => {
      for (const entry of entries) {
        setIsNarrow(entry.contentRect.width <= 180);
      }
    });

    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  const isCollapsed = collapsed || isNarrow;

  const fireEvent = (eventName: string, ...args: unknown[]) => {
    if (eventHandler) {
      eventHandler(eventName, id, args);
    }
  };

  const handleSelect = (itemKey: string) => {
    fireEvent("OnSelect", itemKey);
  };

  const handleNewPlan = () => {
    fireEvent("OnNewPlan");
  };

  const handleSelectAgent = () => {
    fireEvent("OnSelectAgent");
  };

  const handleToggleCollapse = () => {
    fireEvent("OnToggleCollapse");
  };

  // Keyboard shortcut listener for Ctrl+K / Cmd+K and Ctrl+A / Cmd+A
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      const isCmdOrCtrl = isMac ? e.metaKey : e.ctrlKey;
      if (isCmdOrCtrl && e.key.toLowerCase() === "k") {
        e.preventDefault();
        handleNewPlan();
      } else if (isCmdOrCtrl && e.key.toLowerCase() === "a") {
        const target = e.target as HTMLElement;
        if (target && (target.tagName === "INPUT" || target.tagName === "TEXTAREA" || target.isContentEditable)) {
          return;
        }
        e.preventDefault();
        handleSelectAgent();
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [isMac]);

  return (
    <div ref={containerRef} className={`tendril-sidebar ${isCollapsed ? "collapsed" : ""}`}>
      {/* Header */}
      <div className="tendril-sidebar-header">
        <div className="tendril-sidebar-brand" title="Ivy Tendril">
          <img src="/tendril/assets/Tendril.svg" alt="Ivy Tendril" className="tendril-sidebar-logo" />
          {!isCollapsed && (
            <div className="tendril-sidebar-title-box">
              <span className="tendril-sidebar-title">Ivy Tendril</span>
              <span className="tendril-sidebar-version">{version}</span>
            </div>
          )}
        </div>
        {showCollapseButton && !isCollapsed && (
          <button
            type="button"
            className="tendril-sidebar-collapse-btn"
            onClick={handleToggleCollapse}
            title="Toggle Sidebar"
          >
            <PanelLeft size={18} />
          </button>
        )}
      </div>

      {/* Action Buttons */}
      <div className="tendril-sidebar-actions">
        <button
          type="button"
          className={`tendril-sidebar-new-plan-btn ${isCollapsed ? "icon-only" : ""}`}
          onClick={handleNewPlan}
          title={isCollapsed ? "New Plan" : undefined}
        >
          <div className="tendril-sidebar-btn-left">
            <Plus size={16} />
            {!isCollapsed && <span>New Plan</span>}
          </div>
          {!isCollapsed && newPlanShortcut && (
            <span className="tendril-sidebar-shortcut">{formatShortcut(newPlanShortcut)}</span>
          )}
        </button>

        <div
          className={`tendril-sidebar-agent-item ${activeItem === "agent" ? "active" : ""} ${isCollapsed ? "icon-only" : ""}`}
          onClick={handleSelectAgent}
          title={isCollapsed ? agentName : undefined}
        >
          <div className="tendril-sidebar-btn-left">
            <Sparkles size={16} />
            {!isCollapsed && <span>{agentName}</span>}
          </div>
          {!isCollapsed && agentShortcut && (
            <span className="tendril-sidebar-shortcut">{formatShortcut(agentShortcut)}</span>
          )}
        </div>
      </div>

      {/* Navigation List */}
      <div className="tendril-sidebar-nav">
        <div
          className={`tendril-sidebar-item ${activeItem === "dashboard" ? "active" : ""}`}
          onClick={() => handleSelect("dashboard")}
          title={isCollapsed ? "Dashboard" : undefined}
        >
          <div className="tendril-sidebar-item-left">
            <LayoutDashboard className="tendril-sidebar-item-icon" />
            {!isCollapsed && <span>Dashboard</span>}
          </div>
        </div>

        <div
          className={`tendril-sidebar-item ${activeItem === "drafts" ? "active" : ""}`}
          onClick={() => handleSelect("drafts")}
          title={isCollapsed ? "Drafts" : undefined}
        >
          <div className="tendril-sidebar-item-left">
            <Feather className="tendril-sidebar-item-icon" />
            {!isCollapsed && <span>Drafts</span>}
          </div>
          {!isCollapsed && draftCount !== undefined && draftCount > 0 && (
            <span className="tendril-sidebar-badge">{draftCount}</span>
          )}
        </div>

        <div
          className={`tendril-sidebar-item ${activeItem === "review" ? "active" : ""}`}
          onClick={() => handleSelect("review")}
          title={isCollapsed ? "Review" : undefined}
        >
          <div className="tendril-sidebar-item-left">
            <ThumbsUp className="tendril-sidebar-item-icon" />
            {!isCollapsed && <span>Review</span>}
          </div>
          {!isCollapsed && reviewCount !== undefined && reviewCount > 0 && (
            <span className="tendril-sidebar-badge">{reviewCount}</span>
          )}
        </div>

        <div
          className={`tendril-sidebar-item ${activeItem === "recommendations" ? "active" : ""}`}
          onClick={() => handleSelect("recommendations")}
          title={isCollapsed ? "Recommendations" : undefined}
        >
          <div className="tendril-sidebar-item-left">
            <Lightbulb className="tendril-sidebar-item-icon" />
            {!isCollapsed && <span>Recommendations</span>}
          </div>
          {!isCollapsed && recommendationsCount !== undefined && recommendationsCount > 0 && (
            <span className="tendril-sidebar-badge">{recommendationsCount}</span>
          )}
        </div>

        {!isCollapsed && <div className="tendril-sidebar-divider" />}

        {/* Jobs Accordion Group */}
        <div className="tendril-sidebar-group">
          <div
            className={`tendril-sidebar-group-header ${activeItem === "jobs" ? "active" : ""}`}
            onClick={() => {
              handleSelect("jobs");
              if (!isCollapsed) setJobsExpanded(!jobsExpanded);
            }}
            title={isCollapsed ? "Jobs" : undefined}
          >
            <div className="tendril-sidebar-item-left">
              <Activity className="tendril-sidebar-item-icon" />
              {!isCollapsed && <span>Jobs</span>}
            </div>
            {!isCollapsed && (
              jobsExpanded ? <ChevronUp size={16} /> : <ChevronDown size={16} />
            )}
          </div>

          {!isCollapsed && jobsExpanded && jobs && jobs.length > 0 && (
            <div className="tendril-sidebar-group-items">
              {jobs.map((job) => (
                <div
                  key={job.id}
                  className={`tendril-sidebar-subitem ${activeItem === `job:${job.id}` ? "active" : ""}`}
                  onClick={() => handleSelect(`job:${job.id}`)}
                >
                  <span>{job.name}</span>
                  {job.count !== undefined && job.count > 0 && (
                    <span className="tendril-sidebar-badge">{job.count}</span>
                  )}
                </div>
              ))}
            </div>
          )}
        </div>

        {/* Lower Navigation Items */}
        <div
          className={`tendril-sidebar-item ${activeItem === "pull-requests" ? "active" : ""}`}
          onClick={() => handleSelect("pull-requests")}
          title={isCollapsed ? "Pull Requests" : undefined}
        >
          <div className="tendril-sidebar-item-left">
            <GitPullRequest className="tendril-sidebar-item-icon" />
            {!isCollapsed && <span>Pull Requests</span>}
          </div>
          {!isCollapsed && pullRequestCount !== undefined && pullRequestCount > 0 && (
            <span className="tendril-sidebar-badge">{pullRequestCount}</span>
          )}
        </div>

        <div
          className={`tendril-sidebar-item ${activeItem === "icebox" ? "active" : ""}`}
          onClick={() => handleSelect("icebox")}
          title={isCollapsed ? "Icebox" : undefined}
        >
          <div className="tendril-sidebar-item-left">
            <Snowflake className="tendril-sidebar-item-icon" />
            {!isCollapsed && <span>Icebox</span>}
          </div>
          {!isCollapsed && iceboxCount !== undefined && iceboxCount > 0 && (
            <span className="tendril-sidebar-badge">{iceboxCount}</span>
          )}
        </div>

        <div
          className={`tendril-sidebar-item ${activeItem === "help" ? "active" : ""}`}
          onClick={() => handleSelect("help")}
          title={isCollapsed ? "Help Requests" : undefined}
        >
          <div className="tendril-sidebar-item-left">
            <HelpCircle className="tendril-sidebar-item-icon" />
            {!isCollapsed && <span>Help Requests</span>}
          </div>
          {!isCollapsed && helpRequestCount !== undefined && helpRequestCount > 0 && (
            <span className="tendril-sidebar-badge">{helpRequestCount}</span>
          )}
        </div>
      </div>

      {/* Footer / Settings */}
      <div className="tendril-sidebar-footer">
        {!isCollapsed && <div className="tendril-sidebar-divider" />}
        <div
          className={`tendril-sidebar-item ${activeItem === "settings" ? "active" : ""}`}
          onClick={() => handleSelect("settings")}
          title={isCollapsed ? "Settings" : undefined}
        >
          <div className="tendril-sidebar-item-left">
            <Settings className="tendril-sidebar-item-icon" />
            {!isCollapsed && <span>Settings</span>}
          </div>
        </div>
      </div>
    </div>
  );
}

