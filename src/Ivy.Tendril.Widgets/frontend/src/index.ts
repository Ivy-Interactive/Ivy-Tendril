import { TendrilProcessViewer } from "./TendrilProcessViewer";
import { AgentViewer } from "./AgentViewer";
import { DraftMarkdown } from "./DraftMarkdown";
import { SortableVerificationList } from "./SortableVerificationList";
import { ContentInput } from "./ContentInput/ContentInput";
import { BadgeSelect } from "./BadgeSelect";
import { BrainMap } from "./BrainMap/BrainMap";
import { PlanDiffView } from "./PlanDiffView/PlanDiffView";
import { ChatWidget } from "./ChatWidget/ChatWidget";

if (typeof window !== "undefined") {
  (window as unknown as Record<string, unknown>).IvyTendrilWidgets = {
    TendrilProcessViewer,
    AgentViewer,
    DraftMarkdown,
    SortableVerificationList,
    ContentInput,
    BadgeSelect,
    BrainMap,
    PlanDiffView,
    ChatWidget,
  };
}

export {
  TendrilProcessViewer,
  AgentViewer,
  DraftMarkdown,
  SortableVerificationList,
  ContentInput,
  BadgeSelect,
  BrainMap,
  PlanDiffView,
  ChatWidget,
};
