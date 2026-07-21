import React, { useEffect, useRef, useState } from "react";
import { ChevronDown, Flag, Folder, WandSparkles, X, type LucideIcon } from "lucide-react";
import "./badge-select.css";

type IvyEventHandler = (eventName: string, widgetId: string, args: unknown[]) => void;

export interface BadgeSelectOption {
  value: string;
  label: string;
  icon?: string | null;
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
  const rootRef = useRef<HTMLDivElement>(null);
  const selected = Array.isArray(value) ? value : [];

  useEffect(() => {
    if (!open) return;
    const onPointerDown = (e: MouseEvent) => {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) {
        setOpen(false);
      }
    };
    document.addEventListener("mousedown", onPointerDown);
    return () => document.removeEventListener("mousedown", onPointerDown);
  }, [open]);

  const emit = (next: string[]) => {
    if (eventHandler && events.includes("OnChange")) {
      eventHandler("OnChange", id, [next]);
    }
  };

  const toggle = (optionValue: string) => {
    if (multiple) {
      const next = selected.includes(optionValue)
        ? selected.filter((v) => v !== optionValue)
        : [...selected, optionValue];
      emit(next);
      return;
    }
    emit([optionValue]);
    setOpen(false);
  };

  const remove = (optionValue: string, e: React.MouseEvent) => {
    e.preventDefault();
    e.stopPropagation();
    emit(selected.filter((v) => v !== optionValue));
  };

  const triggerIcon =
    icon ||
    (selected.length === 0
      ? undefined
      : options.find((o) => o.value === selected[0])?.icon || undefined);

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
        <div className="bselect-badges">
          {selected.length === 0 ? (
            <span className="bselect-placeholder">{placeholder}</span>
          ) : (
            selected.map((v) => {
              const opt = options.find((o) => o.value === v);
              return (
                <span key={v} className="bselect-badge">
                  <span className="bselect-badge-label">{opt?.label ?? v}</span>
                  {multiple && (
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
            })
          )}
        </div>
        <ChevronDown size={14} className="bselect-chevron" />
      </button>

      {open && (
        <div className="bselect-menu" role="listbox" aria-multiselectable={multiple}>
          {options.map((opt) => {
            const isSelected = selected.includes(opt.value);
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
                {isSelected && <X size={14} className="bselect-item-x" />}
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}
