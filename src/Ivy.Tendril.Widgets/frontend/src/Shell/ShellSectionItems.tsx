import React, { useCallback, useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import {
  CircleCheck,
  Ellipsis,
  LoaderCircle,
  MessageCircle,
  Pencil,
  SquareTerminal,
  Trash2,
} from "lucide-react";
import { ShellItemState, ShellSectionItemDto } from "./types";
import { Badge } from "../ui/Badge";
import { IconButton } from "../ui/IconButton";
import "./shell.css";

const STATE_LABELS: Record<ShellItemState, string> = { working: "Working", completed: "Completed" };
const MENU_OFFSET = 4;
const MENU_MARGIN = 8;

const ItemStateIcon: React.FC<{ state: ShellItemState }> = ({ state }) => (
  <span className="tsh-section-item-state" data-state={state} role="img" aria-label={STATE_LABELS[state]}>
    {state === "working" ? <LoaderCircle size={12} className="tsh-spin" /> : <CircleCheck size={12} />}
  </span>
);

/** Maps a `ShellSectionItemDto.icon` name to its lucide component; unknown names render nothing. */
export const sectionItemIcons: Record<string, React.FC<{ size?: number }>> = {
  Terminal: SquareTerminal,
  MessageCircle: MessageCircle,
};

interface ItemMenuProps {
  item: ShellSectionItemDto;
  onRename?: () => void;
  onDelete?: () => void;
  onOpenChange?: (open: boolean) => void;
}

/**
 * The row's options: an ellipsis that only shows while the row is hovered or
 * the menu is open. The menu is portaled and fixed so the scrolling list cannot
 * clip it, and it closes on Escape, a click outside, or a pick.
 */
const ItemMenu: React.FC<ItemMenuProps> = ({ item, onRename, onDelete, onOpenChange }) => {
  const [open, setOpen] = useState(false);
  const [position, setPosition] = useState<{ top: number; right: number } | null>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);

  const close = useCallback(() => {
    setOpen(false);
    buttonRef.current?.focus();
  }, []);

  const toggle = () => {
    const button = buttonRef.current;
    if (!button) return;
    const rect = button.getBoundingClientRect();
    setPosition({
      top: rect.bottom + MENU_OFFSET,
      right: Math.max(MENU_MARGIN, window.innerWidth - rect.right),
    });
    setOpen((value) => !value);
  };

  useEffect(() => {
    onOpenChange?.(open);
  }, [open, onOpenChange]);

  useEffect(() => {
    if (!open) return;
    menuRef.current?.querySelector<HTMLElement>("[role=menuitem]")?.focus();
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        close();
        return;
      }
      if (e.key !== "ArrowDown" && e.key !== "ArrowUp") return;
      const items = [...(menuRef.current?.querySelectorAll<HTMLElement>("[role=menuitem]") ?? [])];
      if (items.length === 0) return;
      e.preventDefault();
      const current = items.indexOf(document.activeElement as HTMLElement);
      const delta = e.key === "ArrowDown" ? 1 : -1;
      items[(current + delta + items.length) % items.length].focus();
    };
    const onPointerDown = (e: Event) => {
      const target = e.target as Node | null;
      if (!target) return;
      if (menuRef.current?.contains(target) || buttonRef.current?.contains(target)) return;
      setOpen(false);
    };
    const onScroll = () => setOpen(false);
    window.addEventListener("keydown", onKeyDown);
    window.addEventListener("pointerdown", onPointerDown, true);
    window.addEventListener("scroll", onScroll, true);
    window.addEventListener("resize", onScroll);
    return () => {
      window.removeEventListener("keydown", onKeyDown);
      window.removeEventListener("pointerdown", onPointerDown, true);
      window.removeEventListener("scroll", onScroll, true);
      window.removeEventListener("resize", onScroll);
    };
  }, [open, close]);

  const pick = (action?: () => void) => {
    setOpen(false);
    action?.();
  };

  return (
    <>
      <IconButton
        ref={buttonRef}
        label={`${item.title} options`}
        tooltip="Options"
        size="sm"
        className="tsh-section-item-menu-btn"
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={toggle}
      >
        <Ellipsis size={14} />
      </IconButton>
      {open &&
        position &&
        createPortal(
          <div
            ref={menuRef}
            className="tsh-item-menu"
            role="menu"
            aria-label={`${item.title} options`}
            style={{ top: position.top, right: position.right }}
          >
            {onRename && (
              <button type="button" role="menuitem" className="tsh-item-menu-item" onClick={() => pick(onRename)}>
                <Pencil size={14} />
                Edit name
              </button>
            )}
            {onDelete && (
              <button
                type="button"
                role="menuitem"
                className="tsh-item-menu-item tsh-item-menu-item--danger"
                onClick={() => pick(onDelete)}
              >
                <Trash2 size={14} />
                Delete
              </button>
            )}
          </div>,
          document.body,
        )}
    </>
  );
};

interface ItemTitleEditorProps {
  title: string;
  onSave: (title: string) => void;
  onCancel: () => void;
}

const ItemTitleEditor: React.FC<ItemTitleEditorProps> = ({ title, onSave, onCancel }) => {
  const [text, setText] = useState(title);
  const save = () => {
    const trimmed = text.trim();
    if (trimmed && trimmed !== title) onSave(trimmed);
    else onCancel();
  };
  return (
    <input
      type="text"
      className="tsh-section-item-input"
      aria-label="Item name"
      value={text}
      autoFocus
      onChange={(e) => setText(e.target.value)}
      onBlur={save}
      onKeyDown={(e) => {
        if (e.key === "Enter") save();
        if (e.key === "Escape") onCancel();
      }}
    />
  );
};

interface ShellSectionItemsProps {
  items: ShellSectionItemDto[];
  selectedId?: string;
  showBadges?: boolean;
  emptyText?: string;
  className?: string;
  onSelect: (itemId: string) => void;
  /** Rows gain an options menu when either of these is given. */
  onRename?: (itemId: string, title: string) => void;
  onDelete?: (itemId: string) => void;
  /** True while a row menu is open or a row is being renamed; a floating host should not close over it. */
  onInteractionChange?: (active: boolean) => void;
}

/** The plan/chat rows, shared by the expanded sidebar list and the rail's flyout menu. */
export const ShellSectionItems: React.FC<ShellSectionItemsProps> = ({
  items,
  selectedId,
  showBadges = true,
  emptyText,
  className = "",
  onSelect,
  onRename,
  onDelete,
  onInteractionChange,
}) => {
  const [editingId, setEditingId] = useState<string | null>(null);
  const [menuOpenId, setMenuOpenId] = useState<string | null>(null);
  const hasMenu = !!onRename || !!onDelete;

  useEffect(() => {
    if (editingId != null && !items.some((item) => item.id === editingId)) setEditingId(null);
  }, [items, editingId]);

  const interacting = editingId != null || menuOpenId != null;
  useEffect(() => {
    onInteractionChange?.(interacting);
  }, [interacting, onInteractionChange]);

  return (
    <div className={`tsh-section-list ${className}`.trim()}>
      {items.length === 0 && emptyText && <div className="tsh-section-empty">{emptyText}</div>}
      {items.map((item) => {
        const ItemIcon = item.icon ? sectionItemIcons[item.icon] : undefined;
        const badges = showBadges ? item.badges : undefined;
        const editing = editingId === item.id;
        const row = (
          <span className="tsh-section-item-top">
            {ItemIcon && (
              <span className="tsh-section-item-icon">
                <ItemIcon size={14} />
              </span>
            )}
            {item.state && <ItemStateIcon state={item.state} />}
            {editing && onRename ? (
              <ItemTitleEditor
                title={item.title}
                onSave={(title) => {
                  setEditingId(null);
                  onRename(item.id, title);
                }}
                onCancel={() => setEditingId(null)}
              />
            ) : (
              <span className="tsh-section-item-title">{item.title}</span>
            )}
            {item.tag && <span className="tsh-section-item-tag">{item.tag}</span>}
          </span>
        );
        const badgeRow = badges && badges.length > 0 && (
          <span className="tsh-section-item-badges">
            {badges.map((badge, i) => (
              <Badge key={i} kind={badge.kind} color={badge.color}>
                {badge.label}
              </Badge>
            ))}
          </span>
        );
        const rowElement = editing ? (
          <div className="tsh-section-item" data-selected={item.id === selectedId} data-editing="true">
            {row}
            {badgeRow}
          </div>
        ) : (
          <button
            className="tsh-section-item"
            data-selected={item.id === selectedId}
            onClick={() => onSelect(item.id)}
          >
            {row}
            {badgeRow}
          </button>
        );
        if (!hasMenu) return <React.Fragment key={item.id}>{rowElement}</React.Fragment>;
        return (
          <div key={item.id} className="tsh-section-item-wrap">
            {rowElement}
            {!editing && (
              <ItemMenu
                item={item}
                onRename={onRename ? () => setEditingId(item.id) : undefined}
                onDelete={onDelete ? () => onDelete(item.id) : undefined}
                onOpenChange={(open) => setMenuOpenId((current) => (open ? item.id : current === item.id ? null : current))}
              />
            )}
          </div>
        );
      })}
    </div>
  );
};
