import React, { useState } from "react";
import type { ToolUsePresentation } from "./types";
import { aggregateToolStatus } from "./group-events";
import { ToolUseCard, inputSummary } from "./tool-use-card";

interface ToolUseGroupProps {
  tools: ToolUsePresentation[];
}

const ChevronDownIcon: React.FC = () => (
  <svg
    xmlns="http://www.w3.org/2000/svg"
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    strokeWidth="2"
    strokeLinecap="round"
    strokeLinejoin="round"
    width="14"
    height="14"
    aria-hidden="true"
  >
    <path d="m6 9 6 6 6-6" />
  </svg>
);

export const ToolUseGroup: React.FC<ToolUseGroupProps> = ({ tools }) => {
  const [open, setOpen] = useState(false);
  const status = aggregateToolStatus(tools);

  const handleToggle = () => setOpen((o) => !o);

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "Enter" || e.key === " ") {
      e.preventDefault();
      handleToggle();
    }
  };

  const lastTool = tools[tools.length - 1];
  const lastToolSummary = inputSummary({
    name: lastTool.name,
    input: lastTool.input,
    description: lastTool.description,
  });
  const preview = lastToolSummary ? `${lastTool.name} ${lastToolSummary}` : lastTool.name;

  return (
    <div className={`aov-tool-group ${open ? "open" : ""}`}>
      <div
        className="aov-tool-group-header"
        onClick={handleToggle}
        onKeyDown={handleKeyDown}
        role="button"
        tabIndex={0}
        aria-expanded={open}
      >
        <span className="aov-tool-chevron">
          <ChevronDownIcon />
        </span>
        <span className={`aov-tool-status aov-tool-status--${status}`} />
        <span className="aov-tool-name">{tools.length} tool calls</span>
        <span className="aov-tool-preview">{preview}</span>
      </div>
      {open && (
        <div className="aov-tool-group-body">
          {tools.map((tool) => (
            <ToolUseCard
              key={tool.toolUseId}
              tool={{
                name: tool.name,
                input: tool.input,
                result: tool.result,
                isError: tool.isError,
              }}
            />
          ))}
        </div>
      )}
    </div>
  );
};
