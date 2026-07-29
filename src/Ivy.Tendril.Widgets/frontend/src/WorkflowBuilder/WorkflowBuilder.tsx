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
  Grid,
  Plus,
  Layers
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
  connectionName: string; // Used for Promptware Name
  action: string;
  args: string;
  provider?: string;
  model?: string; // Used for Agent-CLI / Model
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
  const stepsRef = useRef(steps);
  stepsRef.current = steps;

  const [selectedNode, setSelectedNode] = useState<string | null>(selectedNodeId || null);

  // Dragging connection line state
  const [connectingFromId, setConnectingFromId] = useState<string | null>(null);
  const [mouseCanvasPos, setMouseCanvasPos] = useState<{ x: number; y: number } | null>(null);

  // Floating Island & Overlay state
  const [showOverlay, setShowOverlay] = useState<"connections" | "nodes" | "triggers" | null>(null);
  const [overlaySearch, setOverlaySearch] = useState("");

  const [currentProject, setCurrentProject] = useState(selectedProject || "Acme Corp.");
  const [currentWorkflowId, setCurrentWorkflowId] = useState(selectedWorkflowId || (workflows[0]?.id ?? 0));

  const canvasRef = useRef<HTMLDivElement>(null);

  // Math aligned node port coordinates (280px wide card, port y at 24px center of header)
  const getNodePortCoords = (step: WorkflowStep) => {
    const isTrigger = step.type.toLowerCase() === "trigger";
    const width = isTrigger ? 150 : 280;
    const heightOffset = isTrigger ? 22 : 24;

    return {
      leftX: step.x,
      rightX: step.x + width,
      centerY: step.y + heightOffset,
    };
  };

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
            connectionName: s.connectionName || "CodeQuality",
            provider: s.provider || "Claude Code",
            model: s.model || "Claude Code (Fable 5 High)",
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
        connectionName: "CodeSecurity",
        action: "",
        args: "Review every plan that gets drafted for security risks.",
        provider: "Claude Code",
        model: "Claude Code (Fable 5 High)",
        next: [],
        x: 60,
        y: 380,
      },
      {
        id: "draft",
        name: "Draft",
        type: "Prompt",
        connectionName: "PlanDrafter",
        action: "",
        args: "",
        provider: "Claude Code",
        model: "Claude Code (Fable 5 High)",
        next: ["review"],
        x: 440,
        y: 200,
      },
      {
        id: "review",
        name: "Review",
        type: "Prompt",
        connectionName: "PlanReviewer",
        action: "",
        args: "",
        provider: "Claude Code",
        model: "Claude Code (Opus 5 High)",
        next: ["pr", "slack"],
        x: 820,
        y: 200,
      },
      {
        id: "pr",
        name: "Pull Request",
        type: "Prompt",
        connectionName: "PRCreator",
        action: "",
        args: "",
        provider: "Claude Code",
        model: "Claude Code (Fable 5 High)",
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

  // Reliable card dragging using ref + e.preventDefault()
  const startDragNode = (e: React.MouseEvent, stepId: string) => {
    if (isReadOnly) return;

    const targetEl = e.target as HTMLElement;
    // Don't start drag if user clicked inside select, option, button, or port
    if (
      targetEl.closest("select") ||
      targetEl.closest("input") ||
      targetEl.closest("button") ||
      targetEl.closest(".wfb-port")
    ) {
      return;
    }

    e.preventDefault();
    e.stopPropagation();
    setSelectedNode(stepId);

    const targetStep = stepsRef.current.find((s) => s.id === stepId);
    if (!targetStep) return;

    const startX = e.clientX;
    const startY = e.clientY;
    const initialX = targetStep.x;
    const initialY = targetStep.y;

    const onMouseMove = (moveEvent: MouseEvent) => {
      moveEvent.preventDefault();
      const dx = moveEvent.clientX - startX;
      const dy = moveEvent.clientY - startY;
      const newX = Math.max(10, initialX + dx);
      const newY = Math.max(10, initialY + dy);

      setSteps((prev) =>
        prev.map((s) => (s.id === stepId ? { ...s, x: newX, y: newY } : s))
      );
    };

    const onMouseUp = () => {
      window.removeEventListener("mousemove", onMouseMove);
      window.removeEventListener("mouseup", onMouseUp);
    };

    window.addEventListener("mousemove", onMouseMove);
    window.addEventListener("mouseup", onMouseUp);
  };

  // Dragging out a new arrow connection from a port dot
  const startConnecting = (e: React.MouseEvent, sourceStepId: string) => {
    e.stopPropagation();
    e.preventDefault();
    setConnectingFromId(sourceStepId);

    const updateMousePos = (clientX: number, clientY: number) => {
      const cRect = canvasRef.current?.getBoundingClientRect() ?? { left: 0, top: 0 };
      const sLeft = canvasRef.current?.scrollLeft ?? 0;
      const sTop = canvasRef.current?.scrollTop ?? 0;

      setMouseCanvasPos({
        x: clientX - cRect.left + sLeft,
        y: clientY - cRect.top + sTop,
      });
    };

    updateMousePos(e.clientX, e.clientY);

    const onMouseMove = (moveEvent: MouseEvent) => {
      updateMousePos(moveEvent.clientX, moveEvent.clientY);
    };

    const onMouseUp = (upEvent: MouseEvent) => {
      window.removeEventListener("mousemove", onMouseMove);
      window.removeEventListener("mouseup", onMouseUp);

      const targetElement = document.elementFromPoint(upEvent.clientX, upEvent.clientY);
      const targetNodeEl = targetElement?.closest("[data-node-id]");
      if (targetNodeEl) {
        const targetId = targetNodeEl.getAttribute("data-node-id");
        if (targetId && targetId !== sourceStepId) {
          setSteps((prev) =>
            prev.map((s) => {
              if (s.id === sourceStepId && !s.next.includes(targetId)) {
                return { ...s, next: [...s.next, targetId] };
              }
              return s;
            })
          );
        }
      }

      setConnectingFromId(null);
      setMouseCanvasPos(null);
    };

    window.addEventListener("mousemove", onMouseMove);
    window.addEventListener("mouseup", onMouseUp);
  };

  const addNodeToCanvas = (type: string, name: string, description: string = "") => {
    const newId = `node_${Date.now()}`;
    const newStep: WorkflowStep = {
      id: newId,
      name,
      type,
      connectionName: type === "Connection" ? name : "CodeQuality",
      action: type === "Connection" ? "SendMessage" : "",
      args: description,
      provider: "Claude Code",
      model: "Claude Code (Fable 5 High)",
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

      {/* --- CANVAS WORKSPACE (SCROLLABLE AREA) --- */}
      <div
        className="wfb-canvas-workspace"
        ref={canvasRef}
      >
        <svg className="wfb-canvas-svg">
          <defs>
            <marker
              id="arrowhead"
              markerWidth="7"
              markerHeight="5"
              refX="6"
              refY="2.5"
              orient="auto"
            >
              <polygon points="0 0, 7 2.5, 0 5" fill="rgba(200, 200, 200, 0.6)" />
            </marker>
          </defs>

          {/* Draw SVG Edge Connections with Sleek 2px Stroke */}
          {steps.map((step) =>
            step.next.map((targetId) => {
              const target = steps.find((s) => s.id === targetId);
              if (!target) return null;

              const sourcePort = getNodePortCoords(step);
              const targetPort = getNodePortCoords(target);

              const sourceX = sourcePort.rightX;
              const sourceY = sourcePort.centerY;
              const targetX = targetPort.leftX;
              const targetY = targetPort.centerY;

              const dx = Math.max(30, targetX - sourceX);
              const controlOffset = Math.min(160, Math.max(40, dx * 0.45));

              const pathData = `M ${sourceX} ${sourceY} C ${sourceX + controlOffset} ${sourceY}, ${targetX - controlOffset} ${targetY}, ${targetX} ${targetY}`;

              // Exact mid-point on cubic bezier curve at t = 0.5
              const t = 0.5;
              const p0x = sourceX, p0y = sourceY;
              const p1x = sourceX + controlOffset, p1y = sourceY;
              const p2x = targetX - controlOffset, p2y = targetY;
              const p3x = targetX, p3y = targetY;

              const midX = (1-t)*(1-t)*(1-t)*p0x + 3*(1-t)*(1-t)*t*p1x + 3*(1-t)*t*t*p2x + t*t*t*p3x;
              const midY = (1-t)*(1-t)*(1-t)*p0y + 3*(1-t)*(1-t)*t*p1y + 3*(1-t)*t*t*p2y + t*t*t*p3y;

              const jobCount = Math.abs(step.id.length * 3 + target.id.length * 2) % 15 + 4;

              return (
                <g key={`${step.id}-${targetId}`}>
                  <path
                    d={pathData}
                    className="wfb-connection-path"
                    markerEnd="url(#arrowhead)"
                  />
                  {/* Livestream Job Badge */}
                  <g transform={`translate(${midX - 22}, ${midY - 12})`}>
                    <rect
                      x="0"
                      y="0"
                      width="44"
                      height="24"
                      rx="12"
                      className="wfb-edge-badge-bg"
                    />
                    <circle cx="11" cy="12" r="3" className="wfb-job-pulse-dot" />
                    <text
                      x="27"
                      y="13"
                      textAnchor="middle"
                      className="wfb-edge-badge-text"
                    >
                      {jobCount}
                    </text>
                  </g>
                </g>
              );
            })
          )}

          {/* Live Preview Connecting Line when Dragging New Arrow */}
          {connectingFromId && mouseCanvasPos && (() => {
            const sourceStep = steps.find((s) => s.id === connectingFromId);
            if (!sourceStep) return null;
            const sourcePort = getNodePortCoords(sourceStep);
            const sourceX = sourcePort.rightX;
            const sourceY = sourcePort.centerY;
            const targetX = mouseCanvasPos.x;
            const targetY = mouseCanvasPos.y;

            const dx = Math.max(30, targetX - sourceX);
            const controlOffset = Math.min(160, Math.max(40, dx * 0.45));
            const pathData = `M ${sourceX} ${sourceY} C ${sourceX + controlOffset} ${sourceY}, ${targetX - controlOffset} ${targetY}, ${targetX} ${targetY}`;

            return (
              <path
                d={pathData}
                className="wfb-connection-path wfb-connecting-preview"
                markerEnd="url(#arrowhead)"
              />
            );
          })()}
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
              data-node-id={step.id}
              className={`wfb-canvas-node ${isSelected ? "selected" : ""} ${
                isTrigger ? "node-trigger-pill" : ""
              }`}
              style={{ left: `${step.x}px`, top: `${step.y}px` }}
              onMouseDown={(e) => startDragNode(e, step.id)}
            >
              {/* Left Input Port Dot */}
              <div className="wfb-port wfb-port-in" title="Connect to this node">
                <div className="wfb-port-inner" />
              </div>

              {/* Right Output Port Dot (Drag Out to Connect) */}
              <div
                className="wfb-port wfb-port-out"
                title="Drag out to create new connection"
                onMouseDown={(e) => startConnecting(e, step.id)}
              >
                <Plus size={8} className="text-white opacity-0 hover:opacity-100" />
              </div>

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
                      {/* Select 1: Ivy Styled Promptware Selector */}
                      <div className="wfb-node-select-row">
                        <Layers size={14} className="wfb-select-icon text-cyan-400" />
                        <select
                          className="wfb-node-dropdown"
                          value={step.connectionName || "CodeQuality"}
                          onChange={(e) => updateStep(step.id, { connectionName: e.target.value })}
                        >
                          <option value="CodeQuality">CodeQuality</option>
                          <option value="CodeSecurity">CodeSecurity</option>
                          <option value="PlanDrafter">PlanDrafter</option>
                          <option value="PlanReviewer">PlanReviewer</option>
                          <option value="PRCreator">PRCreator</option>
                        </select>
                      </div>

                      {/* Select 2: Ivy Styled Agent-CLI / Model Selector */}
                      <div className="wfb-node-select-row">
                        <Terminal size={14} className="wfb-select-icon text-purple-400" />
                        <select
                          className="wfb-node-dropdown"
                          value={step.model || "Claude Code (Fable 5 High)"}
                          onChange={(e) => updateStep(step.id, { model: e.target.value })}
                        >
                          <option value="Claude Code (Fable 5 High)">Claude Code (Fable 5 High)</option>
                          <option value="Claude Code (Opus 5 High)">Claude Code (Opus 5 High)</option>
                          <option value="Claude Code (Sonnet 3.5)">Claude Code (Sonnet 3.5)</option>
                          <option value="Ollama (Llama 3.3 70B)">Ollama (Llama 3.3 70B)</option>
                          <option value="Ollama (Qwen 2.5 Coder)">Ollama (Qwen 2.5 Coder)</option>
                          <option value="GPT-4o">GPT-4o</option>
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
