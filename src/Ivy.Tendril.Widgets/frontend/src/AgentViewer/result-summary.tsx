import React from "react";
import Markdown from "react-markdown";
import type { ResultWire } from "./types";
import { BlockHandler } from "../BlockHandler";
import { getMarkdownPlugins } from "../math";

interface ResultSummaryProps {
  wire: ResultWire;
}

export const ResultSummary: React.FC<ResultSummaryProps> = ({ wire }) => {
  const isError = !wire.is_success;
  const usage = wire.usage;

  const statsList: React.ReactNode[] = [];
  if (usage?.cost_usd != null && usage.cost_usd > 0) {
    statsList.push(<span key="cost">Cost: ${usage.cost_usd.toFixed(4)}</span>);
  }
  if (wire.duration_ms != null && wire.duration_ms > 0) {
    statsList.push(<span key="dur">Duration: {(wire.duration_ms / 1000).toFixed(1)}s</span>);
  }
  if (usage != null && (usage.input_tokens > 0 || usage.output_tokens > 0)) {
    statsList.push(
      <span key="tok">
        Tokens: {usage.input_tokens.toLocaleString()} in / {usage.output_tokens.toLocaleString()} out
      </span>
    );
  }
  if (usage?.premium_requests != null && usage.premium_requests > 0) {
    statsList.push(<span key="prem">Premium: {usage.premium_requests}</span>);
  }
  if (wire.exit_code != null && wire.exit_code !== 0) {
    statsList.push(<span key="exit">Exit: {wire.exit_code}</span>);
  }
  if (wire.permission_denials != null && wire.permission_denials.length > 0) {
    statsList.push(<span key="denied">Denied: {wire.permission_denials.length}</span>);
  }

  const hasResponse = Boolean(wire.response && wire.response.trim().length > 0);

  const plugins = getMarkdownPlugins(wire.response ?? "");

  // Return null if there is nothing to render, preventing empty container boxes
  if (!isError && !hasResponse && statsList.length === 0) {
    return null;
  }

  return (
    <div className={`aov-result ${isError ? "error" : ""}`}>
      {isError && (
        <div className="aov-result-header">
          <span className="aov-result-title">❌ Error</span>
        </div>
      )}
      {hasResponse && (
        <div className="aov-markdown aov-result-body">
          <Markdown
            remarkPlugins={plugins.remarkPlugins}
            rehypePlugins={plugins.rehypePlugins}
            components={{ code: BlockHandler }}
          >
            {wire.response}
          </Markdown>
        </div>
      )}
      {statsList.length > 0 && (
        <div className="aov-result-stats">{statsList}</div>
      )}
    </div>
  );
};
