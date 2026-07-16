import React, { useState, useEffect, useRef } from "react";
import { Save, Plus, Trash2 } from "lucide-react";
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

interface WorkflowBuilderProps {
  id: string;
  workflowDefinitionJson?: string;
  availableConnections?: ConnectionItem[];
  availableProviders?: string[];
  eventHandler: IvyEventHandler;
}

export const WorkflowBuilder: React.FC<WorkflowBuilderProps> = ({
  id,
  workflowDefinitionJson = "",
  availableConnections = [],
  availableProviders = [],
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

  const canvasRef = useRef<HTMLDivElement>(null);

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

  const saveWorkflow = () => {
    const definition: WorkflowDefinition = { steps };
    const jsonStr = JSON.stringify(definition, null, 2);
    eventHandler("OnSave", id, [jsonStr]);
  };

  const getPortCoords = (s: WorkflowStep) => {
    const w = 280;
    let h = 250;
    if (s.type.toLowerCase() === "trigger") h = 130;
    else if (s.type.toLowerCase() === "connection") h = 310;
    else if (s.type.toLowerCase() === "prompt") h = 250;

    const x = s.x || 0;
    const y = s.y || 0;
    return {
      input: { x, y: y + h / 2 },
      output: { x: x + w, y: y + h / 2 },
    };
  };

  // Drag Card
  const handleCardMouseDown = (e: React.MouseEvent, stepId: string) => {
    const target = e.target as HTMLElement;
    if (target.tagName.toLowerCase() === "input" || target.tagName.toLowerCase() === "select" || target.tagName.toLowerCase() === "textarea" || target.tagName.toLowerCase() === "button") {
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
    connName: string = ""
  ) => {
    e.dataTransfer.setData("type", type);
    e.dataTransfer.setData("connectionName", connName);
  };

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault();
    if (!canvasRef.current) return;
    const rect = canvasRef.current.getBoundingClientRect();
    const x = e.clientX - rect.left + canvasRef.current.scrollLeft - 140;
    const y = e.clientY - rect.top + canvasRef.current.scrollTop - 50;

    const type = e.dataTransfer.getData("type");
    const connectionName = e.dataTransfer.getData("connectionName");

    if (!type) return;

    const newStep: WorkflowStep = {
      id: Math.random().toString(36).substring(2, 9),
      name: `${type}_Step_${steps.length + 1}`,
      type,
      connectionName: connectionName || "",
      action: "",
      args: type === "Connection" ? "{}" : "Your prompt here...",
      provider: type === "Prompt" ? (availableProviders[0] || "default") : "",
      model: type === "Prompt" ? "default" : "",
      next: [],
      x: Math.max(0, x),
      y: Math.max(0, y),
    };

    setSteps([...steps, newStep]);
  };

  // Quick Spawn Connected Element (+)
  const spawnStep = (sourceId: string, type: "Connection" | "Prompt", connectionName = "") => {
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
    const updated = steps
      .filter((s) => s.id !== stepId)
      .map((s) => ({
        ...s,
        next: s.next.filter((nid) => nid !== stepId),
      }));
    setSteps(updated);
  };

  const removeConnection = (fromId: string, toId: string) => {
    setSteps(
      steps.map((s) =>
        s.id === fromId ? { ...s, next: s.next.filter((id) => id !== toId) } : s
      )
    );
  };

  const updateStep = (stepId: string, updates: Partial<WorkflowStep>) => {
    setSteps(steps.map((s) => (s.id === stepId ? { ...s, ...updates } : s)));
  };

  const drawBezier = (fx: number, fy: number, tx: number, ty: number) => {
    const dx = tx - fx;
    const cx1 = fx + dx * 0.5;
    const cx2 = tx - dx * 0.5;
    return `M ${fx} ${fy} C ${cx1} ${fy}, ${cx2} ${ty}, ${tx} ${ty}`;
  };

  return (
    <div className="wfb-shell">
      {/* Palette Sidebar */}
      <div className="wfb-sidebar">
        <div className="wfb-sidebar-title">Step Templates</div>
        <div className="wfb-palette-list">
          <div
            className="wfb-palette-item"
            draggable
            onDragStart={(e) => handleDragStart(e, "Prompt")}
          >
            <span className="wfb-step-badge wfb-badge-prompt">Prompt</span>
            Agent Prompt
          </div>

          <div className="wfb-sidebar-title" style={{ marginTop: "12px" }}>
            Integrations
          </div>
          {availableConnections.map((conn) => (
            <div
              key={conn.id}
              className="wfb-palette-item"
              draggable
              onDragStart={(e) => handleDragStart(e, "Connection", conn.name)}
            >
              <span className="wfb-step-badge wfb-badge-connection">
                {conn.provider}
              </span>
              {conn.name}
            </div>
          ))}
          {availableConnections.length === 0 && (
            <div style={{ fontSize: "0.75rem", color: "var(--muted-foreground)" }}>
              No connections configured. Add one in the Connections tab first.
            </div>
          )}
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

        {/* Toolbar */}
        <div className="wfb-canvas-toolbar">
          <button className="wfb-btn wfb-btn-primary" onClick={saveWorkflow}>
            <Save size={14} style={{ marginRight: "6px" }} />
            Save
          </button>
        </div>

        {/* Step Nodes Cards */}
        {steps.map((step) => {
          const isTrigger = step.type.toLowerCase() === "trigger";
          const isConnection = step.type.toLowerCase() === "connection";
          const isPrompt = step.type.toLowerCase() === "prompt";

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
              className={`wfb-canvas-card type-${step.type.toLowerCase()}`}
              style={{ left: `${step.x}px`, top: `${step.y}px` }}
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
                    onChange={(e) =>
                      updateStep(step.id, {
                        name: e.target.value.replace(/\s+/g, "_"),
                      })
                    }
                    placeholder="Step Name"
                  />
                </div>
                {!isTrigger && (
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
                    }}
                  >
                    Workflow Trigger: Manually run this workflow. Trigger payload can
                    be injected in child step prompts.
                  </div>
                )}

                {isConnection && (
                  <>
                    <div className="wfb-field-group">
                      <label className="wfb-label">Connection</label>
                      <select
                        className="wfb-select"
                        value={step.connectionName}
                        onChange={(e) =>
                          updateStep(step.id, {
                            connectionName: e.target.value,
                            action: "",
                          })
                        }
                      >
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
                          onChange={(e) =>
                            updateStep(step.id, { action: e.target.value })
                          }
                          placeholder="SendMessage"
                        />
                      ) : (
                        <select
                          className="wfb-select"
                          value={step.action}
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

              {/* Spawn step (+) element button */}
              <button
                className="wfb-quick-add"
                onClick={() =>
                  setSpawnMenuId(spawnMenuId === step.id ? null : step.id)
                }
              >
                <Plus size={12} />
              </button>

              {/* Floating Spawn Menu */}
              {spawnMenuId === step.id && (
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
      </div>
    </div>
  );
};
