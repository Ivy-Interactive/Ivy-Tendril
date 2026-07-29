import React, { useState, useEffect, useRef } from "react";
import { 
  Trash2, 
  Search, 
  ChevronDown, 
  Zap, 
  Workflow, 
  Terminal, 
  Plug,
  Play,
  Share2,
  Folder,
  MessageSquare,
  FileText,
  Video,
  CheckSquare,
  Users,
  Grid
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

interface WorkflowBuilderProps {
  id: string;
  workflowDefinitionJson?: string;
  availableConnections?: ConnectionItem[];
  availableProviders?: string[];
  isReadOnly?: boolean;
  selectedNodeId?: string;
  selectedWorkflowId?: number;
  
  workflows?: WorkflowItem[];
  systemPromptwares?: string[];
  projects?: string[];
  selectedProject?: string;
  
  eventHandler: IvyEventHandler;
}

export const WorkflowBuilder: React.FC<WorkflowBuilderProps> = ({
  id,
  workflowDefinitionJson = "",
  availableConnections: _availableConnections = [],
  availableProviders: _availableProviders = ["Claude Code", "CodeQuality", "CodeSecurity", "Custom"],
  isReadOnly = false,
  selectedNodeId = "",
  selectedWorkflowId = 0,
  
  workflows = [],
  projects = ["Acme Corp.", "Ivy-Tendril", "default"],
  selectedProject = "Acme Corp.",
  
  eventHandler,
}) => {
  const [steps, setSteps] = useState<WorkflowStep[]>([]);
  const [isDragging, setIsDragging] = useState(false);
  const [draggedStepId, setDraggedStepId] = useState<string | null>(null);
  const [dragOffset, setDragOffset] = useState({ x: 0, y: 0 });
  const [selectedNode, setSelectedNode] = useState<string | null>(selectedNodeId || null);

  // Floating Island & Overlay state
  const [showOverlay, setShowOverlay] = useState<"connections" | "nodes" | "triggers" | null>(null);
  const [overlaySearch, setOverlaySearch] = useState("");

  const [currentProject, setCurrentProject] = useState(selectedProject || "Acme Corp.");
  const [currentWorkflowId, setCurrentWorkflowId] = useState(selectedWorkflowId || (workflows[0]?.id ?? 0));

  const canvasRef = useRef<HTMLDivElement>(null);

  // Parse incoming definition JSON
  useEffect(() => {
    try {
      if (workflowDefinitionJson) {
        const parsed = JSON.parse(workflowDefinitionJson) as WorkflowDefinition;
        if (parsed && Array.isArray(parsed.steps)) {
          const normalized = parsed.steps.map((s, idx) => ({
            ...s,
            x: s.x ?? (idx * 340 + 60),
            y: s.y ?? (idx % 2 === 0 ? 180 : 220),
            provider: s.provider || "Claude Code",
            model: s.model || "Fable 5 High",
          }));
          setSteps(normalized);
          return;
        }
      }
    } catch (e) {
      console.error("Failed to parse workflow definition JSON:", e);
    }

    // Default seed steps matching Figma preview
    setSteps([
      {
        id: "start",
        name: "+ New Plan",
        type: "Trigger",
        connectionName: "",
        action: "manual",
        args: "{}",
        next: ["draft"],
        x: 60,
        y: 200,
      },
      {
        id: "security",
        name: "Security Agent",
        type: "Prompt",
        connectionName: "",
        action: "",
        args: "Review every plan that gets drafted for security risks.",
        provider: "Claude Code",
        model: "Fable 5 High",
        next: [],
        x: 60,
        y: 380,
      },
      {
        id: "draft",
        name: "Draft",
        type: "Prompt",
        connectionName: "",
        action: "",
        args: "",
        provider: "Claude Code",
        model: "Fable 5 High",
        next: ["review"],
        x: 440,
        y: 200,
      },
      {
        id: "review",
        name: "Review",
        type: "Prompt",
        connectionName: "",
        action: "",
        args: "",
        provider: "Claude Code",
        model: "Opus 5 High",
        next: ["pr", "slack"],
        x: 820,
        y: 200,
      },
      {
        id: "pr",
        name: "Pull Request",
        type: "Prompt",
        connectionName: "",
        action: "",
        args: "",
        provider: "Claude Code",
        model: "Fable 5 High",
        next: [],
        x: 1200,
        y: 180,
      },
      {
        id: "slack",
        name: "Slack",
        type: "Connection",
        connectionName: "Slack",
        action: "SendMessage",
        args: "Post a message in #ivy-tendril every time an implementation is approved. Link the plan in the message and also include how many plans are in progress.",
        next: [],
        x: 1200,
        y: 380,
      },
    ]);
  }, [workflowDefinitionJson]);

  // Sync back steps
  useEffect(() => {
    if (steps.length > 0) {
      const definition: WorkflowDefinition = { steps };
      const jsonStr = JSON.stringify(definition, null, 2);
      eventHandler("OnSave", id, [jsonStr]);
    }
  }, [steps]);

  // Dragging steps on canvas
  const handleMouseDownStep = (e: React.MouseEvent, step: WorkflowStep) => {
    if (isReadOnly) return;
    e.stopPropagation();
    setSelectedNode(step.id);
    setDraggedStepId(step.id);
    setIsDragging(true);

    const canvasRect = canvasRef.current?.getBoundingClientRect() ?? { left: 0, top: 0 };
    setDragOffset({
      x: e.clientX - canvasRect.left - step.x,
      y: e.clientY - canvasRect.top - step.y,
    });
  };

  const handleMouseMoveCanvas = (e: React.MouseEvent) => {
    if (!isDragging || !draggedStepId || isReadOnly) return;
    const canvasRect = canvasRef.current?.getBoundingClientRect() ?? { left: 0, top: 0 };
    const newX = Math.max(20, e.clientX - canvasRect.left - dragOffset.x);
    const newY = Math.max(20, e.clientY - canvasRect.top - dragOffset.y);

    setSteps((prev) =>
      prev.map((s) => (s.id === draggedStepId ? { ...s, x: newX, y: newY } : s))
    );
  };

  const handleMouseUpCanvas = () => {
    setIsDragging(false);
    setDraggedStepId(null);
  };

  const addNodeToCanvas = (type: string, name: string, description: string = "") => {
    const newId = `node_${Date.now()}`;
    const newStep: WorkflowStep = {
      id: newId,
      name,
      type,
      connectionName: type === "Connection" ? name : "",
      action: type === "Connection" ? "SendMessage" : "",
      args: description,
      provider: "Claude Code",
      model: "Fable 5 High",
      next: [],
      x: 350 + Math.random() * 200,
      y: 250 + Math.random() * 150,
    };

    setSteps((prev) => [...prev, newStep]);
    setSelectedNode(newId);
    setShowOverlay(null);
  };

  const deleteStep = (stepId: string) => {
    setSteps((prev) =>
      prev
        .filter((s) => s.id !== stepId)
        .map((s) => ({
          ...s,
          next: s.next.filter((n) => n !== stepId),
        }))
    );
    if (selectedNode === stepId) setSelectedNode(null);
  };

  const updateStep = (stepId: string, updates: Partial<WorkflowStep>) => {
    setSteps((prev) => prev.map((s) => (s.id === stepId ? { ...s, ...updates } : s)));
  };

  // Preset integrations for search overlay
  const presetIntegrations = [
    { name: "Slack", description: "Send and receive messages in your Slack channels.", icon: <MessageSquare size={16} className="text-orange-500" /> },
    { name: "LinkedIn", description: "Easily download and share your LinkedIn updates.", icon: <Users size={16} className="text-blue-500" /> },
    { name: "Trello", description: "Manage tasks and collaborate with your team.", icon: <CheckSquare size={16} className="text-blue-400" /> },
    { name: "Google Drive", description: "Securely store and share files in the cloud.", icon: <Folder size={16} className="text-green-500" /> },
    { name: "Asana", description: "Manage team projects and workflows.", icon: <Grid size={16} className="text-red-400" /> },
    { name: "Jira", description: "Organize tasks and projects with boards, and lists.", icon: <FileText size={16} className="text-blue-600" /> },
    { name: "Teams", description: "Facilitate video conferencing for remote meetings.", icon: <Users size={16} className="text-indigo-500" /> },
    { name: "Zoom", description: "Video conferencing for remote team meetings.", icon: <Video size={16} className="text-blue-500" /> },
  ];

  const filteredIntegrations = presetIntegrations.filter((item) =>
    item.name.toLowerCase().includes(overlaySearch.toLowerCase()) ||
    item.description.toLowerCase().includes(overlaySearch.toLowerCase())
  );

  return (
    <div className="wfb-root-canvas-container">
      {/* --- TOP BAR HEADER (FIXED PINNED AT TOP) --- */}
      <div className="wfb-top-bar">
        <div className="wfb-top-left">
          <div className="wfb-project-selector">
            <Folder size={15} className="wfb-icon-muted" />
            <select
              className="wfb-header-select"
              value={currentProject}
              onChange={(e) => {
                setCurrentProject(e.target.value);
                eventHandler("OnProjectSelect", id, [e.target.value]);
              }}
            >
              {projects.map((p) => (
                <option key={p} value={p}>
                  {p}
                </option>
              ))}
            </select>
            <ChevronDown size={14} className="wfb-icon-muted" />
          </div>

          <div className="wfb-workflow-selector">
            <Workflow size={15} className="wfb-icon-muted" />
            <select
              className="wfb-header-select"
              value={currentWorkflowId}
              onChange={(e) => {
                const wid = parseInt(e.target.value);
                setCurrentWorkflowId(wid);
                eventHandler("OnWorkflowSelect", id, [wid]);
              }}
            >
              {workflows.length > 0 ? (
                workflows.map((w) => (
                  <option key={w.id} value={w.id}>
                    {w.name}
                  </option>
                ))
              ) : (
                <option value={0}>Tendril Core Lifecycle</option>
              )}
            </select>
            <ChevronDown size={14} className="wfb-icon-muted" />
          </div>
        </div>

        <div className="wfb-top-right">
          <button
            className="wfb-btn-outline"
            onClick={() => eventHandler("OnShare", id, [])}
          >
            <Share2 size={14} />
            Share
          </button>

          <button
            className="wfb-btn-primary"
            onClick={() => eventHandler("OnTestWorkflow", id, [currentWorkflowId])}
          >
            <Play size={14} fill="currentColor" />
            Test Workflow
          </button>
        </div>
      </div>

      {/* --- CANVAS WORKSPACE (SCROLLABLE & PANNEABLE AREA) --- */}
      <div
        className="wfb-canvas-workspace"
        ref={canvasRef}
        onMouseMove={handleMouseMoveCanvas}
        onMouseUp={handleMouseUpCanvas}
      >
        <svg className="wfb-canvas-svg">
          <defs>
            <marker
              id="arrowhead"
              markerWidth="8"
              markerHeight="6"
              refX="7"
              refY="3"
              orient="auto"
            >
              <polygon points="0 0, 8 3, 0 6" fill="rgba(150, 150, 150, 0.5)" />
            </marker>
          </defs>

          {/* Draw SVG Edge Connections with Accurate Coordinates */}
          {steps.map((step) =>
            step.next.map((targetId) => {
              const target = steps.find((s) => s.id === targetId);
              if (!target) return null;

              const isSourceTrigger = step.type.toLowerCase() === "trigger";
              const sourceWidth = isSourceTrigger ? 140 : 260;
              const sourceYOffset = isSourceTrigger ? 20 : 24;

              const isTargetTrigger = target.type.toLowerCase() === "trigger";
              const targetYOffset = isTargetTrigger ? 20 : 24;

              const sourceX = step.x + sourceWidth;
              const sourceY = step.y + sourceYOffset;
              const targetX = target.x;
              const targetY = target.y + targetYOffset;

              const dx = Math.max(40, targetX - sourceX);
              const controlOffset = Math.min(160, Math.max(40, dx * 0.45));

              const pathData = `M ${sourceX} ${sourceY} C ${sourceX + controlOffset} ${sourceY}, ${targetX - controlOffset} ${targetY}, ${targetX} ${targetY}`;

              const midX = (sourceX + targetX) / 2;
              const midY = (sourceY + targetY) / 2;

              const stepCount = Math.abs(step.id.length * 3 + target.id.length * 2) % 15 + 4;

              return (
                <g key={`${step.id}-${targetId}`}>
                  <path
                    d={pathData}
                    className="wfb-connection-path"
                    markerEnd="url(#arrowhead)"
                  />
                  <g transform={`translate(${midX - 16}, ${midY - 12})`}>
                    <rect
                      x="0"
                      y="0"
                      width="34"
                      height="22"
                      rx="11"
                      className="wfb-edge-badge-bg"
                    />
                    <text
                      x="17"
                      y="14"
                      textAnchor="middle"
                      className="wfb-edge-badge-text"
                    >
                      ⚙ {stepCount}
                    </text>
                  </g>
                </g>
              );
            })
          )}
        </svg>

        {/* Draw Canvas Nodes */}
        {steps.map((step) => {
          const isSelected = selectedNode === step.id;
          const isTrigger = step.type.toLowerCase() === "trigger";
          const isConnection = step.type.toLowerCase() === "connection";
          const isPrompt = step.type.toLowerCase() === "prompt";

          return (
            <div
              key={step.id}
              className={`wfb-canvas-node ${isSelected ? "selected" : ""} ${
                isTrigger ? "node-trigger-pill" : ""
              }`}
              style={{ left: `${step.x}px`, top: `${step.y}px` }}
              onMouseDown={(e) => handleMouseDownStep(e, step)}
            >
              <div className="wfb-node-header">
                <div className="wfb-node-title">
                  {isTrigger && <Zap size={14} className="text-amber-500" />}
                  {isPrompt && <Terminal size={14} className="text-cyan-500" />}
                  {isConnection && <Plug size={14} className="text-orange-500" />}
                  <span>{step.name}</span>
                </div>

                <div className="wfb-node-header-actions">
                  <button
                    className="wfb-node-opt-btn"
                    onClick={(e) => {
                      e.stopPropagation();
                      deleteStep(step.id);
                    }}
                  >
                    <Trash2 size={13} />
                  </button>
                </div>
              </div>

              {!isTrigger && (
                <div className="wfb-node-body">
                  {isPrompt && (
                    <>
                      <div className="wfb-node-select-row">
                        <span className="wfb-select-icon">✴</span>
                        <select
                          className="wfb-node-dropdown"
                          value={step.provider || "Claude Code"}
                          onChange={(e) => updateStep(step.id, { provider: e.target.value })}
                        >
                          <option value="Claude Code">Claude Code</option>
                          <option value="CodeQuality">CodeQuality</option>
                          <option value="CodeSecurity">CodeSecurity</option>
                        </select>
                      </div>

                      <div className="wfb-node-select-row">
                        <span className="wfb-select-icon">⚙</span>
                        <select
                          className="wfb-node-dropdown"
                          value={step.model || "Fable 5 High"}
                          onChange={(e) => updateStep(step.id, { model: e.target.value })}
                        >
                          <option value="Fable 5 High">Fable 5 High</option>
                          <option value="Opus 5 High">Opus 5 High</option>
                          <option value="Sonnet 3.5">Sonnet 3.5</option>
                        </select>
                      </div>
                    </>
                  )}

                  {step.args && (
                    <div className="wfb-node-description">
                      {step.args}
                    </div>
                  )}

                  <div className="wfb-node-badges">
                    {isPrompt && <span className="wfb-badge badge-promptware">⚙ Promptware</span>}
                    {isTrigger && <span className="wfb-badge badge-trigger">▷ Trigger</span>}
                    {isConnection && <span className="wfb-badge badge-connection">🔌 Connection</span>}
                  </div>
                </div>
              )}
            </div>
          );
        })}
      </div>

      {/* --- FLOATING SEARCH OVERLAY POPUP (FIXED ON SCREEN) --- */}
      {showOverlay && (
        <div className="wfb-overlay-popup">
          <div className="wfb-overlay-search-bar">
            <Search size={15} className="text-muted-foreground" />
            <input
              type="text"
              className="wfb-overlay-input"
              placeholder="Search Connections..."
              value={overlaySearch}
              onChange={(e) => setOverlaySearch(e.target.value)}
              autoFocus
            />
          </div>

          <div className="wfb-overlay-list">
            {filteredIntegrations.map((item) => (
              <div
                key={item.name}
                className="wfb-overlay-item"
                onClick={() => addNodeToCanvas("Connection", item.name, item.description)}
              >
                <div className="wfb-overlay-item-icon">{item.icon}</div>
                <div className="wfb-overlay-item-info">
                  <div className="wfb-overlay-item-name">{item.name}</div>
                  <div className="wfb-overlay-item-desc">{item.description}</div>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* --- BOTTOM FLOATING ISLAND TOOLBAR (FIXED ON SCREEN) --- */}
      <div className="wfb-floating-island">
        <button
          className={`wfb-island-btn ${showOverlay === "connections" ? "active" : ""}`}
          title="Search Connections"
          onClick={() => setShowOverlay(showOverlay === "connections" ? null : "connections")}
        >
          <Plug size={16} />
        </button>

        <button
          className={`wfb-island-btn ${showOverlay === "nodes" ? "active" : ""}`}
          title="Add Promptware Node"
          onClick={() => addNodeToCanvas("Prompt", "New Step", "Custom prompt step")}
        >
          <Terminal size={16} />
        </button>

        <button
          className={`wfb-island-btn ${showOverlay === "triggers" ? "active" : ""}`}
          title="Add Trigger Node"
          onClick={() => addNodeToCanvas("Trigger", "+ Trigger", "")}
        >
          <Zap size={16} />
        </button>

        <div className="wfb-island-divider" />

        <button
          className="wfb-island-btn primary"
          title="Run Test"
          onClick={() => eventHandler("OnTestWorkflow", id, [currentWorkflowId])}
        >
          <Play size={15} fill="currentColor" />
        </button>
      </div>
    </div>
  );
};
