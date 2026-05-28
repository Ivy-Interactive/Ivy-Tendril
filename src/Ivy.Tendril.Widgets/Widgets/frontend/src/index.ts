import { TendrilProcessView } from "./TendrilProcessView";
import { AgentOutputView } from "./AgentOutputView";

if (typeof window !== "undefined") {
  (window as unknown as Record<string, unknown>).IvyTendrilWidgets = {
    TendrilProcessView,
    AgentOutputView,
  };
}

export { TendrilProcessView, AgentOutputView };
