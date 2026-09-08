import React, { useEffect, useRef, useState } from "react";
import {
  ArrowLeft,
  ArrowRight,
  Check,
  Monitor,
  RefreshCw,
  Smartphone,
  SquareDashedMousePointer,
  TabletSmartphone,
  type LucideIcon,
} from "lucide-react";
import { Tooltip } from "./Tooltip";
import { Floating } from "./floating";
import { actionIcon } from "./icons";
import { addressParts } from "./address";
import { DEVICE_LABELS, DEVICE_ORDER, type DeviceKey } from "./devices";

export interface ToolbarAction {
  id: string;
  icon: string;
  label: string;
  active?: boolean;
  disabled?: boolean;
  badge?: string | null;
  primary?: boolean;
}

export interface ToolbarProps {
  url: string | null;
  canGoBack: boolean;
  canGoForward: boolean;
  loading: boolean;
  device: DeviceKey;
  selecting: boolean;
  actions: ToolbarAction[];
  onBack: () => void;
  onForward: () => void;
  onReload: () => void;
  onNavigate: (url: string) => void;
  onDevice: (device: DeviceKey) => void;
  onToggleSelect: () => void;
  onAction: (id: string) => void;
}

const ICON_SIZE = 16;
const ICON_STROKE = 1.75;

const DEVICE_ICONS: Record<DeviceKey, LucideIcon> = {
  desktop: Monitor,
  tablet: TabletSmartphone,
  mobile: Smartphone,
};

interface IconButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  label: string;
  icon: LucideIcon;
  active?: boolean;
  primary?: boolean;
  badge?: string | null;
  tooltipDisabled?: boolean;
}

const IconButton = React.forwardRef<HTMLButtonElement, IconButtonProps>(
  ({ label, icon: Icon, active, primary, badge, tooltipDisabled, ...rest }, ref) => (
    <Tooltip label={label} disabled={tooltipDisabled}>
      <button
        ref={ref}
        type="button"
        className="wvr-icon-btn"
        aria-label={label}
        data-active={active ? "true" : undefined}
        data-primary={primary ? "true" : undefined}
        {...rest}
      >
        <Icon size={ICON_SIZE} strokeWidth={ICON_STROKE} aria-hidden="true" />
        {badge && <span className="wvr-badge">{badge}</span>}
      </button>
    </Tooltip>
  ),
);
IconButton.displayName = "IconButton";

interface AddressBarProps {
  url: string | null;
  onNavigate: (url: string) => void;
}

const AddressBar: React.FC<AddressBarProps> = ({ url, onNavigate }) => {
  const [draft, setDraft] = useState<string | null>(null);

  if (draft === null) {
    const parts = url ? addressParts(url) : null;
    return (
      <button
        type="button"
        className="wvr-address wvr-address-display"
        aria-label="Address"
        title={url ?? undefined}
        onClick={() => setDraft(url ?? "")}
      >
        {parts ? (
          <span className="wvr-address-text">
            <span className="wvr-address-host">{parts.host}</span>
            <span className="wvr-address-path">{parts.path}</span>
          </span>
        ) : (
          <span className="wvr-address-placeholder">Enter a URL</span>
        )}
      </button>
    );
  }

  const commit = () => {
    const next = draft.trim();
    setDraft(null);
    if (next) onNavigate(next);
  };

  return (
    <input
      className="wvr-address wvr-address-input"
      aria-label="Address"
      value={draft}
      autoFocus
      spellCheck={false}
      autoComplete="off"
      onFocus={(e) => e.currentTarget.select()}
      onChange={(e) => setDraft(e.target.value)}
      onBlur={() => setDraft(null)}
      onKeyDown={(e) => {
        if (e.key === "Enter") commit();
        else if (e.key === "Escape") setDraft(null);
      }}
    />
  );
};

interface DeviceMenuProps {
  device: DeviceKey;
  onDevice: (device: DeviceKey) => void;
}

const DeviceMenu: React.FC<DeviceMenuProps> = ({ device, onDevice }) => {
  const [open, setOpen] = useState(false);
  const buttonRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!open) return;
    const onMouseDown = (e: MouseEvent) => {
      const target = e.target as Element | null;
      if (target?.closest(".wvr-menu") || buttonRef.current?.contains(target)) return;
      setOpen(false);
    };
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key !== "Escape") return;
      setOpen(false);
      buttonRef.current?.focus();
    };
    document.addEventListener("mousedown", onMouseDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("mousedown", onMouseDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);

  const CurrentIcon = DEVICE_ICONS[device];
  return (
    <>
      <IconButton
        ref={buttonRef}
        label={`Viewport: ${DEVICE_LABELS[device]}`}
        icon={CurrentIcon}
        active={open}
        tooltipDisabled={open}
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen((value) => !value)}
      />
      {open && buttonRef.current && (
        <Floating anchor={buttonRef.current} align="end" className="wvr-menu" role="menu">
          {DEVICE_ORDER.map((key) => {
            const Icon = DEVICE_ICONS[key];
            return (
              <button
                key={key}
                type="button"
                role="menuitemradio"
                aria-checked={key === device}
                className="wvr-menu-item"
                onClick={() => {
                  onDevice(key);
                  setOpen(false);
                }}
              >
                <Icon size={ICON_SIZE} strokeWidth={ICON_STROKE} aria-hidden="true" />
                <span className="wvr-menu-item-label">{DEVICE_LABELS[key]}</span>
                <Check className="wvr-menu-check" size={14} aria-hidden="true" />
              </button>
            );
          })}
        </Floating>
      )}
    </>
  );
};

export const Toolbar: React.FC<ToolbarProps> = ({
  url,
  canGoBack,
  canGoForward,
  loading,
  device,
  selecting,
  actions,
  onBack,
  onForward,
  onReload,
  onNavigate,
  onDevice,
  onToggleSelect,
  onAction,
}) => (
  <div className="wvr-bar" role="toolbar" aria-label="Browser controls">
    <div className="wvr-bar-group">
      <IconButton label="Back" icon={ArrowLeft} disabled={!canGoBack} onClick={onBack} />
      <IconButton label="Forward" icon={ArrowRight} disabled={!canGoForward} onClick={onForward} />
      <IconButton label="Reload" icon={RefreshCw} onClick={onReload} />
    </div>
    <div className="wvr-bar-center">
      <AddressBar url={url} onNavigate={onNavigate} />
    </div>
    <div className="wvr-bar-group wvr-bar-end">
      <IconButton
        label={selecting ? "Stop selecting" : "Select an element to comment on"}
        icon={SquareDashedMousePointer}
        active={selecting}
        aria-pressed={selecting}
        onClick={onToggleSelect}
      />
      <DeviceMenu device={device} onDevice={onDevice} />
      {actions.map((action) => (
        <IconButton
          key={action.id}
          label={action.label}
          icon={actionIcon(action.icon)}
          active={action.active}
          primary={action.primary}
          badge={action.badge}
          disabled={action.disabled}
          aria-pressed={action.active === undefined ? undefined : action.active}
          onClick={() => onAction(action.id)}
        />
      ))}
    </div>
    {loading && <div className="wvr-progress" aria-hidden="true" />}
  </div>
);
