import { TendrilProcessViewer } from "./TendrilProcessViewer";
import { TendrilDashboard } from "./TendrilDashboard/TendrilDashboard";
import { AgentViewer } from "./AgentViewer";
import { DraftMarkdown } from "./DraftMarkdown";
import { SortableVerificationList } from "./SortableVerificationList";
import { ContentInput } from "./ContentInput/ContentInput";
import { BadgeSelect } from "./BadgeSelect";
import { PlanDiffView } from "./PlanDiffView/PlanDiffView";
import { ChatWidget } from "./ChatWidget/ChatWidget";
import { WebViewer } from "./WebViewer";

if (typeof window !== "undefined") {
  (window as unknown as Record<string, unknown>).IvyTendrilWidgets = {
    TendrilProcessViewer,
    TendrilDashboard,
    AgentViewer,
    DraftMarkdown,
    SortableVerificationList,
    ContentInput,
    BadgeSelect,
    PlanDiffView,
    ChatWidget,
    WebViewer,
  };
}

export {
  TendrilProcessViewer,
  TendrilDashboard,
  AgentViewer,
  DraftMarkdown,
  SortableVerificationList,
  ContentInput,
  BadgeSelect,
  PlanDiffView,
  ChatWidget,
  WebViewer,
};
