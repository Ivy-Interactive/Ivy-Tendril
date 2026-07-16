import React, { useEffect, useRef, useState } from "react";
import * as echarts from "echarts";
import { IvyEventHandler } from "../TendrilProcessViewer/types";
import { getWidth, getHeight } from "../styles";

export interface BrainNodeData {
  id: string;
  label: string;
  type: string; // "memory" or "code"
  status: string; // "ok", "outdated", "broken"
}

export interface BrainEdgeData {
  source: string;
  target: string;
}

interface BrainMapProps {
  id: string;
  width?: string;
  height?: string;
  events?: string[];
  eventHandler: IvyEventHandler;
  nodes?: BrainNodeData[];
  edges?: BrainEdgeData[];
  selectedNodeId?: string;
}

export const BrainMap: React.FC<BrainMapProps> = ({
  id,
  width = "Full",
  height = "Full",
  events = [],
  eventHandler,
  nodes = [],
  edges = [],
  selectedNodeId,
}) => {
  const containerRef = useRef<HTMLDivElement>(null);
  const chartInstanceRef = useRef<echarts.ECharts | null>(null);
  const [theme, setTheme] = useState<"light" | "dark">("light");

  // Track system theme changes dynamically
  useEffect(() => {
    const detectTheme = () =>
      document.documentElement.classList.contains("dark") ? "dark" : "light";
    setTheme(detectTheme());

    const observer = new MutationObserver(() => {
      setTheme(detectTheme());
    });

    observer.observe(document.documentElement, {
      attributes: true,
      attributeFilter: ["class"],
    });

    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    if (!containerRef.current) return;

    // Initialize ECharts instance
    const chart = echarts.init(containerRef.current);
    chartInstanceRef.current = chart;

    // Handle clicks on nodes
    chart.on("click", (params) => {
      if (params.dataType === "node") {
        const nodeData = params.data as any;
        if (events.includes("OnNodeClick")) {
          eventHandler("OnNodeClick", id, [nodeData.id]);
        }
      }
    });

    // Resize observer to handle dynamic layout and container changes gracefully
    const resizeObserver = new ResizeObserver(() => {
      chart.resize();
    });
    resizeObserver.observe(containerRef.current);

    return () => {
      resizeObserver.disconnect();
      chart.dispose();
      chartInstanceRef.current = null;
    };
  }, [id, events, eventHandler]);

  // Update chart option whenever nodes, edges, selection, or theme changes
  useEffect(() => {
    const chart = chartInstanceRef.current;
    if (!chart) return;

    const isDark = theme === "dark";

    // Theme responsive design values
    const labelColor = isDark ? "#e5e7eb" : "#1f2937";
    const textBorderColor = isDark ? "#1f2937" : "#ffffff";
    const lineColor = isDark ? "#374151" : "#e5e7eb";
    const legendColor = isDark ? "#9ca3af" : "#4b5563";
    const selectedBorderColor = isDark ? "#ffffff" : "#111827";

    const tooltipBg = isDark ? "#1f2937" : "#ffffff";
    const tooltipBorder = isDark ? "#374151" : "#e5e7eb";
    const tooltipText = isDark ? "#f3f4f6" : "#1f2937";

    // Build the ECharts nodes list
    const chartNodes = nodes.map((n) => {
      const isSelected = n.id === selectedNodeId;
      const isMemory = n.type === "memory";

      // Theme-responsive high-contrast colors
      let color = "#8b5cf6"; // Default memory note violet
      if (isMemory) {
        if (n.status === "outdated") color = isDark ? "#f97316" : "#ea580c"; // Amber/Orange warning
        else if (n.status === "broken") color = isDark ? "#f43f5e" : "#ef4444"; // Rose/Red error
      } else {
        // Code file
        color = n.status === "outdated"
          ? (isDark ? "#fbbf24" : "#d97706") // Warning amber
          : (isDark ? "#22d3ee" : "#0891b2"); // Cyan code file
      }

      return {
        id: n.id,
        name: n.label,
        symbolSize: isSelected ? 28 : isMemory ? 20 : 12,
        value: n.id,
        category: isMemory ? 0 : 1,
        itemStyle: {
          color: color,
          borderColor: isSelected ? selectedBorderColor : undefined,
          borderWidth: isSelected ? 3 : 0,
          shadowBlur: isSelected ? 15 : 0,
          shadowColor: color,
        },
        label: {
          show: isSelected || nodes.length < 50 || isMemory,
          fontSize: isSelected ? 13 : 11,
          fontWeight: isSelected ? ("bold" as const) : ("normal" as const),
          color: labelColor,
          textBorderColor: textBorderColor,
          textBorderWidth: 2,
        },
      };
    });

    // Build the links list (deduplicating to avoid multiple lines between same nodes)
    const seenEdges = new Set<string>();
    const chartLinks: any[] = [];

    for (const e of edges) {
      if (!e.source || !e.target) continue;
      // Sort source and target to create an order-independent unique key
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
            curveness: 0, // Straight lines look clean and matches Obsidian graph view
            opacity: 0.8,
          },
        });
      }
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
      series: [
        {
          type: "graph",
          layout: "force",
          categories: [
            { name: "Memory Notes" },
            { name: "Code Files" },
          ],
          data: chartNodes,
          links: chartLinks,
          roam: true,
          label: {
            show: true,
            position: "right",
            formatter: "{b}",
            color: labelColor,
          },
          force: {
            repulsion: 350,
            edgeLength: 120,
            gravity: 0.05,
            layoutAnimation: true,
          },
          emphasis: {
            focus: "adjacency",
            lineStyle: {
              width: 3,
            },
          },
        },
      ],
    };

    chart.setOption(option);
  }, [nodes, edges, selectedNodeId, theme]);

  const style: React.CSSProperties = {
    ...getWidth(width),
    ...getHeight(height),
    position: "relative",
    overflow: "hidden",
  };

  return (
    <div style={style} className="brain-map-container">
      <div ref={containerRef} style={{ width: "100%", height: "100%" }} />
    </div>
  );
};
