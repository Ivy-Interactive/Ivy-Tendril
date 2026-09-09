import React, { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { Maximize2, Minus, Plus } from "lucide-react";
import {
  buildNeighbourIndex,
  layoutGraph,
  normalizeEdges,
  truncateToWidth,
  type GraphEdge,
  type GraphNode,
  type Orientation,
} from "./layout";
import "./plan-dependency-graph.css";

type IvyEventHandler = (eventName: string, widgetId: string, args: unknown[]) => void;

export interface PlanDependencyGraphProps {
  id: string;
  nodes?: GraphNode[];
  edges?: GraphEdge[];
  selectedId?: string | null;
  orientation?: Orientation;
  showLegend?: boolean;
  emptyMessage?: string;
  events?: string[];
  eventHandler?: IvyEventHandler;
}

const EMPTY_NODES: GraphNode[] = [];
const EMPTY_EDGES: GraphEdge[] = [];
const EMPTY_EVENTS: string[] = [];

const MIN_ZOOM = 0.25;
const MAX_ZOOM = 2.5;
const ZOOM_STEP = 1.25;
const DRAG_SLOP = 4;
const TITLE_FONT_SIZE = 13;
const META_FONT_SIZE = 11;

const KNOWN_STATUSES = [
  "draft",
  "creating",
  "updating",
  "executing",
  "review",
  "completed",
  "failed",
  "blocked",
  "skipped",
  "icebox",
];

const statusKey = (status?: string): string => {
  const value = (status ?? "").trim().toLowerCase();
  return KNOWN_STATUSES.includes(value) ? value : "unknown";
};

const clamp = (value: number, min: number, max: number) => Math.min(max, Math.max(min, value));

interface Transform {
  x: number;
  y: number;
  k: number;
}

const IDENTITY: Transform = { x: 0, y: 0, k: 1 };

export const PlanDependencyGraph: React.FC<PlanDependencyGraphProps> = ({
  id,
  nodes = EMPTY_NODES,
  edges = EMPTY_EDGES,
  selectedId = null,
  orientation = "vertical",
  showLegend = true,
  emptyMessage = "No plan dependencies to show",
  events = EMPTY_EVENTS,
  eventHandler,
}) => {
  const viewportRef = useRef<HTMLDivElement>(null);
  const [transform, setTransform] = useState<Transform>(IDENTITY);
  const [hoveredId, setHoveredId] = useState<string | null>(null);

  const cleanEdges = useMemo(() => normalizeEdges(nodes, edges), [nodes, edges]);
  const layout = useMemo(
    () => layoutGraph(nodes, cleanEdges, { orientation }),
    [nodes, cleanEdges, orientation]
  );
  const neighbours = useMemo(() => buildNeighbourIndex(cleanEdges), [cleanEdges]);
  const nodeById = useMemo(() => new Map(nodes.map((node) => [node.id, node])), [nodes]);

  // Hovering dims everything the plan is not connected to; a selection only highlights, because a
  // selected plan is a long-lived state and a permanently dimmed graph is hard to read.
  const hoverSet = useMemo(() => {
    if (!hoveredId || !nodeById.has(hoveredId)) return null;
    return new Set<string>([hoveredId, ...(neighbours.get(hoveredId) ?? [])]);
  }, [hoveredId, neighbours, nodeById]);
  const activeId = hoveredId ?? selectedId;

  const fitView = useCallback(() => {
    const viewport = viewportRef.current;
    if (!viewport || layout.width === 0 || layout.height === 0) return;
    const { clientWidth, clientHeight } = viewport;
    if (clientWidth === 0 || clientHeight === 0) return;
    const k = clamp(
      Math.min(clientWidth / layout.width, clientHeight / layout.height, 1),
      MIN_ZOOM,
      MAX_ZOOM
    );
    setTransform({
      k,
      x: (clientWidth - layout.width * k) / 2,
      y: (clientHeight - layout.height * k) / 2,
    });
  }, [layout.width, layout.height]);

  // A pan or a zoom is the viewer's own framing, so a later resize must not throw it away.
  const adjustedRef = useRef(false);

  useLayoutEffect(() => {
    adjustedRef.current = false;
    fitView();
  }, [fitView]);

  useEffect(() => {
    const viewport = viewportRef.current;
    if (!viewport || typeof ResizeObserver === "undefined") return;
    const observer = new ResizeObserver(() => {
      if (!adjustedRef.current) fitView();
    });
    observer.observe(viewport);
    return () => observer.disconnect();
  }, [fitView]);

  const zoomBy = useCallback((factor: number, origin?: { x: number; y: number }) => {
    adjustedRef.current = true;
    setTransform((current) => {
      const k = clamp(current.k * factor, MIN_ZOOM, MAX_ZOOM);
      if (k === current.k) return current;
      const viewport = viewportRef.current;
      const anchor = origin ?? {
        x: (viewport?.clientWidth ?? 0) / 2,
        y: (viewport?.clientHeight ?? 0) / 2,
      };
      const scale = k / current.k;
      return {
        k,
        x: anchor.x - (anchor.x - current.x) * scale,
        y: anchor.y - (anchor.y - current.y) * scale,
      };
    });
  }, []);

  // Wheel has to be a native non-passive listener; React routes it through a passive root handler.
  useEffect(() => {
    const viewport = viewportRef.current;
    if (!viewport) return;
    const onWheel = (event: WheelEvent) => {
      event.preventDefault();
      const rect = viewport.getBoundingClientRect();
      const origin = { x: event.clientX - rect.left, y: event.clientY - rect.top };
      if (event.ctrlKey || event.metaKey || Math.abs(event.deltaY) > Math.abs(event.deltaX)) {
        zoomBy(event.deltaY < 0 ? ZOOM_STEP : 1 / ZOOM_STEP, origin);
      } else {
        adjustedRef.current = true;
        setTransform((current) => ({ ...current, x: current.x - event.deltaX }));
      }
    };
    viewport.addEventListener("wheel", onWheel, { passive: false });
    return () => viewport.removeEventListener("wheel", onWheel);
  }, [zoomBy]);

  const dragRef = useRef<{ pointerId: number; x: number; y: number; moved: boolean } | null>(null);
  const [panning, setPanning] = useState(false);

  const onPointerDown = (event: React.PointerEvent<HTMLDivElement>) => {
    if (event.button !== 0) return;
    dragRef.current = { pointerId: event.pointerId, x: event.clientX, y: event.clientY, moved: false };
  };

  const onPointerMove = (event: React.PointerEvent<HTMLDivElement>) => {
    const drag = dragRef.current;
    if (!drag || drag.pointerId !== event.pointerId) return;
    const dx = event.clientX - drag.x;
    const dy = event.clientY - drag.y;
    if (!drag.moved && Math.hypot(dx, dy) < DRAG_SLOP) return;
    if (!drag.moved) {
      // Capture only once the pointer really is panning: a captured pointer retargets the click
      // to the viewport, which would swallow every click on a node.
      event.currentTarget.setPointerCapture?.(event.pointerId);
      adjustedRef.current = true;
      setPanning(true);
    }
    drag.moved = true;
    drag.x = event.clientX;
    drag.y = event.clientY;
    setTransform((current) => ({ ...current, x: current.x + dx, y: current.y + dy }));
  };

  const endDrag = (event: React.PointerEvent<HTMLDivElement>) => {
    const drag = dragRef.current;
    if (!drag || drag.pointerId !== event.pointerId) return;
    if (event.currentTarget.hasPointerCapture?.(event.pointerId)) {
      event.currentTarget.releasePointerCapture(event.pointerId);
    }
    dragRef.current = null;
    setPanning(false);
  };

  const fireNodeClick = (nodeId: string) => {
    if (eventHandler && events.includes("OnNodeClick")) {
      eventHandler("OnNodeClick", id, [nodeId]);
    }
  };

  const legendStatuses = useMemo(() => {
    const present = new Set(nodes.map((node) => statusKey(node.status)));
    return KNOWN_STATUSES.filter((status) => present.has(status));
  }, [nodes]);

  if (nodes.length === 0) {
    return (
      <div className="pdg-root pdg-empty">
        <span>{emptyMessage}</span>
      </div>
    );
  }

  return (
    <div className="pdg-root">
      <div
        ref={viewportRef}
        className={`pdg-viewport${panning ? " is-panning" : ""}`}
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={endDrag}
        onPointerCancel={endDrag}
      >
        <svg
          className="pdg-canvas"
          width={layout.width * transform.k}
          height={layout.height * transform.k}
          viewBox={`0 0 ${layout.width} ${layout.height}`}
          style={{ transform: `translate(${transform.x}px, ${transform.y}px)` }}
          role="img"
          aria-label="Plan dependency graph"
        >
          <defs>
            <marker
              id={`pdg-arrow-${id}`}
              viewBox="0 0 8 8"
              refX="7"
              refY="4"
              markerWidth="7"
              markerHeight="7"
              orient="auto-start-reverse"
            >
              <path className="pdg-arrow" d="M 0 0 L 8 4 L 0 8 z" />
            </marker>
          </defs>

          <g className="pdg-edges">
            {layout.edges.map((edge) => {
              const touchesActive = edge.plan === activeId || edge.dependsOn === activeId;
              const active = activeId !== null && touchesActive;
              const dimmed = hoverSet !== null && !touchesActive;
              return (
                <path
                  key={`${edge.dependsOn}->${edge.plan}`}
                  className={`pdg-edge${edge.cyclic ? " is-cyclic" : ""}${active ? " is-active" : ""}${dimmed ? " is-dimmed" : ""}`}
                  d={edge.path}
                  markerEnd={`url(#pdg-arrow-${id})`}
                  data-from={edge.dependsOn}
                  data-to={edge.plan}
                />
              );
            })}
          </g>

          <g className="pdg-nodes">
            {layout.nodes.map((placed) => {
              const node = nodeById.get(placed.id)!;
              const status = statusKey(node.status);
              const selected = selectedId === node.id;
              const dimmed = hoverSet !== null && !hoverSet.has(node.id);
              const title = node.title?.trim() || node.id;
              const meta = [node.badge?.trim() || node.id, node.project?.trim(), node.level?.trim()]
                .filter(Boolean)
                .join(" · ");
              return (
                <g
                  key={node.id}
                  className={`pdg-node pdg-status-${status}${selected ? " is-selected" : ""}${dimmed ? " is-dimmed" : ""}`}
                  transform={`translate(${placed.x}, ${placed.y})`}
                  role="button"
                  tabIndex={0}
                  aria-label={`${title} (${node.status ?? "unknown"})`}
                  aria-pressed={selected}
                  data-plan-id={node.id}
                  onClick={() => fireNodeClick(node.id)}
                  onKeyDown={(event) => {
                    if (event.key === "Enter" || event.key === " ") {
                      event.preventDefault();
                      fireNodeClick(node.id);
                    }
                  }}
                  onPointerEnter={() => setHoveredId(node.id)}
                  onPointerLeave={() => setHoveredId((current) => (current === node.id ? null : current))}
                  onFocus={() => setHoveredId(node.id)}
                  onBlur={() => setHoveredId((current) => (current === node.id ? null : current))}
                >
                  <title>{`${title}${node.status ? ` (${node.status})` : ""}`}</title>
                  <rect className="pdg-node-box" width={placed.width} height={placed.height} rx={10} />
                  <rect className="pdg-node-accent" width={4} height={placed.height} rx={2} />
                  <text className="pdg-node-title" x={16} y={26} fontSize={TITLE_FONT_SIZE}>
                    {truncateToWidth(title, placed.width - 28, TITLE_FONT_SIZE)}
                  </text>
                  <text className="pdg-node-meta" x={16} y={44} fontSize={META_FONT_SIZE}>
                    {truncateToWidth(meta, placed.width - 28, META_FONT_SIZE)}
                  </text>
                  <circle className="pdg-node-dot" cx={placed.width - 14} cy={20} r={4} />
                </g>
              );
            })}
          </g>
        </svg>

        <div className="pdg-controls">
          <button type="button" aria-label="Zoom in" onClick={() => zoomBy(ZOOM_STEP)}>
            <Plus size={14} />
          </button>
          <button type="button" aria-label="Zoom out" onClick={() => zoomBy(1 / ZOOM_STEP)}>
            <Minus size={14} />
          </button>
          <button
            type="button"
            aria-label="Fit to view"
            onClick={() => {
              adjustedRef.current = false;
              fitView();
            }}
          >
            <Maximize2 size={14} />
          </button>
        </div>
      </div>

      {showLegend && legendStatuses.length > 0 && (
        <div className="pdg-legend">
          {legendStatuses.map((status) => (
            <span key={status} className={`pdg-legend-item pdg-status-${status}`}>
              <span className="pdg-legend-dot" />
              {status}
            </span>
          ))}
        </div>
      )}
    </div>
  );
};
