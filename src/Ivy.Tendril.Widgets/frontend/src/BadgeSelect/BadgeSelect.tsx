import React, { useEffect, useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { ChevronDown, Flag, Folder, WandSparkles, X, type LucideIcon } from "lucide-react";
import "./badge-select.css";

type IvyEventHandler = (eventName: string, widgetId: string, args: unknown[]) => void;

export interface BadgeSelectOption {
  value: string;
  label: string;
  icon?: string | null;
  removable?: boolean;
}

interface BadgeSelectProps {
  id: string;
  options?: BadgeSelectOption[];
  value?: string[];
  placeholder?: string;
  icon?: string;
  multiple?: boolean;
  tooltip?: string;
  width?: string;
  events?: string[];
  eventHandler?: IvyEventHandler;
}

const EMPTY_OPTIONS: BadgeSelectOption[] = [];
const EMPTY_VALUE: string[] = [];
const EMPTY_EVENTS: string[] = [];
const MENU_GAP = 4;
const MENU_MAX_HEIGHT = 240;

const ICONS: Record<string, LucideIcon> = {
  ChevronDown,
  Flag,
  Folder,
  WandSparkles,
  X,
};

function NamedIcon({ name, size = 14, className }: { name?: string | null; size?: number; className?: string }) {
  if (!name) return null;
  const Cmp = ICONS[name];
  return Cmp ? <Cmp size={size} className={className} aria-hidden /> : null;
}

function parseWidth(width?: string): React.CSSProperties {
  if (!width) return {};
  const [wanted, min, max] = width.split(",");
  const style: React.CSSProperties = {};
  const apply = (part: string | undefined, key: "width" | "minWidth" | "maxWidth") => {
    if (!part) return;
    const [type, raw] = part.split(":");
    const value = raw ? parseFloat(raw) : undefined;
    switch (type.toLowerCase()) {
      case "fraction":
        style[key] = `${(value ?? 0) * 100}%`;
        break;
      case "full":
        style[key] = "100%";
        break;
      case "px":
        style[key] = `${value}px`;
        break;
      case "rem":
        style[key] = `${value}rem`;
        break;
      case "units":
        style[key] = `${(value ?? 0) * 0.25}rem`;
        break;
      case "fit":
        style[key] = "fit-content";
        break;
      case "grow":
        style.flexGrow = value || 1;
        style.minWidth = 0;
        break;
      default:
        break;
    }
  };
  apply(wanted, "width");
  apply(min, "minWidth");
  apply(max, "maxWidth");
  return style;
}

export function BadgeSelect({
  id,
  options = EMPTY_OPTIONS,
  value = EMPTY_VALUE,
  placeholder = "Select...",
  icon,
  multiple = true,
  tooltip,
  width,
  events = EMPTY_EVENTS,
  eventHandler,
}: BadgeSelectProps) {
  const [open, setOpen] = useState(false);
  const [menuStyle, setMenuStyle] = useState<React.CSSProperties>({});
  const [visibleCount, setVisibleCount] = useState(Number.MAX_SAFE_INTEGER);
  const [containerWidth, setContainerWidth] = useState(0);
  const rootRef = useRef<HTMLDivElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const badgesRef = useRef<HTMLDivElement>(null);
  const selected = Array.isArray(value) ? value : [];
  const selectedKey = selected.join("\u0000");

  const updateMenuPosition = () => {
    const trigger = rootRef.current;
    if (!trigger) return;
    const rect = trigger.getBoundingClientRect();
    const spaceBelow = window.innerHeight - rect.bottom - MENU_GAP;
    const spaceAbove = rect.top - MENU_GAP;
    const openUp = spaceBelow < Math.min(MENU_MAX_HEIGHT, 160) && spaceAbove > spaceBelow;
    const maxHeight = Math.min(MENU_MAX_HEIGHT, Math.max(120, openUp ? spaceAbove : spaceBelow));

    setMenuStyle({
      position: "fixed",
      left: rect.left,
      width: rect.width,
      maxHeight,
      top: openUp ? undefined : rect.bottom + MENU_GAP,
      bottom: openUp ? window.innerHeight - rect.top + MENU_GAP : undefined,
      zIndex: 1000,
      pointerEvents: "auto",
    });
  };

  useLayoutEffect(() => {
    if (!open) return;
    updateMenuPosition();
  }, [open, options.length, selected.length]);

  useEffect(() => {
    const el = rootRef.current;
    if (!el || typeof ResizeObserver === "undefined") return;
    const ro = new ResizeObserver(() => setContainerWidth(el.clientWidth));
    ro.observe(el);
    setContainerWidth(el.clientWidth);
    return () => ro.disconnect();
  }, []);

  useLayoutEffect(() => {
    const container = badgesRef.current;
    if (!container || selected.length === 0) return;
    const badges = Array.from(container.querySelectorAll<HTMLElement>("[data-badge]"));
    badges.forEach((b) => (b.style.display = ""));
    const gap = 4;
    const counterReserve = 40;
    const avail = container.clientWidth;
    let used = 0;
    let count = 0;
    for (let i = 0; i < badges.length; i++) {
      const w = badges[i].offsetWidth + (i > 0 ? gap : 0);
      const hasMore = i < badges.length - 1;
      const reserve = hasMore ? counterReserve + gap : 0;
      if (used + w + reserve <= avail) {
        used += w;
        count++;
      } else {
        break;
      }
    }
    count = Math.max(1, count);
    badges.forEach((b, i) => (b.style.display = i < count ? "" : "none"));
    setVisibleCount(count);
  }, [selectedKey, containerWidth]);

  useEffect(() => {
    if (!open) return;
    const menu = menuRef.current;
    if (!menu) return;
    const stop = (e: Event) => e.stopPropagation();
    menu.addEventListener("wheel", stop, { passive: false });
    menu.addEventListener("touchmove", stop, { passive: false });
    return () => {
      menu.removeEventListener("wheel", stop);
      menu.removeEventListener("touchmove", stop);
    };
  }, [open]);

  useEffect(() => {
    if (!open) return;
    const onPointerDown = (e: MouseEvent) => {
      const target = e.target as Node;
      if (rootRef.current?.contains(target) || menuRef.current?.contains(target)) return;
      setOpen(false);
    };
    const onReposition = () => updateMenuPosition();
    document.addEventListener("mousedown", onPointerDown);
    window.addEventListener("resize", onReposition);
    window.addEventListener("scroll", onReposition, true);
    return () => {
      document.removeEventListener("mousedown", onPointerDown);
      window.removeEventListener("resize", onReposition);
      window.removeEventListener("scroll", onReposition, true);
    };
  }, [open]);

  const emit = (next: string[]) => {
    if (eventHandler && events.includes("OnChange")) {
      eventHandler("OnChange", id, [next]);
    }
  };

  const toggle = (optionValue: string) => {
    if (multiple) {
      if (selected.includes(optionValue)) {
        const opt = options.find((o) => o.value === optionValue);
        if (opt?.removable === false) return;
        emit(selected.filter((v) => v !== optionValue));
        return;
      }
      emit([...selected, optionValue]);
      return;
    }
    emit([optionValue]);
    setOpen(false);
  };

  const remove = (optionValue: string, e: React.MouseEvent) => {
    e.preventDefault();
    e.stopPropagation();
    const opt = options.find((o) => o.value === optionValue);
    if (opt?.removable === false) return;
    emit(selected.filter((v) => v !== optionValue));
  };

  const triggerIcon =
    icon ||
    (selected.length === 0
      ? undefined
      : options.find((o) => o.value === selected[0])?.icon || undefined);

  const menu =
    open &&
    createPortal(
      <div
        ref={menuRef}
        className="bselect-menu bselect-menu-portal"
        role="listbox"
        aria-multiselectable={multiple}
        style={menuStyle}
      >
        {options.map((opt) => {
          const isSelected = selected.includes(opt.value);
          const showRemove = isSelected && opt.removable !== false;
          return (
            <button
              key={opt.value}
              type="button"
              role="option"
              aria-selected={isSelected}
              className={`bselect-item${isSelected ? " bselect-item-selected" : ""}`}
              onClick={() => toggle(opt.value)}
            >
              <NamedIcon name={opt.icon} />
              <span className="bselect-item-label">{opt.label}</span>
              {showRemove && <X size={14} className="bselect-item-x" />}
            </button>
          );
        })}
      </div>,
      document.body,
    );

  return (
    <div ref={rootRef} className="bselect" style={parseWidth(width)} title={tooltip}>
      <button
        type="button"
        className="bselect-trigger"
        aria-expanded={open}
        aria-haspopup="listbox"
        onClick={() => setOpen((v) => !v)}
      >
        <NamedIcon name={triggerIcon} className="bselect-trigger-icon" />
        <div ref={badgesRef} className="bselect-badges">
          {selected.length === 0 ? (
            <span className="bselect-placeholder">{placeholder}</span>
          ) : (
            <>
              {selected.map((v, i) => {
                const opt = options.find((o) => o.value === v);
                const canRemove = multiple && opt?.removable !== false;
                return (
                  <span
                    key={v}
                    data-badge
                    className="bselect-badge"
                    style={{ display: i < visibleCount ? undefined : "none" }}
                  >
                    <span className="bselect-badge-label">{opt?.label ?? v}</span>
                    {canRemove && (
                      <button
                        type="button"
                        className="bselect-badge-x"
                        aria-label={`Remove ${opt?.label ?? v}`}
                        onClick={(e) => remove(v, e)}
                      >
                        <X size={12} />
                      </button>
                    )}
                  </span>
                );
              })}
              {visibleCount < selected.length && (
                <span className="bselect-count">+{selected.length - visibleCount}</span>
              )}
            </>
          )}
        </div>
        <ChevronDown size={14} className="bselect-chevron" />
      </button>
      {menu}
    </div>
  );
}
