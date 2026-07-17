import React, { useState, useEffect, useRef } from "react";
import { 
  Trash2, 
  Plus, 
  Search, 
  ChevronDown, 
  ChevronRight, 
  Zap, 
  Workflow, 
  Lock, 
  Terminal, 
  Layers,
  Link
} from "lucide-react";
import "./workflow-builder.css";

type IvyEventHandler = (eventName: string, widgetId: string, args: unknown[]) => void;

interface ConnectionItem {
  id: number;
  name: string;
  provider: string;
  permissions: string;
}

interface WorkflowStep {
  id: string;
  name: string;
  type: string; // "Trigger", "Connection", "Prompt"
  connectionName: string;
  action: string;
  args: string;
  provider?: string;
  model?: string;
  next: string[];
  x: number;
  y: number;
}

interface WorkflowDefinition {
  steps: WorkflowStep[];
}

interface WorkflowItem {
  id: number;
  name: string;
  description: string;
  project: string;
  definition: string;
  isActive: boolean;
  isSystem: boolean;
  created: string;
  updated: string;
}

interface CustomPrompt {
  name: string;
  template: string;
}

interface WorkflowBuilderProps {
  id: string;
  workflowDefinitionJson?: string;
  availableConnections?: ConnectionItem[];
  availableProviders?: string[];
  isReadOnly?: boolean;
  selectedNodeId?: string;
  selectedWorkflowId?: number;
  
  // Unified Sidebar Props
  workflows?: WorkflowItem[];
  systemPromptwares?: string[];
  projects?: string[];
  selectedProject?: string;
  
  eventHandler: IvyEventHandler;
}

export const WorkflowBuilder: React.FC<WorkflowBuilderProps> = ({
  id,
  workflowDefinitionJson = "",
  availableConnections = [],
  availableProviders = [],
  isReadOnly = false,
  selectedNodeId = "",
  selectedWorkflowId = 0,
  
  // Unified Sidebar Default values
  workflows = [],
  systemPromptwares = [],
  projects = [],
  selectedProject = "",
  
  eventHandler,
}) => {
  const [steps, setSteps] = useState<WorkflowStep[]>([]);
  const [isDragging, setIsDragging] = useState(false);
  const [draggedStepId, setDraggedStepId] = useState<string | null>(null);
  const [dragOffset, setDragOffset] = useState({ x: 0, y: 0 });
  const [tempConnection, setTempConnection] = useState<{
    fromId: string;
    fromX: number;
    fromY: number;
    toX: number;
    toY: number;
  } | null>(null);
  const [spawnMenuId, setSpawnMenuId] = useState<string | null>(null);
  const [selectedNode, setSelectedNode] = useState<string | null>(selectedNodeId || null);

  // Sidebar Accordion states
  const [expandedCategories, setExpandedCategories] = useState<{ [key: string]: boolean }>({
    prompts: false,
    connections: false,
    triggers: false,
    flows: false,
    templates: false,
  });

  // Global search or category search states
  const [promptSearch, setPromptSearch] = useState("");
  const [connectionSearch, setConnectionSearch] = useState("");
  const [triggerSearch, setTriggerSearch] = useState("");
  const [flowSearch, setFlowSearch] = useState("");
  const [templateSearch, setTemplateSearch] = useState("");

  // Custom Prompt Creation states
  const [customPrompts, setCustomPrompts] = useState<CustomPrompt[]>(() => {
    try {
      const saved = localStorage.getItem("ivy-custom-prompts");
      return saved ? JSON.parse(saved) : [];
    } catch {
      return [];
    }
  });
  const [showCreatePrompt, setShowCreatePrompt] = useState(false);
  const [newPromptName, setNewPromptName] = useState("");
  const [newPromptTemplate, setNewPromptTemplate] = useState("");

  const canvasRef = useRef<HTMLDivElement>(null);

  const [cardHeights, setCardHeights] = useState<{ [id: string]: number }>({});
  const cardRefs = useRef<{ [id: string]: HTMLDivElement | null }>({});
  const resizeObservers = useRef<{ [id: string]: ResizeObserver }>({});

  const measureCardRef = (stepId: string) => (node: HTMLDivElement | null) => {
    if (node) {
      cardRefs.current[stepId] = node;
      if (resizeObservers.current[stepId]) {
        resizeObservers.current[stepId].disconnect();
      }
      const observer = new ResizeObserver((entries) => {
        for (const entry of entries) {
          const newHeight = entry.borderBoxSize?.[0]?.blockSize ?? entry.contentRect.height;
          setCardHeights((prev) => {
            if (prev[stepId] === newHeight) return prev;
            return { ...prev, [stepId]: newHeight };
          });
        }
      });
      observer.observe(node);
      resizeObservers.current[stepId] = observer;
    } else {
      if (resizeObservers.current[stepId]) {
        resizeObservers.current[stepId].disconnect();
        delete resizeObservers.current[stepId];
      }
      delete cardRefs.current[stepId];
    }
  };

  useEffect(() => {
    return () => {
      Object.values(resizeObservers.current).forEach((obs) => obs.disconnect());
    };
  }, []);

  useEffect(() => {
    try {
      if (workflowDefinitionJson) {
        const parsed = JSON.parse(workflowDefinitionJson) as WorkflowDefinition;
        if (parsed && Array.isArray(parsed.steps)) {
          // Normalize coordinates if missing
          const normalized = parsed.steps.map((s, idx) => ({
            ...s,
            x: s.x ?? idx * 340 + 50,
            y: s.y ?? 150,
          }));
          setSteps(normalized);
          return;
        }
      }
    } catch (e) {
      console.error("Failed to parse workflow definition JSON:", e);
    }

    setSteps([
      {
        id: "start",
        name: "Start",
        type: "Trigger",
        connectionName: "",
        action: "",
        args: "{}",
        next: [],
        x: 50,
        y: 200,
      },
    ]);
  }, [workflowDefinitionJson]);

  useEffect(() => {
    if (selectedNodeId) {
      setSelectedNode(selectedNodeId);
    }
  }, [selectedNodeId]);

  // Sync back steps whenever they change
  useEffect(() => {
    if (steps.length > 0) {
      const definition: WorkflowDefinition = { steps };
      const jsonStr = JSON.stringify(definition, null, 2);
      // Auto-save changes back to container state without explicit button click
      eventHandler("OnSave", id, [jsonStr]);
    }
  }, [steps]);

  const getCardHeight = (step: WorkflowStep): number => {
    const type = step.type.toLowerCase();
    if (type === "trigger") {
      const action = step.action || "manual";
      if (action === "webhook") {
        return step.connectionName ? 180 : 240;
      }
      if (action === "schedule") {
        return 190;
      }
      if (action === "event") {
        return 155;
      }
      return 140; // manual
    }
    if (type === "connection") {
      return 250;
    }
    if (type === "prompt") {
      return 220;
    }
    return 250; // fallback
  };

  const getPortCoords = (s: WorkflowStep) => {
    const w = 280;
    const h = cardHeights[s.id] || getCardHeight(s);

    const x = s.x || 0;
    const y = s.y || 0;
    return {
      input: { x, y: y + h / 2 },
      output: { x: x + w, y: y + h / 2 },
    };
  };

  const handleCardClick = (stepId: string) => {
    setSelectedNode(stepId);
    eventHandler("OnNodeSelect", id, [stepId]);
  };

  // Drag Card
  const handleCardMouseDown = (e: React.MouseEvent, stepId: string) => {
    if (isReadOnly) return;
    const target = e.target as HTMLElement;
    if (
      target.tagName.toLowerCase() === "input" ||
      target.tagName.toLowerCase() === "select" ||
      target.tagName.toLowerCase() === "textarea" ||
      target.tagName.toLowerCase() === "button"
    ) {
      return;
    }
    e.preventDefault();
    const step = steps.find((s) => s.id === stepId);
    if (!step) return;

    setIsDragging(true);
    setDraggedStepId(stepId);
    setDragOffset({
      x: e.clientX - step.x,
      y: e.clientY - step.y,
    });
  };

  const handleMouseMove = (e: React.MouseEvent) => {
    if (isDragging && draggedStepId) {
      const x = e.clientX - dragOffset.x;
      const y = e.clientY - dragOffset.y;
      setSteps(
        steps.map((s) =>
          s.id === draggedStepId
            ? { ...s, x: Math.max(0, x), y: Math.max(0, y) }
            : s
        )
      );
    } else if (tempConnection) {
      if (!canvasRef.current) return;
      const rect = canvasRef.current.getBoundingClientRect();
      setTempConnection({
        ...tempConnection,
        toX: e.clientX - rect.left + canvasRef.current.scrollLeft,
        toY: e.clientY - rect.top + canvasRef.current.scrollTop,
      });
    }
  };

  const handleMouseUp = () => {
    setIsDragging(false);
    setDraggedStepId(null);
    setTempConnection(null);
  };

  // Drag Port Connection
  const handlePortMouseDown = (e: React.MouseEvent, step: WorkflowStep) => {
    if (isReadOnly) return;
    e.stopPropagation();
    e.preventDefault();
    const coords = getPortCoords(step);
    if (!canvasRef.current) return;
    const rect = canvasRef.current.getBoundingClientRect();
    setTempConnection({
      fromId: step.id,
      fromX: coords.output.x,
      fromY: coords.output.y,
      toX: e.clientX - rect.left + canvasRef.current.scrollLeft,
      toY: e.clientY - rect.top + canvasRef.current.scrollTop,
    });
  };

  const handlePortMouseUp = (e: React.MouseEvent, targetStep: WorkflowStep) => {
    if (isReadOnly) return;
    e.stopPropagation();
    if (tempConnection && tempConnection.fromId !== targetStep.id) {
      const updated = steps.map((s) => {
        if (s.id === tempConnection.fromId) {
          if (!s.next.includes(targetStep.id)) {
            return { ...s, next: [...s.next, targetStep.id] };
          }
        }
        return s;
      });
      setSteps(updated);
    }
    setTempConnection(null);
  };

  // HTML5 Drag Sidebar Palette Item
  const handleDragStart = (
    e: React.DragEvent,
    type: string,
    connectionName: string = "",
    provider: string = "",
    promptType: string = "",
    args: string = "",
    action: string = ""
  ) => {
    if (isReadOnly) return;
    e.dataTransfer.setData("type", type);
    e.dataTransfer.setData("connectionName", connectionName);
    e.dataTransfer.setData("provider", provider);
    e.dataTransfer.setData("promptType", promptType);
    e.dataTransfer.setData("args", args);
    e.dataTransfer.setData("action", action);
  };

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault();
    if (isReadOnly) return;
    if (!canvasRef.current) return;
    const rect = canvasRef.current.getBoundingClientRect();
    const x = e.clientX - rect.left + canvasRef.current.scrollLeft - 140;
    const y = e.clientY - rect.top + canvasRef.current.scrollTop - 50;

    const type = e.dataTransfer.getData("type");
    const connectionName = e.dataTransfer.getData("connectionName");
    const provider = e.dataTransfer.getData("provider");
    const draggedArgs = e.dataTransfer.getData("args");
    const draggedAction = e.dataTransfer.getData("action");

    if (!type) return;

    // Default arguments setup
    let defaultArgs = "Your prompt here...";
    if (type === "Connection") {
      defaultArgs = "{}";
    } else if (type === "Trigger") {
      defaultArgs = "{}";
    }

    if (draggedArgs) {
      defaultArgs = draggedArgs;
    }

    let stepName = `${type}_Step_${steps.length + 1}`;
    if (type === "Trigger") {
      if (draggedAction === "webhook") {
        stepName = connectionName ? `${connectionName}_Trigger_Step_${steps.length + 1}` : `Webhook_Trigger_Step_${steps.length + 1}`;
      } else {
        stepName = `Manual_Trigger_Step_${steps.length + 1}`;
      }
    }

    const newStep: WorkflowStep = {
      id: Math.random().toString(36).substring(2, 9),
      name: stepName,
      type,
      connectionName: connectionName || "",
      action: draggedAction || "",
      args: defaultArgs,
      provider: type === "Prompt" ? (provider || availableProviders[0] || "default") : "",
      model: type === "Prompt" ? "default" : "",
      next: [],
      x: Math.max(0, x),
      y: Math.max(0, y),
    };

    setSteps([...steps, newStep]);
  };

  // Quick Spawn Connected Element (+)
  const spawnStep = (sourceId: string, type: "Connection" | "Prompt", connectionName = "") => {
    if (isReadOnly) return;
    const sourceStep = steps.find((s) => s.id === sourceId);
    if (!sourceStep) return;

    const newStep: WorkflowStep = {
      id: Math.random().toString(36).substring(2, 9),
      name: `${type}_Step_${steps.length + 1}`,
      type,
      connectionName: connectionName,
      action: "",
      args: type === "Connection" ? "{}" : "Your prompt here...",
      provider: type === "Prompt" ? (availableProviders[0] || "default") : "",
      model: type === "Prompt" ? "default" : "",
      next: [],
      x: sourceStep.x + 340,
      y: sourceStep.y,
    };

    const updated = steps.map((s) => {
      if (s.id === sourceId) {
        return { ...s, next: [...s.next, newStep.id] };
      }
      return s;
    });

    setSteps([...updated, newStep]);
    setSpawnMenuId(null);
  };

  const removeStep = (stepId: string) => {
    if (isReadOnly) return;
    const updated = steps
      .filter((s) => s.id !== stepId)
      .map((s) => ({
        ...s,
        next: s.next.filter((nid) => nid !== stepId),
      }));
    setSteps(updated);
  };

  const removeConnection = (fromId: string, toId: string) => {
    if (isReadOnly) return;
    setSteps(
      steps.map((s) =>
        s.id === fromId ? { ...s, next: s.next.filter((id) => id !== toId) } : s
      )
    );
  };

  const updateStep = (stepId: string, updates: Partial<WorkflowStep>) => {
    if (isReadOnly) return;
    setSteps(steps.map((s) => (s.id === stepId ? { ...s, ...updates } : s)));
  };

  const drawBezier = (fx: number, fy: number, tx: number, ty: number) => {
    const dx = tx - fx;
    const cx1 = fx + dx * 0.5;
    const cx2 = tx - dx * 0.5;
    return `M ${fx} ${fy} C ${cx1} ${fy}, ${cx2} ${ty}, ${tx} ${ty}`;
  };

  const toggleCategory = (cat: string) => {
    setExpandedCategories(prev => ({ ...prev, [cat]: !prev[cat] }));
  };

  // Custom Prompt handlers
  const handleSaveCustomPrompt = () => {
    const name = newPromptName.trim();
    if (!name) return;
    const updated = [...customPrompts, { name, template: newPromptTemplate }];
    setCustomPrompts(updated);
    localStorage.setItem("ivy-custom-prompts", JSON.stringify(updated));
    setNewPromptName("");
    setNewPromptTemplate("");
    setShowCreatePrompt(false);
  };

  const handleDeleteCustomPrompt = (e: React.MouseEvent, index: number) => {
    e.stopPropagation();
    const updated = customPrompts.filter((_, i) => i !== index);
    setCustomPrompts(updated);
    localStorage.setItem("ivy-custom-prompts", JSON.stringify(updated));
  };

  // Filtered lists for rendering
  const filteredPromptwares = (systemPromptwares.length > 0 ? systemPromptwares : availableProviders)
    .filter(p => !promptSearch || p.toLowerCase().includes(promptSearch.toLowerCase()));

  const filteredCustomPrompts = customPrompts
    .filter(cp => !promptSearch || cp.name.toLowerCase().includes(promptSearch.toLowerCase()));

  const fastAgents = ["claude", "codex", "antigravity", "copilot"]
    .filter(a => !promptSearch || a.toLowerCase().includes(promptSearch.toLowerCase()));

  const filteredConnections = availableConnections
    .filter(c => !connectionSearch || c.name.toLowerCase().includes(connectionSearch.toLowerCase()) || c.provider.toLowerCase().includes(connectionSearch.toLowerCase()));

  const triggersList = [
    { name: "Manual Trigger", action: "manual", connectionName: "", provider: "system" },
    { name: "Webhook Trigger", action: "webhook", connectionName: "", provider: "system" },
    { name: "Timed Trigger (Cron)", action: "schedule", connectionName: "", provider: "system" },
    { name: "Tendril Event Trigger", action: "event", connectionName: "", provider: "system" },
    ...availableConnections.map(c => ({
      name: `${c.name} Trigger`,
      action: "webhook",
      connectionName: c.name,
      provider: c.provider
    }))
  ].filter(t => !triggerSearch || t.name.toLowerCase().includes(triggerSearch.toLowerCase()));

  const systemFlows = workflows.filter(w => w.isSystem);
  const filteredSystemFlows = systemFlows
    .filter(f => !flowSearch || f.name.toLowerCase().includes(flowSearch.toLowerCase()));

  const userTemplates = workflows.filter(w => !w.isSystem);
  const filteredUserTemplates = userTemplates
    .filter(t => !templateSearch || t.name.toLowerCase().includes(templateSearch.toLowerCase()));

  // Active loaded workflow
  const loadedWorkflow = (() => {
    if (selectedWorkflowId) {
      return workflows.find(w => w.id === selectedWorkflowId);
    }
    if (!workflowDefinitionJson) return undefined;
    try {
      const parsedTarget = JSON.parse(workflowDefinitionJson);
      return workflows.find(w => {
        try {
          return JSON.stringify(JSON.parse(w.definition || "{}")) === JSON.stringify(parsedTarget);
        } catch {
          return false;
        }
      });
    } catch {
      return undefined;
    }
  })();

  return (
    <div className={`wfb-shell ${!workflowDefinitionJson ? "wfb-no-header" : ""}`}>
      {/* Redesigned Sidebar */}
      <div className="wfb-sidebar">
        
        {/* Project Selector */}
        {projects.length > 0 && (
          <div className="wfb-project-selector-container">
            <label className="wfb-label">Project</label>
            <select
              className="wfb-select"
              value={selectedProject}
              onChange={(e) => eventHandler("OnProjectSelect", id, [e.target.value])}
            >
              {projects.map((proj) => (
                <option key={proj} value={proj}>
                  {proj}
                </option>
              ))}
            </select>
          </div>
        )}

        {/* Categories list */}
        <div className="wfb-categories-accordion">
          
          {/* Category: Prompts */}
          <div className={`wfb-accordion-item ${expandedCategories.prompts ? "expanded" : ""}`}>
            <button className="wfb-accordion-header" onClick={() => toggleCategory("prompts")}>
              <span className="wfb-header-title">
                {expandedCategories.prompts ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
                <Terminal size={14} className="icon-prompt" />
                Prompts & Actions
              </span>
            </button>
            
            {expandedCategories.prompts && (
              <div className="wfb-accordion-content">
                <div className="wfb-search-wrapper">
                  <Search size={12} className="wfb-search-icon" />
                  <input
                    type="text"
                    className="wfb-category-search"
                    placeholder="Search prompts & actions..."
                    value={promptSearch}
                    onChange={(e) => setPromptSearch(e.target.value)}
                  />
                  {!isReadOnly && (
                    <button className="wfb-add-inline-btn" title="Create Custom Prompt" onClick={() => setShowCreatePrompt(!showCreatePrompt)}>
                      <Plus size={14} />
                    </button>
                  )}
                </div>

                {/* Inline form to create custom prompt */}
                {showCreatePrompt && (
                  <div className="wfb-inline-prompt-form">
                    <input
                      type="text"
                      className="wfb-input"
                      placeholder="Prompt Name (e.g. Audit)"
                      value={newPromptName}
                      onChange={(e) => setNewPromptName(e.target.value.replace(/\s+/g, ""))}
                    />
                    <textarea
                      className="wfb-textarea"
                      placeholder="System instructions/prompt template..."
                      value={newPromptTemplate}
                      onChange={(e) => setNewPromptTemplate(e.target.value)}
                    />
                    <div className="wfb-inline-form-actions">
                      <button className="wfb-btn-small outline" onClick={() => setShowCreatePrompt(false)}>Cancel</button>
                      <button className="wfb-btn-small primary" onClick={handleSaveCustomPrompt}>Save</button>
                    </div>
                  </div>
                )}

                <div className="wfb-palette-list">
                  {/* System Promptwares */}
                  {filteredPromptwares.map((pw) => (
                    <div
                      key={`system-pw-${pw}`}
                      className="wfb-palette-item"
                      draggable
                      onDragStart={(e) => handleDragStart(e, "Prompt", "", pw, "Promptware", "Analyze code...")}
                    >
                      <span className="wfb-step-badge wfb-badge-prompt">System</span>
                      <span className="wfb-item-text">{pw}</span>
                    </div>
                  ))}

                  {/* Custom Promptwares */}
                  {filteredCustomPrompts.map((cp, idx) => (
                    <div
                      key={`custom-pw-${cp.name}`}
                      className="wfb-palette-item wfb-custom-prompt-item"
                      draggable
                      onDragStart={(e) => handleDragStart(e, "Prompt", "", cp.name, "Promptware", cp.template)}
                    >
                      <span className="wfb-step-badge wfb-badge-custom-prompt">Custom</span>
                      <span className="wfb-item-text">{cp.name}</span>
                      <button className="wfb-delete-prompt-btn" onClick={(e) => handleDeleteCustomPrompt(e, idx)}>
                        <Trash2 size={12} />
                      </button>
                    </div>
                  ))}

                  {/* Fast Agents */}
                  {fastAgents.map((agent) => (
                    <div
                      key={`agent-${agent}`}
                      className="wfb-palette-item"
                      draggable
                      onDragStart={(e) => handleDragStart(e, "Prompt", "", agent, "FastAgent")}
                    >
                      <span className="wfb-step-badge wfb-badge-agent">Agent</span>
                      <span className="wfb-item-text">{agent}</span>
                    </div>
                  ))}

                  {filteredPromptwares.length === 0 && filteredCustomPrompts.length === 0 && fastAgents.length === 0 && (
                    <div className="wfb-no-items">No prompts found</div>
                  )}
                </div>
              </div>
            )}
          </div>

          {/* Category: Connections */}
          <div className={`wfb-accordion-item ${expandedCategories.connections ? "expanded" : ""}`}>
            <button className="wfb-accordion-header" onClick={() => toggleCategory("connections")}>
              <span className="wfb-header-title">
                {expandedCategories.connections ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
                <Link size={14} className="icon-connection" />
                Connections
              </span>
            </button>

            {expandedCategories.connections && (
              <div className="wfb-accordion-content">
                <div className="wfb-search-wrapper">
                  <Search size={12} className="wfb-search-icon" />
                  <input
                    type="text"
                    className="wfb-category-search"
                    placeholder="Search connections..."
                    value={connectionSearch}
                    onChange={(e) => setConnectionSearch(e.target.value)}
                  />
                </div>

                <div className="wfb-palette-list">
                  {filteredConnections.map((conn) => (
                    <div
                      key={`conn-${conn.id}`}
                      className="wfb-palette-item"
                      draggable
                      onDragStart={(e) => handleDragStart(e, "Connection", conn.name)}
                    >
                      <span className="wfb-step-badge wfb-badge-connection">{conn.provider}</span>
                      <span className="wfb-item-text">{conn.name}</span>
                    </div>
                  ))}
                  {filteredConnections.length === 0 && (
                    <div className="wfb-no-items">No connections found</div>
                  )}
                </div>
              </div>
            )}
          </div>

          {/* Category: Triggers */}
          <div className={`wfb-accordion-item ${expandedCategories.triggers ? "expanded" : ""}`}>
            <button className="wfb-accordion-header" onClick={() => toggleCategory("triggers")}>
              <span className="wfb-header-title">
                {expandedCategories.triggers ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
                <Zap size={14} className="icon-trigger" />
                Triggers
              </span>
            </button>

            {expandedCategories.triggers && (
              <div className="wfb-accordion-content">
                <div className="wfb-search-wrapper">
                  <Search size={12} className="wfb-search-icon" />
                  <input
                    type="text"
                    className="wfb-category-search"
                    placeholder="Search triggers..."
                    value={triggerSearch}
                    onChange={(e) => setTriggerSearch(e.target.value)}
                  />
                </div>

                <div className="wfb-palette-list">
                  {triggersList.map((trig, idx) => (
                    <div
                      key={`trigger-${idx}`}
                      className="wfb-palette-item"
                      draggable
                      onDragStart={(e) => handleDragStart(e, "Trigger", trig.connectionName, trig.provider, "", "{}", trig.action)}
                    >
                      <span className="wfb-step-badge wfb-badge-trigger">
                        {trig.provider === "system" ? "Core" : trig.provider}
                      </span>
                      <span className="wfb-item-text">{trig.name}</span>
                    </div>
                  ))}
                  {triggersList.length === 0 && (
                    <div className="wfb-no-items">No triggers found</div>
                  )}
                </div>
              </div>
            )}
          </div>

          {/* Category: Tendril Flows */}
          <div className={`wfb-accordion-item ${expandedCategories.flows ? "expanded" : ""}`}>
            <button className="wfb-accordion-header" onClick={() => toggleCategory("flows")}>
              <span className="wfb-header-title">
                {expandedCategories.flows ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
                <Layers size={14} className="icon-flow" />
                Tendril Flows
              </span>
            </button>

            {expandedCategories.flows && (
              <div className="wfb-accordion-content">
                <div className="wfb-search-wrapper">
                  <Search size={12} className="wfb-search-icon" />
                  <input
                    type="text"
                    className="wfb-category-search"
                    placeholder="Search flows..."
                    value={flowSearch}
                    onChange={(e) => setFlowSearch(e.target.value)}
                  />
                </div>

                <div className="wfb-flows-list">
                  {filteredSystemFlows.map((flow) => {
                    const isActive = loadedWorkflow && loadedWorkflow.id === flow.id;
                    return (
                      <div
                        key={`flow-${flow.id}`}
                        className={`wfb-sidebar-row ${isActive ? "active" : ""}`}
                        onClick={() => eventHandler("OnWorkflowSelect", id, [flow.id])}
                      >
                        <div className="wfb-row-info">
                          <span className="wfb-row-title">
                            <Lock size={12} className="wfb-lock-icon" />
                            {flow.name}
                          </span>
                          <span className="wfb-row-desc">{flow.description}</span>
                        </div>
                        <span className="wfb-row-badge system">Flow</span>
                      </div>
                    );
                  })}
                  {filteredSystemFlows.length === 0 && (
                    <div className="wfb-no-items">No system flows found</div>
                  )}
                </div>
              </div>
            )}
          </div>

          {/* Category: Templates */}
          <div className={`wfb-accordion-item ${expandedCategories.templates ? "expanded" : ""}`}>
            <button className="wfb-accordion-header" onClick={() => toggleCategory("templates")}>
              <span className="wfb-header-title">
                {expandedCategories.templates ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
                <Workflow size={14} className="icon-template" />
                Templates
              </span>
            </button>

            {expandedCategories.templates && (
              <div className="wfb-accordion-content">
                <div className="wfb-search-wrapper">
                  <Search size={12} className="wfb-search-icon" />
                  <input
                    type="text"
                    className="wfb-category-search"
                    placeholder="Search templates..."
                    value={templateSearch}
                    onChange={(e) => setTemplateSearch(e.target.value)}
                  />
                  <button className="wfb-add-inline-btn" title="Create New Template" onClick={() => eventHandler("OnCreateWorkflow", id, [])}>
                    <Plus size={14} />
                  </button>
                </div>

                <div className="wfb-flows-list">
                  {filteredUserTemplates.map((template) => {
                    const isActive = loadedWorkflow && loadedWorkflow.id === template.id;
                    return (
                      <div
                        key={`template-${template.id}`}
                        className={`wfb-sidebar-row ${isActive ? "active" : ""}`}
                        onClick={() => eventHandler("OnWorkflowSelect", id, [template.id])}
                      >
                        <div className="wfb-row-info">
                          <span className="wfb-row-title">{template.name}</span>
                          <span className="wfb-row-desc">{template.description}</span>
                        </div>
                        <span className={`wfb-row-badge ${template.isActive ? "active" : "inactive"}`}>
                          {template.isActive ? "Active" : "Inactive"}
                        </span>
                      </div>
                    );
                  })}
                  {filteredUserTemplates.length === 0 && (
                    <div className="wfb-no-items">No templates found</div>
                  )}
                </div>
              </div>
            )}
          </div>

        </div>
      </div>

      {/* Canvas Workspace */}
      <div
        ref={canvasRef}
        className="wfb-canvas-workspace"
        onMouseMove={handleMouseMove}
        onMouseUp={handleMouseUp}
        onDragOver={(e) => e.preventDefault()}
        onDrop={handleDrop}
      >
        {!loadedWorkflow ? (
          <div className="wfb-canvas-placeholder">
            <Workflow size={48} className="wfb-placeholder-icon" />
            <h3>No Workflow Selected</h3>
            <p>Select a workflow from Tendril Flows or Templates in the sidebar to start editing, or create a new template.</p>
          </div>
        ) : (
          <>
            {/* SVG connection lines */}
            <svg className="wfb-canvas-svg">
              <defs>
                <marker
                  id="arrow"
                  viewBox="0 0 10 10"
                  refX="6"
                  refY="5"
                  markerWidth="6"
                  markerHeight="6"
                  orient="auto-start-reverse"
                >
                  <path d="M 0 1 L 10 5 L 0 9 z" fill="var(--foreground)" opacity={0.6} />
                </marker>
              </defs>

              {steps.map((source) => {
                const outPort = getPortCoords(source).output;
                return source.next.map((targetId) => {
                  const target = steps.find((s) => s.id === targetId);
                  if (!target) return null;
                  const inPort = getPortCoords(target).input;

                  const midX = (outPort.x + inPort.x) / 2;
                  const midY = (outPort.y + inPort.y) / 2;

                  return (
                    <g key={`${source.id}-${targetId}`}>
                      <path
                        d={drawBezier(outPort.x, outPort.y, inPort.x, inPort.y)}
                        className="wfb-connection-path"
                        markerEnd="url(#arrow)"
                      />
                      {/* Delete button midpoint on edge */}
                      {!isReadOnly && (
                        <g
                          className="wfb-connection-del-btn"
                          onClick={() => removeConnection(source.id, targetId)}
                          style={{ transformOrigin: `${midX}px ${midY}px` }}
                        >
                          <circle cx={midX} cy={midY} r={8} />
                          <line
                            x1={midX - 3}
                            y1={midY - 3}
                            x2={midX + 3}
                            y2={midY + 3}
                            stroke="white"
                            strokeWidth={1.5}
                          />
                          <line
                            x1={midX + 3}
                            y1={midY - 3}
                            x2={midX - 3}
                            y2={midY + 3}
                            stroke="white"
                            strokeWidth={1.5}
                          />
                        </g>
                      )}
                    </g>
                  );
                });
              })}

              {/* Temp drag connection line */}
              {tempConnection && (
                <path
                  d={drawBezier(
                    tempConnection.fromX,
                    tempConnection.fromY,
                    tempConnection.toX,
                    tempConnection.toY
                  )}
                  className="wfb-connection-path"
                  style={{ stroke: "var(--primary)", strokeDasharray: "4 4" }}
                />
              )}
            </svg>

            {/* Step Nodes Cards */}
            {steps.map((step) => {
              const isTrigger = step.type.toLowerCase() === "trigger";
              const isConnection = step.type.toLowerCase() === "connection";
              const isPrompt = step.type.toLowerCase() === "prompt";
              const isWebhookTrigger = isTrigger && step.action === "webhook";

              const selectedConnObj = availableConnections.find(
                (c) => c.name === step.connectionName
              );
              const allowedActions = selectedConnObj
                ? selectedConnObj.permissions
                    .split(",")
                    .map((p) => p.trim())
                : [];

              return (
                <div
                  key={step.id}
                  ref={measureCardRef(step.id)}
                  className={`wfb-canvas-card type-${step.type.toLowerCase()} ${selectedNode === step.id ? "active" : ""} ${isWebhookTrigger && !step.connectionName ? "webhook-generic" : ""}`}
                  style={{ left: `${step.x}px`, top: `${step.y}px` }}
                  onClick={() => handleCardClick(step.id)}
                >
                  {/* Drag Handle Header */}
                  <div
                    className="wfb-card-drag-handle"
                    onMouseDown={(e) => handleCardMouseDown(e, step.id)}
                  >
                    <div className="wfb-card-title">
                      <span
                        className={`wfb-step-badge wfb-badge-${step.type.toLowerCase()}`}
                      >
                        {step.type}
                      </span>
                      <input
                        type="text"
                        className="wfb-card-input-name"
                        value={step.name}
                        disabled={isReadOnly}
                        onChange={(e) =>
                          updateStep(step.id, {
                            name: e.target.value.replace(/\s+/g, "_"),
                          })
                        }
                        placeholder="Step Name"
                      />
                    </div>
                    {(!isTrigger || steps.filter(s => s.type.toLowerCase() === "trigger").length > 1) && !isReadOnly && (
                      <button
                        className="wfb-btn"
                        style={{
                          padding: "4px",
                          border: "none",
                          background: "transparent",
                        }}
                        onClick={() => removeStep(step.id)}
                      >
                        <Trash2 size={14} className="text-destructive" />
                      </button>
                    )}
                  </div>

                  {/* Form Input fields */}
                  <div className="wfb-card-body">
                    {isTrigger && (
                      <div
                        style={{
                          fontSize: "0.75rem",
                          color: "var(--muted-foreground)",
                          display: "flex",
                          flexDirection: "column",
                          gap: "6px"
                        }}
                      >
                        <div className="wfb-field-group">
                          <label className="wfb-label">Trigger Type</label>
                          <select
                            className="wfb-select"
                            value={step.action || "manual"}
                            disabled={isReadOnly}
                            onChange={(e) =>
                              updateStep(step.id, {
                                action: e.target.value,
                                args: e.target.value === "schedule" ? "*/5 * * * *" : e.target.value === "event" ? "plan_completed_and_merged" : "{}"
                              })
                            }
                          >
                            <option value="manual">Manual Trigger</option>
                            <option value="webhook">Webhook Trigger</option>
                            <option value="schedule">Timed Trigger (Cron)</option>
                            <option value="event">Tendril Event Trigger</option>
                          </select>
                        </div>

                        {step.action === "webhook" && (
                          <>
                            <div style={{ fontWeight: 600, color: "var(--foreground)" }}>
                              Webhook Trigger
                            </div>
                            <div>
                              Starts workflow via incoming HTTP POST.
                            </div>
                            {step.connectionName ? (
                              <div style={{ fontSize: "0.7rem", color: "var(--primary)" }}>
                                Connection: {step.connectionName}
                              </div>
                            ) : (
                              <div className="wfb-field-group" style={{ marginTop: "4px" }}>
                                <span style={{ fontSize: "0.65rem", fontWeight: 700, textTransform: "uppercase" }}>Endpoint</span>
                                <input
                                  type="text"
                                  className="wfb-input"
                                  readOnly
                                  style={{ fontFamily: "monospace", fontSize: "0.7rem", padding: "2px 4px" }}
                                  value={`${window.location.origin}/api/jobs`}
                                  onClick={(e) => (e.target as HTMLInputElement).select()}
                                />
                                <span style={{ fontSize: "0.65rem", fontWeight: 700, textTransform: "uppercase", marginTop: "2px" }}>Payload $type</span>
                                <input
                                  type="text"
                                  className="wfb-input"
                                  readOnly
                                  style={{ fontFamily: "monospace", fontSize: "0.7rem", padding: "2px 4px" }}
                                  value="WorkflowRun"
                                  onClick={(e) => (e.target as HTMLInputElement).select()}
                                />
                              </div>
                            )}
                          </>
                        )}

                        {step.action === "schedule" && (
                          <div className="wfb-field-group">
                            <label className="wfb-label">Cron Expression</label>
                            <input
                              type="text"
                              className="wfb-input"
                              value={step.args || "*/5 * * * *"}
                              disabled={isReadOnly}
                              onChange={(e) =>
                                updateStep(step.id, { args: e.target.value })
                              }
                              placeholder="e.g. */5 * * * *"
                            />
                            <div style={{ fontSize: "0.65rem", marginTop: "2px", color: "var(--muted-foreground)" }}>
                              Format: min hour day month day-of-week (e.g. 0 0 * * * for daily)
                            </div>
                          </div>
                        )}

                        {step.action === "event" && (
                          <div className="wfb-field-group">
                            <label className="wfb-label">Event Type</label>
                            <select
                              className="wfb-select"
                              value={step.args || "plan_completed_and_merged"}
                              disabled={isReadOnly}
                              onChange={(e) =>
                                updateStep(step.id, { args: e.target.value })
                              }
                            >
                              <option value="plan_completed_and_merged">Plan Completed & Merged</option>
                              <option value="plan_created">Plan Created</option>
                              <option value="plan_transitioned">Plan Transitioned</option>
                            </select>
                          </div>
                        )}

                        {(!step.action || step.action === "manual") && (
                          <div>
                            Manually run this workflow. Trigger payload can be injected in child step prompts.
                          </div>
                        )}
                      </div>
                    )}

                    {isConnection && (
                      <>
                        <div className="wfb-field-group">
                          <label className="wfb-label">Connection</label>
                          <select
                            className="wfb-select"
                            value={step.connectionName}
                            disabled={isReadOnly}
                            onChange={(e) =>
                              updateStep(step.id, {
                                connectionName: e.target.value,
                                action: "",
                              })
                            }
                          >
                            <option value="">-- Select Connection --</option>
                            {availableConnections.map((conn) => (
                              <option key={conn.id} value={conn.name}>
                                {conn.name} ({conn.provider})
                              </option>
                            ))}
                          </select>
                        </div>

                        <div className="wfb-field-group">
                          <label className="wfb-label">Action</label>
                          {allowedActions.includes("*") ||
                          allowedActions.length === 0 ? (
                            <input
                              type="text"
                              className="wfb-input"
                              value={step.action}
                              disabled={isReadOnly}
                              onChange={(e) =>
                                updateStep(step.id, { action: e.target.value })
                              }
                              placeholder="SendMessage"
                            />
                          ) : (
                            <select
                              className="wfb-select"
                              value={step.action}
                              disabled={isReadOnly}
                              onChange={(e) =>
                                updateStep(step.id, { action: e.target.value })
                              }
                            >
                              <option value="">-- Select Action --</option>
                              {allowedActions.map((act) => (
                                <option key={act} value={act}>
                                  {act}
                                </option>
                              ))}
                            </select>
                          )}
                        </div>

                        <div className="wfb-field-group">
                          <label className="wfb-label">Payload arguments</label>
                          <textarea
                            className="wfb-textarea"
                            value={step.args}
                            disabled={isReadOnly}
                            onChange={(e) =>
                              updateStep(step.id, { args: e.target.value })
                            }
                            placeholder='{"channel": "#general", "text": "Hello!"}'
                          />
                        </div>
                      </>
                    )}

                    {isPrompt && (
                      <>
                        <div className="wfb-field-group">
                          <label className="wfb-label">Agent Provider</label>
                          <select
                            className="wfb-select"
                            value={step.provider}
                            disabled={isReadOnly}
                            onChange={(e) =>
                              updateStep(step.id, { provider: e.target.value })
                            }
                          >
                            {availableProviders.map((prov) => (
                              <option key={prov} value={prov}>
                                {prov}
                              </option>
                            ))}
                          </select>
                        </div>

                        <div className="wfb-field-group">
                          <label className="wfb-label">Prompt Template</label>
                          <textarea
                            className="wfb-textarea"
                            value={step.args}
                            disabled={isReadOnly}
                            style={{ minHeight: "80px" }}
                            onChange={(e) =>
                              updateStep(step.id, { args: e.target.value })
                            }
                            placeholder="Analyze changes: {{steps.Start.output}}"
                          />
                        </div>
                      </>
                    )}
                  </div>

                  {/* Edge midpoint Connection Ports */}
                  {!isReadOnly && (
                    <>
                      {!isTrigger && (
                        <div
                          className="wfb-port wfb-port-input"
                          onMouseUp={(e) => handlePortMouseUp(e, step)}
                        />
                      )}
                      <div
                        className="wfb-port wfb-port-output"
                        onMouseDown={(e) => handlePortMouseDown(e, step)}
                      />
                    </>
                  )}

                  {/* Spawn step (+) element button */}
                  {!isReadOnly && (
                    <button
                      className="wfb-quick-add"
                      onClick={() =>
                        setSpawnMenuId(spawnMenuId === step.id ? null : step.id)
                      }
                    >
                      <Plus size={12} />
                    </button>
                  )}

                  {/* Floating Spawn Menu */}
                  {spawnMenuId === step.id && !isReadOnly && (
                    <div className="wfb-spawn-menu" style={{ right: "-180px", top: "20%" }}>
                      <button
                        className="wfb-spawn-option"
                        onClick={() => spawnStep(step.id, "Prompt")}
                      >
                        + Connect Prompt Step
                      </button>
                      {availableConnections.map((conn) => (
                        <button
                          key={conn.id}
                          className="wfb-spawn-option"
                          onClick={() => spawnStep(step.id, "Connection", conn.name)}
                        >
                          + Connect {conn.name} ({conn.provider})
                        </button>
                      ))}
                    </div>
                  )}
                </div>
              );
            })}
          </>
        )}
      </div>
    </div>
  );
};
