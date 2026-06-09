import { TendrilProcess } from "./TendrilProcess";
import { AgentOutput } from "./AgentOutput";
import { DraftMarkdown } from "./DraftMarkdown";

if (typeof window !== "undefined") {
  (window as unknown as Record<string, unknown>).IvyTendrilWidgets = {
    TendrilProcess,
    AgentOutput,
    DraftMarkdown,
  };
}

export { TendrilProcess, AgentOutput, DraftMarkdown };
