import React, { useCallback, useRef, useState } from "react";
import {
  Activity,
  Check,
  CheckCircle2,
  ChevronDown,
  Cpu,
  Ellipsis,
  LoaderCircle,
  MessageSquarePlus,
  Pencil,
  Sparkles,
  Trash2,
  X,
  XCircle,
} from "lucide-react";
import { useOutsideClick } from "./useOutsideClick";
import type { ChatJobDto } from "./types";

const isRunningJob = (job: ChatJobDto) => job.status === "Running" || job.status === "Pending";
const isCompletedJob = (job: ChatJobDto) => job.status === "Completed";
const isFailedJob = (job: ChatJobDto) => job.status === "Failed" || job.status === "Timeout";

const jobsLabel = (count: number) => `${count} job${count === 1 ? "" : "s"}`;

interface JobsMenuProps {
  jobs: ChatJobDto[];
  spawned: boolean;
  onReview: () => void;
}

const JobsMenu: React.FC<JobsMenuProps> = ({ jobs, spawned, onReview }) => {
  const [open, setOpen] = useState(false);
  const wrapRef = useRef<HTMLDivElement>(null);
  const close = useCallback(() => setOpen(false), []);
  useOutsideClick(open, [wrapRef], close);

  const runningCount = jobs.filter(isRunningJob).length;
  const completedCount = jobs.filter(isCompletedJob).length;
  const failedCount = jobs.filter(isFailedJob).length;
  const allFinished = runningCount === 0 && jobs.length > 0;
  const state = runningCount > 0 ? "running" : failedCount > 0 ? "has-failed" : "all-completed";

  return (
    <div className="chat-jobs-badge-container" ref={wrapRef}>
      <button
        type="button"
        className={`chat-jobs-badge ${state}`}
        onClick={() => setOpen((value) => !value)}
        title={runningCount > 0 ? `${runningCount} job(s) running` : "View jobs"}
        aria-label="View running jobs"
        aria-expanded={open}
      >
        {runningCount > 0 ? (
          <>
            <LoaderCircle size={16} className="spin" />
            <span className="chat-jobs-badge-text">{runningCount} running</span>
            <span className="chat-jobs-pulse-dot" />
          </>
        ) : failedCount > 0 ? (
          <>
            <XCircle size={16} />
            <span className="chat-jobs-badge-text">
              {jobsLabel(jobs.length)} ({failedCount} failed)
            </span>
          </>
        ) : (
          <>
            <Activity size={16} />
            <span className="chat-jobs-badge-text">{jobsLabel(jobs.length)}</span>
          </>
        )}
        <ChevronDown size={16} className={`chat-jobs-badge-chevron ${open ? "open" : ""}`} />
      </button>

      {open && (
        <div className="chat-jobs-dropdown-menu">
          <div className="chat-jobs-dropdown-header">
            <div className="chat-jobs-dropdown-title">
              <Cpu size={14} />
              <span>
                {spawned ? "Spawned Jobs" : "Running Jobs"} ({jobs.length})
              </span>
            </div>
            <div className="chat-jobs-dropdown-chips">
              {runningCount > 0 && (
                <span className="chat-job-chip chip-running">
                  <LoaderCircle size={10} className="spin" />
                  {runningCount} running
                </span>
              )}
              {completedCount > 0 && (
                <span className="chat-job-chip chip-completed">
                  <Check size={10} />
                  {completedCount} completed
                </span>
              )}
              {failedCount > 0 && (
                <span className="chat-job-chip chip-failed">
                  <X size={10} />
                  {failedCount} failed
                </span>
              )}
            </div>
          </div>

          <div className="chat-jobs-dropdown-list">
            {jobs.map((job) => (
              <div key={job.id} className={`chat-jobs-dropdown-item ${job.status.toLowerCase()}`}>
                <div className="chat-job-status-indicator">
                  {isRunningJob(job) && <LoaderCircle size={13} className="spin" />}
                  {isCompletedJob(job) && <CheckCircle2 size={13} />}
                  {isFailedJob(job) && <XCircle size={13} />}
                  {!isRunningJob(job) && !isCompletedJob(job) && !isFailedJob(job) && <span className="chat-job-dot" />}
                </div>
                <div className="chat-job-details">
                  <div className="chat-job-meta">
                    <span className="chat-job-type">{job.type}</span>
                    <span className="chat-job-id">{job.id}</span>
                    {job.planTitle && (
                      <span className="chat-job-plan-title" title={job.planTitle}>
                        {job.planTitle}
                      </span>
                    )}
                  </div>
                  {job.statusMessage && (
                    <div className="chat-job-message" title={job.statusMessage}>
                      {job.statusMessage}
                    </div>
                  )}
                </div>
              </div>
            ))}
          </div>

          {allFinished && (
            <div className="chat-jobs-dropdown-footer">
              <button
                type="button"
                className="chat-spawned-jobs-review-btn"
                onClick={() => {
                  setOpen(false);
                  onReview();
                }}
              >
                <Sparkles size={13} />
                Ask agent to review outcomes
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  );
};

export interface ChatHeaderProps {
  title: string;
  /** Shows the options menu (rename/delete). False while no session is selected. */
  editable: boolean;
  jobs?: ChatJobDto[];
  spawned?: boolean;
  variant?: "default" | "terminal";
  onRename?: (title: string) => void;
  onDelete?: () => void;
  onNewChat: () => void;
  onReviewJobs?: () => void;
  onOpenPlan?: (planId: string) => void;
}

/**
 * The chat thread's header: title (with inline rename), a new-chat button, the
 * options menu (rename/delete), and the jobs pill. Keyed by session id from
 * ChatWidget so a session switch remounts it and resets editing/menu state.
 */
export const ChatHeader: React.FC<ChatHeaderProps> = ({
  title,
  editable,
  jobs = [],
  spawned = false,
  variant = "default",
  onRename,
  onDelete,
  onNewChat,
  onReviewJobs,
  onOpenPlan: _onOpenPlan,
}) => {
  const [isEditingTitle, setIsEditingTitle] = useState(false);
  const [editingTitleText, setEditingTitleText] = useState("");
  const [menuOpen, setMenuOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);

  const closeMenu = useCallback(() => setMenuOpen(false), []);
  useOutsideClick(menuOpen, [menuRef], closeMenu);

  const startTitleEdit = () => {
    setEditingTitleText(title);
    setIsEditingTitle(true);
  };

  const saveTitleEdit = () => {
    const trimmed = editingTitleText.trim();
    if (trimmed && trimmed !== title) {
      onRename?.(trimmed);
    }
    setIsEditingTitle(false);
  };

  return (
    <div className={`chat-header ${variant === "terminal" ? "chat-header--terminal" : ""}`.trim()}>
      <div className="chat-header-title-wrap">
        {isEditingTitle ? (
          <input
            type="text"
            className="chat-title-input"
            aria-label="Chat name"
            value={editingTitleText}
            onChange={(e) => setEditingTitleText(e.target.value)}
            onBlur={saveTitleEdit}
            onKeyDown={(e) => {
              if (e.key === "Enter") saveTitleEdit();
              if (e.key === "Escape") setIsEditingTitle(false);
            }}
            autoFocus
          />
        ) : (
          <h1 className="chat-title" title={title}>
            {title}
          </h1>
        )}
      </div>
      <div className="chat-header-actions">
        <div className="chat-header-icons">
          <button
            type="button"
            className="chat-icon-btn"
            title="New chat"
            aria-label="New chat"
            onClick={onNewChat}
          >
            <MessageSquarePlus size={16} />
          </button>
          {editable && (
            <div className="chat-menu-wrap" ref={menuRef}>
              <button
                type="button"
                className="chat-icon-btn"
                title="Chat options"
                aria-label="Chat options"
                aria-haspopup="menu"
                aria-expanded={menuOpen}
                onClick={() => setMenuOpen((value) => !value)}
              >
                <Ellipsis size={16} />
              </button>
              {menuOpen && (
                <div className="chat-menu" role="menu" aria-label="Chat options">
                  <button
                    type="button"
                    role="menuitem"
                    className="chat-menu-item"
                    onClick={() => {
                      setMenuOpen(false);
                      startTitleEdit();
                    }}
                  >
                    <Pencil size={14} />
                    Edit name
                  </button>
                  <button
                    type="button"
                    role="menuitem"
                    className="chat-menu-item chat-menu-item--danger"
                    onClick={() => {
                      setMenuOpen(false);
                      onDelete?.();
                    }}
                  >
                    <Trash2 size={14} />
                    Delete chat
                  </button>
                </div>
              )}
            </div>
          )}
        </div>
        {editable && jobs.length > 0 && (
          <JobsMenu jobs={jobs} spawned={spawned} onReview={() => onReviewJobs?.()} />
        )}
      </div>
    </div>
  );
};
