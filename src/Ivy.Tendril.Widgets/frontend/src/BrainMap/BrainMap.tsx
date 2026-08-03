import React, { useEffect, useRef, useState } from "react";
import * as echarts from "echarts";

export interface BrainNode {
  id: string;
  label: string;
  type?: "memory" | "file";
  status?: "clean" | "outdated" | "broken";
  linkCount?: number;
}

export interface BrainEdge {
  source: string;
  target: string;
}

export interface BrainMapProps {
  nodes?: BrainNode[];
  edges?: BrainEdge[];
  selectedNodeId?: string;
  onNodeClick?: (nodeId: string) => void;
  width?: number | string;
  height?: number | string;
  theme?: "light" | "dark";
}

const getWidth = (w?: number | string): React.CSSProperties => {
  if (!w) return { width: "100%" };
  if (typeof w === "number") return { width: `${w}px` };
  return { width: w };
};

const getHeight = (h?: number | string): React.CSSProperties => {
  if (!h) return { height: "100%" };
  if (typeof h === "number") return { height: `${h}px` };
  return { height: h };
};

export const BrainMap: React.FC<BrainMapProps> = ({
  nodes = [],
  edges = [],
  selectedNodeId,
  onNodeClick,
  width,
  height,
  theme = "dark",
}) => {
  const containerRef = useRef<HTMLDivElement>(null);
  const chartRef = useRef<echarts.ECharts | null>(null);
  const onNodeClickRef = useRef(onNodeClick);
  onNodeClickRef.current = onNodeClick;

  const [showAllLabels, setShowAllLabels] = useState<boolean>(false);
  const showAllLabelsRef = useRef(showAllLabels);
  showAllLabelsRef.current = showAllLabels;

  const lastFingerprintRef = useRef<string>("");

  // Setup ECharts & ResizeObserver (Runs ONCE per mount)
  useEffect(() => {
    if (!containerRef.current) return;

    if (!chartRef.current) {
      chartRef.current = echarts.init(containerRef.current);
      chartRef.current.on("click", (params: any) => {
        if (params.dataType === "node" && onNodeClickRef.current) {
          onNodeClickRef.current(params.data.id);
        }
      });
    }

    const resizeObserver = new ResizeObserver((entries) => {
      for (const entry of entries) {
        if (entry.contentRect.width > 0 && entry.contentRect.height > 0) {
          chartRef.current?.resize();
        }
      }
    });
    resizeObserver.observe(containerRef.current);

    // Initial resize trigger after mount
    setTimeout(() => {
      chartRef.current?.resize();
    }, 50);

    return () => {
      resizeObserver.disconnect();
      if (chartRef.current) {
        chartRef.current.dispose();
        chartRef.current = null;
      }
    };
  }, []);

  // Main graph option renderer — only runs when nodes, edges, selectedNodeId, or theme changes
  useEffect(() => {
    const chart = chartRef.current;
    if (!chart) return;

    const currentFingerprint = `${nodes.length}_${edges.length}_${selectedNodeId}_${theme}`;
    lastFingerprintRef.current = currentFingerprint;

    const isDark = theme === "dark";
    const labelColor = isDark ? "#d1d5db" : "#374151";
    const lineColor = isDark ? "#4b5563" : "#cbd5e1";
    const legendColor = isDark ? "#9ca3af" : "#6b7280";
    const tooltipBg = isDark ? "#1f2937" : "#ffffff";
    const tooltipBorder = isDark ? "#374151" : "#e5e7eb";
    const tooltipText = isDark ? "#f3f4f6" : "#1f2937";

    const seenNodeIds = new Set<string>();
    const chartNodes = nodes
      .filter((n) => {
        if (!n || !n.id || seenNodeIds.has(n.id)) return false;
        seenNodeIds.add(n.id);
        return true;
      })
      .map((n) => {
        const isSelected = n.id === selectedNodeId;
        const isMemory = n.type === "memory";

        let color = "#3b82f6";
        if (isMemory) {
          if (n.status === "outdated") color = "#f59e0b";
          else if (n.status === "broken") color = "#ef4444";
          else color = "#a855f7";
        } else {
          color = "#10b981";
        }

        const size = isSelected ? 24 : isMemory ? 14 : 10;

        return {
          id: n.id,
          name: n.id,
          labelContent: n.label || n.id,
          value: n.linkCount || 1,
          symbolSize: size,
          category: isMemory ? 0 : 1,
          itemStyle: {
            color: color,
            borderColor: isSelected ? "#ffffff" : color,
            borderWidth: isSelected ? 3 : 0,
            shadowBlur: isSelected ? 12 : 0,
            shadowColor: isSelected ? "#ffffff" : "transparent",
          },
        };
      });

    const nodeIds = new Set(nodes.map((n) => n.id));
    const seenEdges = new Set<string>();
    const chartLinks: any[] = [];

    for (const e of edges) {
      if (!e.source || !e.target || !nodeIds.has(e.source) || !nodeIds.has(e.target)) continue;
      const key = e.source < e.target 
        ? `${e.source}->${e.target}` 
        : `${e.target}->${e.source}`;

      if (!seenEdges.has(key)) {
        seenEdges.add(key);
        chartLinks.push({
          source: e.source,
          target: e.target,
          lineStyle: {
            color: lineColor,
            width: 1.5,
            curveness: 0,
            opacity: 0.7,
          },
        });
      }
    }

    const n = nodes.length;
    const isLargeGraph = n > 40;

    const currentOpt = chart.getOption() as any;
    const existingSeries = currentOpt?.series?.[0];
    const currentZoom = existingSeries?.zoom ?? 1.0;
    const currentCenter = existingSeries?.center;

    const graphSeries: any = {
      type: "graph",
      layout: "force",
      categories: [
        { name: "Memory Notes" },
        { name: "Code Files" },
      ],
      data: chartNodes,
      links: chartLinks,
      roam: true,
      zoom: currentZoom,
      scaleLimit: {
        min: 0.05,
        max: 15,
      },
      label: {
        show: showAllLabelsRef.current || !isLargeGraph,
        position: "right",
        formatter: (params: any) => {
          const text = params.data.labelContent || params.name;
          return text.length > 24 ? text.slice(0, 21) + "…" : text;
        },
        color: labelColor,
        fontSize: 11,
      },
      force: {
        repulsion: isLargeGraph ? 450 : 350,
        edgeLength: isLargeGraph ? 130 : 100,
        gravity: 0.03,
        layoutAnimation: true, // Always animate layout so edges stay attached to nodes
      },
      emphasis: {
        focus: "adjacency",
        label: {
          show: true,
          position: "right",
          fontSize: 12,
          fontWeight: "bold",
          color: isDark ? "#ffffff" : "#111827",
          backgroundColor: isDark ? "rgba(17, 24, 39, 0.9)" : "rgba(255, 255, 255, 0.9)",
          padding: [3, 6],
          borderRadius: 4,
          shadowColor: "rgba(0,0,0,0.3)",
          shadowBlur: 4,
        },
        lineStyle: {
          width: 2.5,
          opacity: 1,
        },
      },
    };

    if (currentCenter) {
      graphSeries.center = currentCenter;
    }

    const option: any = {
      backgroundColor: "transparent",
      tooltip: {
        trigger: "item",
        backgroundColor: "transparent",
        borderColor: "transparent",
        borderWidth: 0,
        shadowColor: "transparent",
        shadowBlur: 0,
        padding: 0,
        formatter: (params: any) => {
          if (params.dataType === "node") {
            const isMemory = params.data.category === 0;
            return `<div style="padding: 8px 12px; font-size: 12px; font-family: sans-serif; background: ${tooltipBg}; border: 1px solid ${tooltipBorder}; color: ${tooltipText}; border-radius: 6px; box-shadow: 0 4px 6px -1px rgb(0 0 0 / ${
              isDark ? "0.4" : "0.1"
            }), 0 2px 4px -2px rgb(0 0 0 / ${isDark ? "0.4" : "0.1"});">
              <strong style="display: block; margin-bottom: 2px;">${
                isMemory ? "Memory Note" : "Code File"
              }</strong>
              <span style="font-family: monospace; color: #a855f7; font-size: 11px; word-break: break-all;">${
                params.data.id
              }</span>
            </div>`;
          }
          return "";
        },
      },
      legend: [
        {
          data: ["Memory Notes", "Code Files"],
          textStyle: {
            color: legendColor,
            fontSize: 11,
          },
          bottom: 10,
        },
      ],
      series: [graphSeries],
    };

    chart.setOption(option);
  }, [nodes, edges, selectedNodeId, theme]);

  // Zero-lag label toggle — updates label visibility ONLY in-place with lazyUpdate
  useEffect(() => {
    const chart = chartRef.current;
    if (!chart) return;

    const isLargeGraph = nodes.length > 40;
    chart.setOption(
      {
        series: [
          {
            label: {
              show: showAllLabels || !isLargeGraph,
            },
          },
        ],
      },
      false,
      true
    );
  }, [showAllLabels, nodes.length]);

  const style: React.CSSProperties = {
    ...getWidth(width),
    ...getHeight(height),
    position: "relative",
    overflow: "hidden",
  };

  const memoryCount = nodes.filter((n) => n.type === "memory").length;
  const isDark = theme === "dark";

  const zoomIn = () => {
    const chart = chartRef.current;
    if (!chart) return;
    const opt = chart.getOption() as any;
    const currentZoom = opt?.series?.[0]?.zoom || 1.0;
    chart.setOption({ series: [{ zoom: currentZoom * 1.35 }] });
  };

  const zoomOut = () => {
    const chart = chartRef.current;
    if (!chart) return;
    const opt = chart.getOption() as any;
    const currentZoom = opt?.series?.[0]?.zoom || 1.0;
    chart.setOption({ series: [{ zoom: Math.max(0.05, currentZoom / 1.35) }] });
  };

  const resetView = () => {
    const chart = chartRef.current;
    if (!chart) return;
    chart.dispatchAction({ type: "restore" });
    chart.setOption({ series: [{ zoom: 1.0 }] });
  };

  return (
    <div style={style} className="brain-map-container remove-parent-padding">
      <div ref={containerRef} style={{ width: "100%", height: "100%" }} />

      {/* Control Buttons (Zoom In/Out/Reset/Labels) */}
      <div
        style={{
          position: "absolute",
          top: 12,
          right: 12,
          display: "flex",
          gap: 6,
          zIndex: 10,
        }}
      >
        <button
          type="button"
          onClick={() => setShowAllLabels(!showAllLabels)}
          title={showAllLabels ? "Show Labels on Hover Only" : "Show All Labels"}
          style={{
            backgroundColor: isDark ? (showAllLabels ? "#7c3aed" : "rgba(31, 41, 55, 0.85)") : (showAllLabels ? "#7c3aed" : "rgba(255, 255, 255, 0.85)"),
            color: showAllLabels ? "#ffffff" : isDark ? "#d1d5db" : "#374151",
            border: `1px solid ${isDark ? "#374151" : "#cbd5e1"}`,
            borderRadius: 6,
            padding: "4px 10px",
            fontSize: 12,
            fontWeight: 600,
            cursor: "pointer",
            backdropFilter: "blur(4px)",
          }}
        >
          {showAllLabels ? "Labels: On" : "Labels: Hover"}
        </button>

        <button
          type="button"
          onClick={zoomIn}
          title="Zoom In"
          style={{
            backgroundColor: isDark ? "rgba(31, 41, 55, 0.85)" : "rgba(255, 255, 255, 0.85)",
            color: isDark ? "#d1d5db" : "#374151",
            border: `1px solid ${isDark ? "#374151" : "#cbd5e1"}`,
            borderRadius: 6,
            width: 28,
            height: 28,
            fontSize: 14,
            fontWeight: "bold",
            cursor: "pointer",
            backdropFilter: "blur(4px)",
          }}
        >
          +
        </button>

        <button
          type="button"
          onClick={zoomOut}
          title="Zoom Out"
          style={{
            backgroundColor: isDark ? "rgba(31, 41, 55, 0.85)" : "rgba(255, 255, 255, 0.85)",
            color: isDark ? "#d1d5db" : "#374151",
            border: `1px solid ${isDark ? "#374151" : "#cbd5e1"}`,
            borderRadius: 6,
            width: 28,
            height: 28,
            fontSize: 14,
            fontWeight: "bold",
            cursor: "pointer",
            backdropFilter: "blur(4px)",
          }}
        >
          -
        </button>

        <button
          type="button"
          onClick={resetView}
          title="Reset View"
          style={{
            backgroundColor: isDark ? "rgba(31, 41, 55, 0.85)" : "rgba(255, 255, 255, 0.85)",
            color: isDark ? "#d1d5db" : "#374151",
            border: `1px solid ${isDark ? "#374151" : "#cbd5e1"}`,
            borderRadius: 6,
            padding: "4px 8px",
            fontSize: 12,
            cursor: "pointer",
            backdropFilter: "blur(4px)",
          }}
        >
          Reset
        </button>
      </div>

      <div
        style={{
          position: "absolute",
          bottom: 12,
          right: 12,
          backgroundColor: isDark ? "rgba(31, 41, 55, 0.8)" : "rgba(255, 255, 255, 0.8)",
          backdropFilter: "blur(4px)",
          border: `1px solid ${isDark ? "#374151" : "#e5e7eb"}`,
          borderRadius: 6,
          padding: "4px 8px",
          fontSize: 11,
          fontFamily: "monospace",
          color: isDark ? "#9ca3af" : "#4b5563",
          pointerEvents: "none",
          zIndex: 10,
        }}
      >
        Memories: {memoryCount}
      </div>
    </div>
  );
};
