import { useState } from "react";
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

  return (
    <div className={`tendril-sidebar ${collapsed ? "collapsed" : ""}`}>
      {/* Header */}
      <div className="tendril-sidebar-header">
        <div className="tendril-sidebar-brand">
          <svg className="tendril-sidebar-logo" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
            <path d="M12 2a10 10 0 1 0 10 10H12V2z" fill="#059669" opacity="0.2" />
            <path d="M4.5 9.5C6 4.5 11 3 15.5 5.5C19.5 7.5 21 12 19 16C17.5 19 13.5 21 9.5 19.5C6 18 4.5 14.5 6 11.5C7 9.5 9.5 8.5 11.5 9.5" />
          </svg>
          {!collapsed && (
            <div className="tendril-sidebar-title-box">
              <span className="tendril-sidebar-title">Ivy Tendril</span>
              <span className="tendril-sidebar-version">{version}</span>
            </div>
          )}
        </div>
        {showCollapseButton && (
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
      {!collapsed && (
        <div className="tendril-sidebar-actions">
          <button
            type="button"
            className="tendril-sidebar-new-plan-btn"
            onClick={handleNewPlan}
          >
            <div className="tendril-sidebar-btn-left">
              <Plus size={16} />
              <span>New Plan</span>
            </div>
            {newPlanShortcut && (
              <span className="tendril-sidebar-shortcut">{newPlanShortcut}</span>
            )}
          </button>

          <div
            className={`tendril-sidebar-agent-item ${activeItem === "agent" ? "active" : ""}`}
            onClick={handleSelectAgent}
          >
            <div className="tendril-sidebar-btn-left">
              <Sparkles size={16} />
              <span>{agentName}</span>
            </div>
            {agentShortcut && (
              <span className="tendril-sidebar-shortcut">{agentShortcut}</span>
            )}
          </div>
        </div>
      )}

      {/* Navigation List */}
      <div className="tendril-sidebar-nav">
        <div
          className={`tendril-sidebar-item ${activeItem === "dashboard" ? "active" : ""}`}
          onClick={() => handleSelect("dashboard")}
        >
          <div className="tendril-sidebar-item-left">
            <LayoutDashboard className="tendril-sidebar-item-icon" />
            {!collapsed && <span>Dashboard</span>}
          </div>
        </div>

        <div
          className={`tendril-sidebar-item ${activeItem === "drafts" ? "active" : ""}`}
          onClick={() => handleSelect("drafts")}
        >
          <div className="tendril-sidebar-item-left">
            <Feather className="tendril-sidebar-item-icon" />
            {!collapsed && <span>Drafts</span>}
          </div>
          {!collapsed && draftCount !== undefined && draftCount > 0 && (
            <span className="tendril-sidebar-badge">{draftCount}</span>
          )}
        </div>

        <div
          className={`tendril-sidebar-item ${activeItem === "review" ? "active" : ""}`}
          onClick={() => handleSelect("review")}
        >
          <div className="tendril-sidebar-item-left">
            <ThumbsUp className="tendril-sidebar-item-icon" />
            {!collapsed && <span>Review</span>}
          </div>
          {!collapsed && reviewCount !== undefined && reviewCount > 0 && (
            <span className="tendril-sidebar-badge">{reviewCount}</span>
          )}
        </div>

        <div
          className={`tendril-sidebar-item ${activeItem === "recommendations" ? "active" : ""}`}
          onClick={() => handleSelect("recommendations")}
        >
          <div className="tendril-sidebar-item-left">
            <Lightbulb className="tendril-sidebar-item-icon" />
            {!collapsed && <span>Recommendations</span>}
          </div>
          {!collapsed && recommendationsCount !== undefined && recommendationsCount > 0 && (
            <span className="tendril-sidebar-badge">{recommendationsCount}</span>
          )}
        </div>

        {!collapsed && <div className="tendril-sidebar-divider" />}

        {/* Jobs Accordion Group */}
        <div className="tendril-sidebar-group">
          <div
            className={`tendril-sidebar-group-header ${activeItem === "jobs" ? "active" : ""}`}
            onClick={() => {
              handleSelect("jobs");
              setJobsExpanded(!jobsExpanded);
            }}
          >
            <div className="tendril-sidebar-item-left">
              <Activity className="tendril-sidebar-item-icon" />
              {!collapsed && <span>Jobs</span>}
            </div>
            {!collapsed && (
              jobsExpanded ? <ChevronUp size={16} /> : <ChevronDown size={16} />
            )}
          </div>

          {!collapsed && jobsExpanded && jobs && jobs.length > 0 && (
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
        >
          <div className="tendril-sidebar-item-left">
            <GitPullRequest className="tendril-sidebar-item-icon" />
            {!collapsed && <span>Pull Requests</span>}
          </div>
          {!collapsed && pullRequestCount !== undefined && pullRequestCount > 0 && (
            <span className="tendril-sidebar-badge">{pullRequestCount}</span>
          )}
        </div>

        <div
          className={`tendril-sidebar-item ${activeItem === "icebox" ? "active" : ""}`}
          onClick={() => handleSelect("icebox")}
        >
          <div className="tendril-sidebar-item-left">
            <Snowflake className="tendril-sidebar-item-icon" />
            {!collapsed && <span>Icebox</span>}
          </div>
          {!collapsed && iceboxCount !== undefined && iceboxCount > 0 && (
            <span className="tendril-sidebar-badge">{iceboxCount}</span>
          )}
        </div>

        <div
          className={`tendril-sidebar-item ${activeItem === "help" ? "active" : ""}`}
          onClick={() => handleSelect("help")}
        >
          <div className="tendril-sidebar-item-left">
            <HelpCircle className="tendril-sidebar-item-icon" />
            {!collapsed && <span>Help Requests</span>}
          </div>
          {!collapsed && helpRequestCount !== undefined && helpRequestCount > 0 && (
            <span className="tendril-sidebar-badge">{helpRequestCount}</span>
          )}
        </div>
      </div>
    </div>
  );
}
