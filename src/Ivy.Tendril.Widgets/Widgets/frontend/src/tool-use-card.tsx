import React, { useState } from "react";

interface ToolCall {
  name: string;
  description?: string;
  input: Record<string, unknown>;
  result?: string;
  isError?: boolean;
}

interface ToolUseCardProps {
  tool: ToolCall;
}

function displayInput(name: string, input: Record<string, unknown>): string {
  if (name === "Bash" && typeof input.command === "string") return input.command;
  if ((name === "Write" || name === "Edit") && typeof input.file_path === "string") {
    let s = `File: ${input.file_path}`;
    if (typeof input.content === "string") {
      s += `\n${input.content.slice(0, 500)}${input.content.length > 500 ? "\n…" : ""}`;
    }
    return s;
  }
  if (name === "Read" && typeof input.file_path === "string") return `File: ${input.file_path}`;
  return JSON.stringify(input, null, 2);
}

function inputSummary(tool: ToolCall): string {
  if (tool.description) return tool.description;
  const { input } = tool;
  if (typeof input.description === "string") return input.description;
  if (typeof input.command === "string") return input.command;
  if (typeof input.file_path === "string") return input.file_path;
  if (typeof input.path === "string") return input.path;
  if (typeof input.pattern === "string") return input.pattern;
  return "";
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

type ToolStatus = "running" | "success" | "error";

function getToolStatus(tool: ToolCall): ToolStatus {
  if (tool.result === undefined) return "running";
  if (tool.isError) return "error";
  return "success";
}

export const ToolUseCard: React.FC<ToolUseCardProps> = ({ tool }) => {
  const status = getToolStatus(tool);
  const [open, setOpen] = useState(false);

  const handleToggle = () => setOpen((o) => !o);

  const summary = inputSummary(tool);
  let headerPreview = summary || "";
  if (tool.result != null && tool.result.length > 0) {
    const firstLine = tool.result.split("\n")[0].slice(0, 80);
    const hasWeirdChars = /[┌┐└┘├┤┬┴┼─│═║╔╗╚╝╠╣╦╩╬]/.test(firstLine);
    if (firstLine.trim() && (tool.isError || !hasWeirdChars)) {
      headerPreview += ` → ${firstLine}`;
    }
  }

  return (
    <div className={`aov-tool ${open ? "open" : ""}`}>
      <div
        className="aov-tool-header"
        onClick={handleToggle}
        role="button"
        tabIndex={0}
        aria-expanded={open}
        onKeyDown={(e) => {
          if (e.key === "Enter" || e.key === " ") {
            e.preventDefault();
            handleToggle();
          }
        }}
      >
        <span className={`aov-tool-chevron ${open ? "open" : ""}`}>
          <ChevronDownIcon />
        </span>
        <span className={`aov-tool-status aov-tool-status--${status}`} />
        <span className="aov-tool-name">{tool.name}</span>
        {headerPreview && <span className="aov-tool-preview">{headerPreview}</span>}
      </div>
      {open && (
        <div className="aov-tool-body">
          <div className="aov-tool-section">
            <span className="aov-tool-label">IN</span>
            <pre className="aov-tool-pre">
              <code>{displayInput(tool.name, tool.input)}</code>
            </pre>
          </div>
          {tool.result != null && tool.result.length > 0 && (
            <div className="aov-tool-section">
              <span className="aov-tool-label">OUT</span>
              <pre className="aov-tool-pre">
                <code>{tool.result}</code>
              </pre>
            </div>
          )}
        </div>
      )}
    </div>
  );
};
