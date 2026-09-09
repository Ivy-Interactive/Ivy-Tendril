import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Ellipsis, ExternalLink, FileCheck2, FileQuestion, LoaderCircle, LucideIcon } from "lucide-react";
import { ShellTooltip } from "../Shell/ShellTooltip";
import { useOutsideClick } from "../ChatWidget/useOutsideClick";
import { ActionIcon } from "./icons";
import { shortcutKeys, useActionShortcuts } from "./shortcuts";
import type { ShortcutBinding } from "./shortcuts";
import { clampChatWidth, MIN_CHAT_WIDTH, readStoredChatWidth, writeStoredChatWidth } from "./chatWidth";
import { hasNodes } from "./types";
import type { PlanActionDto, PlanWorkspaceProps } from "./types";
import "./plan-workspace.css";

export type { PlanActionDto, PlanProjectBadgeDto, PlanTabDto, PlanWorkspaceProps } from "./types";

/** Below this the chat panel stacks under the plan instead of sitting beside it. */
const NARROW_WIDTH = 760;
/** Below this the icon actions fold into the overflow menu. */
const COMPACT_WIDTH = 560;

const EMPTY_ACTIONS: PlanActionDto[] = [];
const EMPTY_EVENTS: string[] = [];

const Kbd: React.FC<{ shortcut?: string; className?: string }> = ({ shortcut, className = "" }) => {
  if (!shortcut) return null;
  const keys = shortcutKeys(shortcut);
  if (keys.length === 0) return null;
  return (
    <span className={`pws-kbd ${className}`.trim()} aria-hidden="true">
      {keys.map((key, index) => (
        <span key={`${key}-${index}`}>{key}</span>
      ))}
    </span>
  );
};

const IconAction: React.FC<{ action: PlanActionDto; onFire: (tag: string) => void }> = ({ action, onFire }) => (
  <ShellTooltip content={action.label} shortcut={action.shortcut ? shortcutKeys(action.shortcut) : undefined} side="bottom">
    <button
      type="button"
      className="pws-icon-btn"
      data-active={!!action.active}
      data-tag={action.tag}
      aria-label={action.label}
      aria-pressed={action.active ? true : undefined}
      disabled={action.disabled || action.loading}
      onClick={() => onFire(action.tag)}
    >
      {action.loading ? <LoaderCircle size={16} className="pws-spin" /> : <ActionIcon icon={action.icon} />}
      {action.badge && <span className="pws-icon-badge">{action.badge}</span>}
    </button>
  </ShellTooltip>
);

const LabeledButton: React.FC<{ action: PlanActionDto; primary?: boolean; onFire: (tag: string) => void }> = ({
  action,
  primary = false,
  onFire,
}) => (
  <button
    type="button"
    className={`pws-btn ${primary ? "pws-btn--primary" : "pws-btn--secondary"}`}
    data-tag={action.tag}
    disabled={action.disabled || action.loading}
    aria-busy={action.loading || undefined}
    title={action.label}
    onClick={() => onFire(action.tag)}
  >
    {action.loading ? <LoaderCircle size={16} className="pws-spin" /> : <ActionIcon icon={action.icon} />}
    <span className="pws-btn-label">{action.label}</span>
    {action.badge && <span className="pws-btn-badge">{action.badge}</span>}
    <Kbd shortcut={action.shortcut} className="pws-btn-kbd" />
  </button>
);

interface OverflowMenuProps {
  items: PlanActionDto[];
  onFire: (tag: string) => void;
}

const OverflowMenu: React.FC<OverflowMenuProps> = ({ items, onFire }) => {
  const [open, setOpen] = useState(false);
  const wrapRef = useRef<HTMLDivElement>(null);
  const close = useCallback(() => setOpen(false), []);
  useOutsideClick(open, [wrapRef], close);

  useEffect(() => {
    if (!open) return;
    const handle = (e: KeyboardEvent) => {
      if (e.key === "Escape") setOpen(false);
    };
    document.addEventListener("keydown", handle);
    return () => document.removeEventListener("keydown", handle);
  }, [open]);

  return (
    <div className="pws-menu-wrap" ref={wrapRef}>
      <ShellTooltip content="More" side="bottom" enabled={!open}>
        <button
          type="button"
          className="pws-icon-btn"
          aria-label="More actions"
          aria-haspopup="menu"
          aria-expanded={open}
          data-active={open}
          onClick={() => setOpen((value) => !value)}
        >
          <Ellipsis size={16} />
        </button>
      </ShellTooltip>
      {open && (
        <div className="pws-menu" role="menu" aria-label="More actions">
          {items.map((item) => (
            <button
              key={item.tag}
              type="button"
              role="menuitem"
              className="pws-menu-item"
              data-danger={!!item.danger}
              data-tag={item.tag}
              disabled={item.disabled}
              onClick={() => {
                setOpen(false);
                onFire(item.tag);
              }}
            >
              <span className="pws-menu-item-icon">
                <ActionIcon icon={item.icon} size={14} />
              </span>
              <span className="pws-menu-item-label">{item.label}</span>
              <Kbd shortcut={item.shortcut} />
            </button>
          ))}
        </div>
      )}
    </div>
  );
};

interface TabToolProps {
  icon: LucideIcon;
  label: string;
  panel: React.ReactNode;
  open: boolean;
  indicator?: boolean;
  onToggle: () => void;
  onClose: () => void;
}

/** Plans whose Questions dropdown was opened in this page session; the dot stays off for them. */
const seenQuestionPlans = new Set<string>();

/** One of the two icons in the tab strip's far corner; its panel drops down beneath it. */
const TabTool: React.FC<TabToolProps> = ({ icon: Icon, label, panel, open, indicator = false, onToggle, onClose }) => {
  const wrapRef = useRef<HTMLDivElement>(null);
  useOutsideClick(open, [wrapRef], onClose);

  useEffect(() => {
    if (!open) return;
    const handle = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    document.addEventListener("keydown", handle);
    return () => document.removeEventListener("keydown", handle);
  }, [open, onClose]);

  return (
    <div className="pws-tool-wrap" ref={wrapRef}>
      <ShellTooltip content={label} side="bottom" enabled={!open}>
        <button
          type="button"
          className="pws-tool-btn"
          aria-label={label}
          aria-haspopup="dialog"
          aria-expanded={open}
          data-active={open}
          data-indicator={indicator}
          onClick={onToggle}
        >
          <Icon size={16} />
          {indicator && <span className="pws-tool-dot" aria-hidden="true" />}
        </button>
      </ShellTooltip>
      {open && (
        <div className="pws-dropdown" role="group" aria-label={label}>
          <div className="pws-dropdown-title">{label}</div>
          <div className="pws-dropdown-body">{panel}</div>
        </div>
      )}
    </div>
  );
};

export const PlanWorkspace: React.FC<PlanWorkspaceProps> = ({
  id,
  planId = "",
  title = "",
  meta,
  sourceUrl,
  sourceLabel,
  persona,
  personaInitials,
  projects = [],
  actions = EMPTY_ACTIONS,
  menuItems = EMPTY_ACTIONS,
  primary,
  secondary = EMPTY_ACTIONS,
  tabs = [],
  selectedTab,
  chatWidth = 420,
  verificationsLabel = "Verifications",
  questionsLabel = "Questions",
  unansweredQuestions = 0,
  events = EMPTY_EVENTS,
  eventHandler,
  slots,
}) => {
  const rootRef = useRef<HTMLDivElement>(null);
  const [rootWidth, setRootWidth] = useState<number | null>(null);
  const [width, setWidth] = useState(() => readStoredChatWidth() ?? chatWidth);
  const [dragging, setDragging] = useState(false);
  const [openTool, setOpenTool] = useState<"verifications" | "questions" | null>(null);
  const [, setSeenVersion] = useState(0);
  const questionsSeen = seenQuestionPlans.has(planId);

  const openQuestions = () => {
    seenQuestionPlans.add(planId);
    setSeenVersion((value) => value + 1);
    setOpenTool((value) => (value === "questions" ? null : "questions"));
  };

  const emit = useCallback(
    (eventName: string, ...args: unknown[]) => {
      if (eventHandler && events.includes(eventName)) eventHandler(eventName, id, args);
    },
    [eventHandler, events, id],
  );
  const focusChat = useCallback(() => {
    const composer = rootRef.current?.querySelector<HTMLTextAreaElement>(".pws-chat textarea");
    composer?.focus();
  }, []);

  const focusChatTags = useMemo(
    () => new Set([...actions, ...menuItems, ...secondary, ...(primary ? [primary] : [])].filter((a) => a.focusChat).map((a) => a.tag)),
    [actions, menuItems, secondary, primary],
  );

  const fire = useCallback(
    (tag: string) => {
      if (focusChatTags.has(tag)) focusChat();
      emit("OnAction", tag);
    },
    [emit, focusChat, focusChatTags],
  );

  useEffect(() => {
    const root = rootRef.current;
    if (!root || typeof ResizeObserver === "undefined") return;
    const measured = root.getBoundingClientRect().width;
    if (measured > 0) setRootWidth(measured);
    const observer = new ResizeObserver((entries) => {
      for (const entry of entries) {
        if (entry.contentRect.width > 0) setRootWidth(entry.contentRect.width);
      }
    });
    observer.observe(root);
    return () => observer.disconnect();
  }, []);

  const narrow = rootWidth != null && rootWidth < NARROW_WIDTH;
  const compact = rootWidth != null && rootWidth < COMPACT_WIDTH;

  const iconActions = compact ? EMPTY_ACTIONS : actions;
  const menu = useMemo(() => (compact ? [...actions, ...menuItems] : menuItems), [compact, actions, menuItems]);

  const bindings = useMemo<ShortcutBinding[]>(
    () => [...actions, ...menuItems, ...secondary, ...(primary ? [primary] : [])],
    [actions, menuItems, secondary, primary],
  );
  useActionShortcuts(bindings, fire, events.includes("OnAction"));

  const startResize = (e: React.PointerEvent<HTMLDivElement>) => {
    const root = rootRef.current;
    if (!root || e.button !== 0) return;
    e.preventDefault();
    const rect = root.getBoundingClientRect();
    setDragging(true);
    let latest = width;
    const move = (ev: PointerEvent) => {
      latest = clampChatWidth(rect.right - ev.clientX, rect.width);
      setWidth(latest);
    };
    const up = () => {
      window.removeEventListener("pointermove", move);
      window.removeEventListener("pointerup", up);
      window.removeEventListener("pointercancel", up);
      setDragging(false);
      writeStoredChatWidth(latest);
    };
    window.addEventListener("pointermove", move);
    window.addEventListener("pointerup", up);
    window.addEventListener("pointercancel", up);
  };

  const resetWidth = () => {
    setWidth(chatWidth);
    writeStoredChatWidth(null);
  };

  const hasVerifications = hasNodes(slots?.Verifications);
  const hasQuestions = hasNodes(slots?.Questions);
  const hasToolbar = hasNodes(slots?.Toolbar);
  const showChat = hasNodes(slots?.Chat);

  const rootStyle = { "--pws-chat-width": `${Math.max(width, MIN_CHAT_WIDTH)}px` } as React.CSSProperties;

  return (
    <div
      ref={rootRef}
      className="pws-root remove-parent-padding"
      style={rootStyle}
      data-narrow={narrow}
      data-compact={compact}
      data-dragging={dragging}
    >
      <header className="pws-topbar">
        <div className="pws-title-wrap">
          <div className="pws-title-main">
            {planId && <span className="pws-plan-id">{planId}</span>}
            <span className="pws-title" title={title}>
              {title}
            </span>
          </div>
          {sourceUrl && (
            <a className="pws-source" href={sourceUrl} target="_blank" rel="noreferrer" title={sourceUrl}>
              <ExternalLink size={14} />
              <span>{sourceLabel || "Source"}</span>
            </a>
          )}
          {meta && <span className="pws-meta">{meta}</span>}
        </div>
        <div className="pws-topbar-right">
          {projects && projects.length > 0 && (
            <div className="pws-project-badges">
              {projects.map((proj, i) => (
                <span
                  key={proj.label || i}
                  className="pws-project-badge"
                  data-color={proj.color?.toLowerCase()}
                  title={proj.label}
                >
                  {proj.label}
                </span>
              ))}
            </div>
          )}
          {persona && (
            <div className="pws-persona" title={persona}>
              <span className="pws-avatar" aria-hidden="true">
                {personaInitials || persona.charAt(0).toUpperCase()}
              </span>
              <span className="pws-persona-name">{persona}</span>
            </div>
          )}
          {(iconActions.length > 0 || menu.length > 0) && (
            <div className="pws-icon-group">
              {iconActions.map((action) => (
                <IconAction key={action.tag} action={action} onFire={fire} />
              ))}
              {menu.length > 0 && <OverflowMenu items={menu} onFire={fire} />}
            </div>
          )}
          {secondary.map((action) => (
            <LabeledButton key={action.tag} action={action} onFire={fire} />
          ))}
          {primary && <LabeledButton action={primary} primary onFire={fire} />}
        </div>
      </header>

      <div className="pws-body">
        <section className="pws-main">
          {hasToolbar && <div className="pws-toolbar">{slots?.Toolbar}</div>}
          {(tabs.length > 0 || hasVerifications || hasQuestions) && (
            <div className="pws-tabs-row">
              <div className="pws-tabs" role="tablist">
                {tabs.map((tab) => (
                  <button
                    key={tab.id}
                    type="button"
                    role="tab"
                    className="pws-tab"
                    aria-selected={tab.id === selectedTab}
                    onClick={() => emit("OnTabSelect", tab.id)}
                  >
                    <span>{tab.label}</span>
                    {tab.badge && <span className="pws-tab-badge">{tab.badge}</span>}
                  </button>
                ))}
              </div>
              {(hasVerifications || hasQuestions) && (
                <div className="pws-tab-tools">
                  {hasVerifications && (
                    <TabTool
                      icon={FileCheck2}
                      label={verificationsLabel}
                      panel={slots?.Verifications}
                      open={openTool === "verifications"}
                      onToggle={() => setOpenTool((value) => (value === "verifications" ? null : "verifications"))}
                      onClose={() => setOpenTool((value) => (value === "verifications" ? null : value))}
                    />
                  )}
                  {hasQuestions && (
                    <TabTool
                      icon={FileQuestion}
                      label={questionsLabel}
                      panel={slots?.Questions}
                      open={openTool === "questions"}
                      indicator={unansweredQuestions > 0 && !questionsSeen}
                      onToggle={openQuestions}
                      onClose={() => setOpenTool((value) => (value === "questions" ? null : value))}
                    />
                  )}
                </div>
              )}
            </div>
          )}
          <div className="pws-content">{slots?.Content}</div>
        </section>

        {showChat && (
          <>
            <div
              className="pws-resizer"
              role="separator"
              aria-orientation="vertical"
              aria-label="Resize chat"
              aria-valuenow={width}
              aria-valuemin={MIN_CHAT_WIDTH}
              title="Drag to resize, double-click to reset"
              onPointerDown={startResize}
              onDoubleClick={resetWidth}
            />
            <aside className="pws-chat" aria-label="Plan chat">
              {slots?.Chat}
            </aside>
          </>
        )}
      </div>
    </div>
  );
};
