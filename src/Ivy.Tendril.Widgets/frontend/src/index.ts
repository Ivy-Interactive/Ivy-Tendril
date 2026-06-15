import { TendrilProcessViewer } from "./TendrilProcessViewer";
import { AgentViewer } from "./AgentViewer";
import { DraftMarkdown } from "./DraftMarkdown";
import { SortableVerificationList } from "./SortableVerificationList";

if (typeof window !== "undefined") {
  (window as unknown as Record<string, unknown>).IvyTendrilWidgets = {
    TendrilProcessViewer,
    AgentViewer,
    DraftMarkdown,
    SortableVerificationList,
  };
}

export { TendrilProcessViewer, AgentViewer, DraftMarkdown, SortableVerificationList };
