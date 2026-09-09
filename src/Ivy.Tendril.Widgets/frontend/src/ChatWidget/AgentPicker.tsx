import React, { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { Check, ChevronDown, ChevronRight } from "lucide-react";
import { BrandIcon } from "../Shell/brandIcons";
import { Tooltip } from "../ui/Tooltip";
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
  onModelChange: (modelId: string) => void;
  onEffortChange: (effortId: string) => void;
}

interface SelectOption {
  value: string;
  label: string;
}

const PanelSelect: React.FC<{
  title: string;
  value: string;
  options: SelectOption[];
  onChange: (value: string) => void;
}> = ({ title, value, options, onChange }) => {
  const [open, setOpen] = useState(false);
  const current = options.find((option) => option.value === value)?.label ?? (value || "Default");

  return (
    <div className="chat-panel-select" title={title} data-open={open}>
      <button
        type="button"
        className="chat-panel-select-trigger"
        aria-label={title}
        aria-expanded={open}
        onClick={() => setOpen((state) => !state)}
      >
        <span className="chat-panel-select-value">{current}</span>
        <ChevronDown size={16} className="chat-panel-select-chevron" />
      </button>
      {open && (
        <div className="chat-panel-select-list">
          {options.map((option) => (
            <button
              key={option.value}
              type="button"
              className="chat-panel-select-item"
              data-selected={option.value === value}
              onClick={() => {
                onChange(option.value);
                setOpen(false);
              }}
            >
              <span>{option.label}</span>
              {option.value === value && <Check size={14} />}
            </button>
          ))}
        </div>
      )}
    </div>
  );
};

const DEFAULT_EFFORTS: SelectOption[] = [{ value: "default", label: "Default" }];

/**
 * Where the layer starts before it is measured: fixed, so its width is its content's rather than
 * the body's, and hidden, so the reader never sees it jump into place.
 */
const UNPLACED_LAYER: React.CSSProperties = { position: "fixed", left: 0, bottom: 0, zIndex: 10000, visibility: "hidden" };

/**
 * The composer's agent pill. Opens a menu of coding agents above it; the agent under the pointer
 * (or the selected one) gets a side panel with its model and effort selects, so a model can be
 * picked for another agent in one motion, which selects that agent too.
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
}) => {
  const [open, setOpen] = useState(false);
  const [hovered, setHovered] = useState<string | null>(null);
  const [layerStyle, setLayerStyle] = useState<React.CSSProperties>(UNPLACED_LAYER);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const layerRef = useRef<HTMLDivElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);

  const selected = agents.find((agent) => agent.id === selectedAgent);
  const label = selected?.label ?? selectedAgent;

  const panelAgentId = hovered ?? selectedAgent;
  const panelAgent = agents.find((agent) => agent.id === panelAgentId);
  const panelIsSelected = panelAgentId === selectedAgent;
  const panelModels: SelectOption[] = (panelIsSelected ? models : (panelAgent?.models ?? [])).map((model) => ({
    value: model.id,
    label: model.displayName,
  }));
  const panelModelValue = panelIsSelected ? selectedModel : (panelModels[0]?.value ?? "default");
  const panelSupportsEffort = panelIsSelected ? supportsEffort : Boolean(panelAgent?.supportsEffort);
  const panelEfforts: SelectOption[] = panelIsSelected
    ? efforts.map((effort) => ({ value: effort.id, label: effort.displayName }))
    : DEFAULT_EFFORTS;
  const panelEffortValue = panelIsSelected ? selectedEffort : "default";

  const place = useCallback(() => {
    const trigger = triggerRef.current;
    if (!trigger) return;
    const rect = trigger.getBoundingClientRect();
    const layerWidth = layerRef.current?.offsetWidth ?? 0;
    const menuWidth = menuRef.current?.offsetWidth ?? layerWidth;
    // The menu's right edge sits on the pill's right edge so the side panel opens outward;
    // the whole layer is then kept inside the viewport.
    const left = Math.max(8, Math.min(rect.right - menuWidth, window.innerWidth - layerWidth - 8));
    setLayerStyle({
      position: "fixed",
      bottom: Math.max(8, window.innerHeight - rect.top + 8),
      left,
      zIndex: 10000,
      visibility: "visible",
    });
  }, []);

  useLayoutEffect(() => {
    if (open) place();
  }, [open, place, panelModels.length, panelEfforts.length]);

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
    setHovered(null);
    setLayerStyle(UNPLACED_LAYER);
    setOpen((state) => !state);
  };

  const chooseAgent = (agentId: string) => {
    if (agentId !== selectedAgent) onAgentChange(agentId);
    setHovered(agentId);
  };

  const chooseModel = (modelId: string) => {
    if (!panelIsSelected) onAgentChange(panelAgentId);
    onModelChange(modelId);
  };

  const chooseEffort = (effortId: string) => {
    if (!panelIsSelected) onAgentChange(panelAgentId);
    onEffortChange(effortId);
  };

  // The selected agent always gets its panel, even when the host sent no agent list at all.
  const showPanel = (panelIsSelected || panelAgent !== undefined) && (panelModels.length > 0 || panelSupportsEffort);
  const panelLabel = panelAgent?.label ?? label;

  return (
    <>
      <Tooltip content="Agent, model and effort">
        <button
          ref={triggerRef}
          type="button"
          className="chat-agent-trigger"
          data-open={open}
          aria-haspopup="menu"
          aria-expanded={open}
          aria-label={`Agent: ${label}`}
          onClick={toggleMenu}
        >
          <BrandIcon name={selected?.icon} size={16} className="chat-agent-trigger-icon" />
          <span className="chat-agent-trigger-label">{label}</span>
        </button>
      </Tooltip>

      {open &&
        createPortal(
          <div ref={layerRef} className="chat-agent-layer" style={layerStyle}>
            <div ref={menuRef} className="chat-agent-menu" role="menu" aria-label="Agents">
              {agents.map((agent) => (
                <button
                  key={agent.id}
                  type="button"
                  role="menuitem"
                  className="chat-agent-row"
                  data-active={agent.id === panelAgentId}
                  data-selected={agent.id === selectedAgent}
                  onMouseEnter={() => setHovered(agent.id)}
                  onFocus={() => setHovered(agent.id)}
                  onClick={() => chooseAgent(agent.id)}
                >
                  <BrandIcon name={agent.icon} size={16} className="chat-agent-row-icon" />
                  <span className="chat-agent-row-label">{agent.label}</span>
                  <ChevronRight size={16} className="chat-agent-row-chevron" />
                </button>
              ))}
            </div>
            {showPanel && (
              <div className="chat-agent-panel" aria-label={`${panelLabel} settings`}>
                {panelModels.length > 0 && (
                  <PanelSelect title="Model" value={panelModelValue} options={panelModels} onChange={chooseModel} />
                )}
                {panelSupportsEffort && panelEfforts.length > 0 && (
                  <PanelSelect
                    title="Effort Level"
                    value={panelEffortValue}
                    options={panelEfforts}
                    onChange={chooseEffort}
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
