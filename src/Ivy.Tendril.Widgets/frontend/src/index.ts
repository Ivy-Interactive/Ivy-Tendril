import { TendrilProcessViewer } from "./TendrilProcessViewer";
import { AgentViewer } from "./AgentViewer";
import { DraftMarkdown } from "./DraftMarkdown";
import { SortableVerificationList } from "./SortableVerificationList";
import { ContentInput } from "./ContentInput/ContentInput";
import { BadgeSelect } from "./BadgeSelect";
import { WorkflowBuilder } from "./WorkflowBuilder/WorkflowBuilder";

if (typeof window !== "undefined") {
  (window as unknown as Record<string, unknown>).IvyTendrilWidgets = {
    TendrilProcessViewer,
    AgentViewer,
    DraftMarkdown,
    SortableVerificationList,
    ContentInput,
    BadgeSelect,
    WorkflowBuilder,
  };
}

export {
  TendrilProcessViewer,
  AgentViewer,
  DraftMarkdown,
  SortableVerificationList,
  ContentInput,
  BadgeSelect,
  WorkflowBuilder,
};
