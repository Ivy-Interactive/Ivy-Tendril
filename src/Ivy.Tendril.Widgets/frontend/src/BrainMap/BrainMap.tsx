import React, { useEffect, useRef } from "react";
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

    const handleResize = () => {
      chart.resize();
    };

    window.addEventListener("resize", handleResize);

    return () => {
      window.removeEventListener("resize", handleResize);
      chart.dispose();
      chartInstanceRef.current = null;
    };
  }, [id, events, eventHandler]);

  // Update chart option whenever nodes, edges, or selection changes
  useEffect(() => {
    const chart = chartInstanceRef.current;
    if (!chart) return;

    // Build the ECharts nodes list
    const chartNodes = nodes.map((n) => {
      const isSelected = n.id === selectedNodeId;
      const isMemory = n.type === "memory";

      // Color mapping
      let color = "#8b5cf6"; // Default violet
      if (isMemory) {
        if (n.status === "outdated") color = "#f97316"; // Orange warning
        else if (n.status === "broken") color = "#f43f5e"; // Red error
      } else {
        // Code file
        color = n.status === "outdated" ? "#eab308" : "#06b6d4"; // Amber or Cyan
      }

      return {
        id: n.id,
        name: n.label,
        symbolSize: isSelected ? 28 : isMemory ? 20 : 12,
        value: n.id,
        category: isMemory ? 0 : 1,
        itemStyle: {
          color: color,
          borderColor: isSelected ? "#ffffff" : undefined,
          borderWidth: isSelected ? 3 : 0,
          shadowBlur: isSelected ? 15 : 0,
          shadowColor: color,
        },
        label: {
          show: isSelected || nodes.length < 50 || isMemory,
          fontSize: isSelected ? 13 : 11,
          fontWeight: isSelected ? ("bold" as const) : ("normal" as const),
        },
      };
    });

    // Build the links list
    const chartLinks = edges.map((e) => ({
      source: e.source,
      target: e.target,
      lineStyle: {
        color: "#4b5563",
        width: 1.5,
        curveness: 0.1,
        opacity: 0.6,
      },
    }));

    const option: any = {
      backgroundColor: "transparent",
      tooltip: {
        trigger: "item",
        formatter: (params: any) => {
          if (params.dataType === "node") {
            const isMemory = params.data.category === 0;
            return `<div style="padding: 4px 8px; font-size: 12px; font-family: sans-serif;">
              <strong>${isMemory ? "Memory Note" : "Code File"}</strong><br/>
              Path: <span style="font-family: monospace; color: #a855f7;">${params.data.id}</span>
            </div>`;
          }
          return "";
        },
      },
      legend: [
        {
          data: ["Memory Notes", "Code Files"],
          textStyle: {
            color: "#9ca3af",
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
            color: "#e5e7eb",
          },
          force: {
            repulsion: 200,
            edgeLength: 120,
            gravity: 0.08,
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
  }, [nodes, edges, selectedNodeId]);

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
