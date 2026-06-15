import React from "react";
import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";
import type { ResultWire } from "./types";
import { CodeBlock } from "../CodeBlock";

interface ResultSummaryProps {
  wire: ResultWire;
}

export const ResultSummary: React.FC<ResultSummaryProps> = ({ wire }) => {
  const isError = !wire.is_success;
  const usage = wire.usage;

  return (
    <div className={`aov-result ${isError ? "error" : ""}`}>
      <div className="aov-result-header">
        <span className="aov-result-title">{isError ? "❌ Error" : "✅ Completed"}</span>
        {wire.turn_count != null && (
          <span className="aov-result-meta">
            {wire.turn_count} turn{wire.turn_count !== 1 ? "s" : ""}
          </span>
        )}
      </div>
      {wire.response && (
        <div className="aov-markdown aov-result-body">
          <Markdown remarkPlugins={[remarkGfm]} components={{ code: CodeBlock }}>
            {wire.response}
          </Markdown>
        </div>
      )}
      <div className="aov-result-stats">
        {usage?.cost_usd != null && <span>Cost: ${usage.cost_usd.toFixed(4)}</span>}
        {wire.duration_ms != null && (
          <span>Duration: {(wire.duration_ms / 1000).toFixed(1)}s</span>
        )}
        {usage != null && (usage.input_tokens > 0 || usage.output_tokens > 0) && (
          <span>
            Tokens: {usage.input_tokens.toLocaleString()} in / {usage.output_tokens.toLocaleString()} out
          </span>
        )}
        {usage?.premium_requests != null && usage.premium_requests > 0 && (
          <span>Premium: {usage.premium_requests}</span>
        )}
        {wire.exit_code != null && wire.exit_code !== 0 && (
          <span>Exit: {wire.exit_code}</span>
        )}
        {wire.permission_denials != null && wire.permission_denials.length > 0 && (
          <span>Denied: {wire.permission_denials.length}</span>
        )}
      </div>
    </div>
  );
};
