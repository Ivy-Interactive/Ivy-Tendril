import { TendrilProcessViewer } from "./TendrilProcessViewer";
import { AgentViewer } from "./AgentViewer";
import { DraftMarkdown } from "./DraftMarkdown";
import { SortableVerificationList } from "./SortableVerificationList";
import { ContentInput } from "./ContentInput/ContentInput";
import { BrainMap } from "./BrainMap/BrainMap";

if (typeof window !== "undefined") {
  (window as unknown as Record<string, unknown>).IvyTendrilWidgets = {
    TendrilProcessViewer,
    AgentViewer,
    DraftMarkdown,
    SortableVerificationList,
    ContentInput,
    BrainMap,
  };
}

export { TendrilProcessViewer, AgentViewer, DraftMarkdown, SortableVerificationList, ContentInput, BrainMap };
