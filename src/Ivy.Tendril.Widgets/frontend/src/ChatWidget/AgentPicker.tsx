import React, { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { ChevronDown, SlidersHorizontal } from "lucide-react";
import { BrandIcon } from "../Shell/brandIcons";
import type { AgentOptionDto, EffortOptionDto, ModelOptionDto } from "./types";

export interface AgentPickerProps {
  agents: AgentOptionDto[];
  selectedAgent: string;
  selectedModel: string;
  selectedEffort: string;
  /** The selected agent's models and efforts, as the host resolved them. */
  models: ModelOptionDto[];
  efforts: EffortOptionDto[];
  supportsEffort: boolean;
  onAgentChange: (agentId: string) => void;
  /** A model or effort is chosen for one agent, selected or not, and remembered for it. */
  onModelChange: (agentId: string, modelId: string) => void;
  onEffortChange: (agentId: string, effortId: string) => void;
  /** Icon-only trigger, for the narrow composer of an embedded chat. */
  compact?: boolean;
}

interface SelectOption {
  value: string;
  label: string;
}

/** A native select, so its list floats over the page instead of growing the panel. */
const PanelSelect: React.FC<{
  title: string;
  value: string;
  options: SelectOption[];
  onChange: (value: string) => void;
}> = ({ title, value, options, onChange }) => (
  <label className="chat-panel-select" title={title}>
    <select
      className="chat-panel-select-native"
      aria-label={title}
      value={value}
      onChange={(e) => onChange(e.target.value)}
    >
      {!options.some((option) => option.value === value) && <option value={value}>{value || "Default"}</option>}
      {options.map((option) => (
        <option key={option.value} value={option.value}>
          {option.label}
        </option>
      ))}
    </select>
    <ChevronDown size={16} className="chat-panel-select-chevron" aria-hidden="true" />
  </label>
);

const DEFAULT_EFFORTS: SelectOption[] = [{ value: "default", label: "Default" }];
const PANEL_GAP = 8;
const VIEWPORT_MARGIN = 8;

/**
 * Where the layer starts before it is measured: fixed, so its width is its content's rather than
 * the body's, and hidden, so the reader never sees it jump into place.
 */
const UNPLACED_LAYER: React.CSSProperties = { position: "fixed", left: 0, bottom: 0, zIndex: 10000, visibility: "hidden" };

interface AgentSettings {
  models: SelectOption[];
  model: string;
  supportsEffort: boolean;
  efforts: SelectOption[];
  effort: string;
}

const hasSettings = (settings: AgentSettings) => settings.models.length > 0 || settings.supportsEffort;

/**
 * The composer's agent pill. Opens a menu of coding agents above it; clicking one selects it.
 * Each row reveals an options button on hover that opens a panel beside the row with that
 * agent's model and effort, which are remembered per agent without selecting it.
 */
export const AgentPicker: React.FC<AgentPickerProps> = ({
  agents,
  selectedAgent,
  selectedModel,
  selectedEffort,
  models,
  efforts,
  supportsEffort,
  onAgentChange,
  onModelChange,
  onEffortChange,
  compact = false,
}) => {
  const [open, setOpen] = useState(false);
  const [optionsAgentId, setOptionsAgentId] = useState<string | null>(null);
  const [layerStyle, setLayerStyle] = useState<React.CSSProperties>(UNPLACED_LAYER);
  const [panelTop, setPanelTop] = useState(0);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const layerRef = useRef<HTMLDivElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const rowRefs = useRef(new Map<string, HTMLDivElement>());

  // The selected agent is always listed, even when the host sent no agent list at all.
  const rows: AgentOptionDto[] = agents.length > 0 ? agents : [{ id: selectedAgent, label: selectedAgent }];
  const selected = rows.find((agent) => agent.id === selectedAgent);
  const label = selected?.label ?? selectedAgent;

  // An agent's panel offers its own remembered model and effort; the selected agent falls back
  // to the host's resolution of them.
  const settingsFor = (agent: AgentOptionDto): AgentSettings => {
    const isSelected = agent.id === selectedAgent;
    const modelSource = agent.models && agent.models.length > 0 ? agent.models : isSelected ? models : [];
    const modelOptions = modelSource.map((model) => ({ value: model.id, label: model.displayName }));
    const effortSource = agent.efforts && agent.efforts.length > 0 ? agent.efforts : isSelected ? efforts : [];
    return {
      models: modelOptions,
      model: agent.selectedModel ?? (isSelected ? selectedModel : (modelOptions[0]?.value ?? "default")),
      supportsEffort: agent.supportsEffort ?? (isSelected && supportsEffort),
      efforts:
        effortSource.length > 0
          ? effortSource.map((effort) => ({ value: effort.id, label: effort.displayName }))
          : DEFAULT_EFFORTS,
      effort: agent.selectedEffort ?? (isSelected ? selectedEffort : "default"),
    };
  };

  const optionsAgent = optionsAgentId != null ? rows.find((agent) => agent.id === optionsAgentId) : undefined;
  const optionsSettings = optionsAgent ? settingsFor(optionsAgent) : undefined;

  const place = useCallback(() => {
    const trigger = triggerRef.current;
    const menu = menuRef.current;
    if (!trigger || !menu) return;
    const rect = trigger.getBoundingClientRect();
    const menuWidth = menu.offsetWidth;
    const panelWidth = panelRef.current?.offsetWidth ?? 0;
    const layerWidth = menuWidth + (panelWidth > 0 ? PANEL_GAP + panelWidth : 0);
    // The menu's right edge sits on the pill's right edge so the panel opens outward;
    // the whole layer is then kept inside the viewport.
    const left = Math.max(
      VIEWPORT_MARGIN,
      Math.min(rect.right - menuWidth, window.innerWidth - layerWidth - VIEWPORT_MARGIN),
    );
    setLayerStyle({
      position: "fixed",
      bottom: Math.max(VIEWPORT_MARGIN, window.innerHeight - rect.top + PANEL_GAP),
      left,
      zIndex: 10000,
      visibility: "visible",
    });
  }, []);

  useLayoutEffect(() => {
    if (open) place();
  }, [open, place, optionsAgentId]);

  // The panel sits beside its row, top edges aligned, unless that would run off the bottom of
  // the viewport; then it grows upward from the row's bottom edge instead.
  useLayoutEffect(() => {
    if (!open || optionsAgentId == null) return;
    const row = rowRefs.current.get(optionsAgentId);
    const panel = panelRef.current;
    const layer = layerRef.current;
    if (!row || !panel || !layer) return;
    const rowRect = row.getBoundingClientRect();
    const layerTop = layer.getBoundingClientRect().top;
    const panelHeight = panel.offsetHeight;
    const fitsBelow = rowRect.top + panelHeight <= window.innerHeight - VIEWPORT_MARGIN;
    const top = Math.round((fitsBelow ? rowRect.top : rowRect.bottom - panelHeight) - layerTop);
    setPanelTop((current) => (current === top ? current : top));
  });

  useEffect(() => {
    if (!open) return;
    const onMouseDown = (e: MouseEvent) => {
      const target = e.target as Node;
      if (triggerRef.current?.contains(target) || layerRef.current?.contains(target)) return;
      setOpen(false);
    };
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") setOpen(false);
    };
    document.addEventListener("mousedown", onMouseDown);
    document.addEventListener("keydown", onKeyDown);
    window.addEventListener("resize", place);
    window.addEventListener("scroll", place, true);
    return () => {
      document.removeEventListener("mousedown", onMouseDown);
      document.removeEventListener("keydown", onKeyDown);
      window.removeEventListener("resize", place);
      window.removeEventListener("scroll", place, true);
    };
  }, [open, place]);

  const toggleMenu = () => {
    setOptionsAgentId(null);
    setLayerStyle(UNPLACED_LAYER);
    setOpen((state) => !state);
  };

  const chooseAgent = (agentId: string) => {
    if (agentId !== selectedAgent) onAgentChange(agentId);
    setOptionsAgentId(null);
    setOpen(false);
  };

  const toggleOptions = (agentId: string) =>
    setOptionsAgentId((current) => (current === agentId ? null : agentId));

  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        className="chat-agent-trigger"
        data-open={open}
        data-compact={compact}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-label={`Agent: ${label}`}
        title="Agent, model and effort"
        onClick={toggleMenu}
      >
        <BrandIcon name={selected?.icon} size={16} className="chat-agent-trigger-icon" />
        {!compact && <span className="chat-agent-trigger-label">{label}</span>}
      </button>

      {open &&
        createPortal(
          <div ref={layerRef} className="chat-agent-layer" style={layerStyle}>
            <div ref={menuRef} className="chat-agent-menu" role="menu" aria-label="Agents">
              {rows.map((agent) => {
                const settings = settingsFor(agent);
                const optionsOpen = agent.id === optionsAgentId;
                return (
                  <div
                    key={agent.id}
                    ref={(el) => {
                      if (el) rowRefs.current.set(agent.id, el);
                      else rowRefs.current.delete(agent.id);
                    }}
                    role="menuitemradio"
                    aria-checked={agent.id === selectedAgent}
                    aria-label={agent.label}
                    tabIndex={0}
                    className="chat-agent-row"
                    data-selected={agent.id === selectedAgent}
                    data-options-open={optionsOpen}
                    onClick={() => chooseAgent(agent.id)}
                    onKeyDown={(e) => {
                      if (e.key === "Enter" || e.key === " ") {
                        e.preventDefault();
                        chooseAgent(agent.id);
                      } else if (e.key === "ArrowRight" && hasSettings(settings)) {
                        e.preventDefault();
                        setOptionsAgentId(agent.id);
                      }
                    }}
                  >
                    <BrandIcon name={agent.icon} size={16} className="chat-agent-row-icon" />
                    <span className="chat-agent-row-label">{agent.label}</span>
                    {hasSettings(settings) && (
                      <button
                        type="button"
                        className="chat-agent-row-options"
                        aria-label={`${agent.label} options`}
                        aria-expanded={optionsOpen}
                        title="Model and effort"
                        onClick={(e) => {
                          e.stopPropagation();
                          toggleOptions(agent.id);
                        }}
                        onKeyDown={(e) => e.stopPropagation()}
                      >
                        <SlidersHorizontal size={14} />
                      </button>
                    )}
                  </div>
                );
              })}
            </div>
            {optionsAgent && optionsSettings && (
              <div
                ref={panelRef}
                className="chat-agent-panel"
                role="group"
                aria-label={`${optionsAgent.label} settings`}
                style={{ top: panelTop }}
              >
                <div className="chat-agent-panel-title">{optionsAgent.label}</div>
                {optionsSettings.models.length > 0 && (
                  <PanelSelect
                    title="Model"
                    value={optionsSettings.model}
                    options={optionsSettings.models}
                    onChange={(modelId) => onModelChange(optionsAgent.id, modelId)}
                  />
                )}
                {optionsSettings.supportsEffort && (
                  <PanelSelect
                    title="Effort Level"
                    value={optionsSettings.effort}
                    options={optionsSettings.efforts}
                    onChange={(effortId) => onEffortChange(optionsAgent.id, effortId)}
                  />
                )}
              </div>
            )}
          </div>,
          document.body,
        )}
    </>
  );
};
