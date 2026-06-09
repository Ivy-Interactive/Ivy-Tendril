import { TendrilProcessView } from "./TendrilProcessView";
import { AgentOutputView } from "./AgentOutputView";
import { PlanMarkdownView } from "./PlanMarkdownView";

if (typeof window !== "undefined") {
  (window as unknown as Record<string, unknown>).IvyTendrilWidgets = {
    TendrilProcessView,
    AgentOutputView,
    PlanMarkdownView,
  };
}

export { TendrilProcessView, AgentOutputView, PlanMarkdownView };
