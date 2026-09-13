import React, { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { ChevronLeft, ChevronRight, Plus } from "lucide-react";
import { ShellSectionItemDto } from "./types";
import { ShellSectionItems } from "./ShellSectionItems";
import { IconButton } from "../ui/IconButton";
import "./shell.css";

const OPEN_DELAY_MS = 120;
const CLOSE_DELAY_MS = 220;
const MENU_WIDTH = 300;
const MENU_MARGIN = 8;
const MENU_MIN_HEIGHT = 160;

interface MenuPosition {
  top: number;
  left: number;
  maxHeight: number;
}

export interface RailFlyoutTrigger {
  ref: React.RefObject<HTMLButtonElement | null>;
  open: boolean;
  pinned: boolean;
  onPointerEnter: () => void;
  onPointerLeave: () => void;
  onClick: () => void;
}

interface ShellRailFlyoutProps {
  title?: string;
  items: ShellSectionItemDto[];
  selectedId?: string;
  emptyText?: string;
  onSelect: (itemId: string) => void;
  newLabel?: string;
  onNew?: () => void;
  onRename?: (itemId: string, title: string) => void;
  onDelete?: (itemId: string) => void;
  children: (trigger: RailFlyoutTrigger) => React.ReactNode;
}

/**
 * Floats a section list over the content next to a rail button. Hovering the
 * trigger opens the list; clicking pins it open until Escape, a click outside
 * or another click on the trigger dismisses it. The bar's arrows step the
 * selection through the list without leaving the menu.
 */
export const ShellRailFlyout: React.FC<ShellRailFlyoutProps> = ({
  title,
  items,
  selectedId,
  emptyText,
  onSelect,
  newLabel,
  onNew,
  onRename,
  onDelete,
  children,
}) => {
  const [open, setOpen] = useState(false);
  const [pinned, setPinned] = useState(false);
  const [position, setPosition] = useState<MenuPosition | null>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const timerRef = useRef<number | null>(null);
  const [rowInteracting, setRowInteracting] = useState(false);

  const clearTimer = useCallback(() => {
    if (timerRef.current != null) {
      window.clearTimeout(timerRef.current);
      timerRef.current = null;
    }
  }, []);

  const measure = useCallback(() => {
    const button = buttonRef.current;
    if (!button) return;
    const rect = button.getBoundingClientRect();
    const viewportHeight = window.innerHeight || 0;
    setPosition({
      top: Math.max(MENU_MARGIN, rect.top),
      left: rect.right + MENU_MARGIN,
      maxHeight: Math.max(MENU_MIN_HEIGHT, viewportHeight - MENU_MARGIN * 2),
    });
  }, []);

  // The menu opens level with its button, then slides up by however much it
  // overhangs the viewport - a short list stays anchored, a long one grows up.
  useLayoutEffect(() => {
    const menu = menuRef.current;
    if (!open || !menu || !position) return;
    const viewportHeight = window.innerHeight || 0;
    const maxTop = Math.max(MENU_MARGIN, viewportHeight - MENU_MARGIN - menu.offsetHeight);
    if (position.top > maxTop) setPosition({ ...position, top: maxTop });
  }, [open, position, items.length]);

  const openMenu = useCallback(() => {
    clearTimer();
    measure();
    setOpen(true);
  }, [clearTimer, measure]);

  const closeMenu = useCallback(() => {
    clearTimer();
    setPinned(false);
    setOpen(false);
  }, [clearTimer]);

  const scheduleOpen = useCallback(() => {
    clearTimer();
    timerRef.current = window.setTimeout(openMenu, OPEN_DELAY_MS);
  }, [clearTimer, openMenu]);

  const scheduleClose = useCallback(() => {
    clearTimer();
    if (pinned || rowInteracting) return;
    timerRef.current = window.setTimeout(() => setOpen(false), CLOSE_DELAY_MS);
  }, [clearTimer, pinned, rowInteracting]);

  const togglePinned = useCallback(() => {
    if (pinned) {
      closeMenu();
      return;
    }
    setPinned(true);
    openMenu();
  }, [closeMenu, openMenu, pinned]);

  useEffect(() => clearTimer, [clearTimer]);

  useEffect(() => {
    if (!open) return;
    const reposition = () => measure();
    window.addEventListener("resize", reposition);
    window.addEventListener("scroll", reposition, true);
    return () => {
      window.removeEventListener("resize", reposition);
      window.removeEventListener("scroll", reposition, true);
    };
  }, [open, measure]);

  useEffect(() => {
    if (!open || !pinned) return;
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") closeMenu();
    };
    const onPointerDown = (e: Event) => {
      const target = e.target as Node | null;
      if (!target) return;
      if (menuRef.current?.contains(target) || buttonRef.current?.contains(target)) return;
      if (target instanceof Element && target.closest(".tsh-item-menu")) return;
      closeMenu();
    };
    window.addEventListener("keydown", onKeyDown);
    window.addEventListener("pointerdown", onPointerDown, true);
    return () => {
      window.removeEventListener("keydown", onKeyDown);
      window.removeEventListener("pointerdown", onPointerDown, true);
    };
  }, [open, pinned, closeMenu]);

  const selectedIndex = items.findIndex((item) => item.id === selectedId);
  const canStepBack = items.length > 0 && selectedIndex !== 0;
  const canStepForward = items.length > 0 && selectedIndex !== items.length - 1;

  const step = (delta: number) => {
    if (items.length === 0) return;
    const next = selectedIndex === -1 ? (delta < 0 ? items.length - 1 : 0) : selectedIndex + delta;
    if (next < 0 || next >= items.length) return;
    onSelect(items[next].id);
  };

  const closeUnlessPinned = () => {
    if (pinned) return;
    clearTimer();
    setOpen(false);
  };

  const selectItem = (itemId: string) => {
    onSelect(itemId);
    closeUnlessPinned();
  };

  const createNew = () => {
    onNew?.();
    closeUnlessPinned();
  };

  return (
    <>
      {children({
        ref: buttonRef,
        open,
        pinned,
        onPointerEnter: scheduleOpen,
        onPointerLeave: scheduleClose,
        onClick: togglePinned,
      })}
      {open &&
        position &&
        createPortal(
          <div
            ref={menuRef}
            className="tsh-rail-menu"
            style={{
              top: position.top,
              left: position.left,
              width: MENU_WIDTH,
              maxHeight: position.maxHeight,
            }}
            onPointerEnter={clearTimer}
            onPointerLeave={scheduleClose}
          >
            <div className="tsh-rail-menu-bar">
              <span className="tsh-rail-menu-steps">
                <IconButton
                  label="Previous"
                  size="sm"
                  onClick={() => step(-1)}
                  disabled={!canStepBack}
                >
                  <ChevronLeft size={14} />
                </IconButton>
                <IconButton
                  label="Next"
                  size="sm"
                  onClick={() => step(1)}
                  disabled={!canStepForward}
                >
                  <ChevronRight size={14} />
                </IconButton>
              </span>
              {title && <span className="tsh-rail-menu-title">{title}</span>}
              {newLabel && onNew && (
                <IconButton
                  label={newLabel}
                  size="sm"
                  className="tsh-rail-menu-new"
                  onClick={createNew}
                >
                  <Plus size={14} />
                </IconButton>
              )}
            </div>
            <ShellSectionItems
              items={items}
              selectedId={selectedId}
              emptyText={emptyText}
              className="tsh-rail-menu-list"
              onSelect={selectItem}
              onRename={onRename}
              onDelete={onDelete}
              onInteractionChange={setRowInteracting}
            />
          </div>,
          document.body,
        )}
    </>
  );
};
