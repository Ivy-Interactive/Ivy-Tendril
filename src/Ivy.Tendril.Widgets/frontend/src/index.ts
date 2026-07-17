import { TendrilProcessViewer } from "./TendrilProcessViewer";
import { AgentViewer } from "./AgentViewer";
import { DraftMarkdown } from "./DraftMarkdown";
import { SortableVerificationList } from "./SortableVerificationList";
import { TendrilCard } from "./TendrilCard";
import { ContentInput } from "./ContentInput/ContentInput";

if (typeof window !== "undefined") {
  (window as unknown as Record<string, unknown>).IvyTendrilWidgets = {
    TendrilProcessViewer,
    AgentViewer,
    DraftMarkdown,
    SortableVerificationList,
    TendrilCard,
    ContentInput,
  };
}

export {
  TendrilProcessViewer,
  AgentViewer,
  DraftMarkdown,
  SortableVerificationList,
  TendrilCard,
  ContentInput,
};
