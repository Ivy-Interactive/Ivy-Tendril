import { TendrilProcessViewer } from "./TendrilProcessViewer";
import { AgentViewer } from "./AgentViewer";
import { DraftMarkdown } from "./DraftMarkdown";
import { SortableVerificationList } from "./SortableVerificationList";
import { ContentInput } from "./ContentInput/ContentInput";
import { BadgeSelect } from "./BadgeSelect";
import { PlanDiffView } from "./PlanDiffView/PlanDiffView";
import { ChatWidget } from "./ChatWidget/ChatWidget";
import { WebViewer } from "./WebViewer";
import { TendrilShell } from "./Shell/TendrilShell";
import { ShellSidebarHeader } from "./Shell/ShellSidebarHeader";
import { ShellNewPlanButton } from "./Shell/ShellNewPlanButton";
import { ShellAgentButton } from "./Shell/ShellAgentButton";
import { ShellNav } from "./Shell/ShellNav";
import { ShellSidebarSection } from "./Shell/ShellSidebarSection";
import { ShellSettingsButton } from "./Shell/ShellSettingsButton";
import { ShellTabs } from "./Shell/ShellTabs";

if (typeof window !== "undefined") {
  (window as unknown as Record<string, unknown>).IvyTendrilWidgets = {
    TendrilProcessViewer,
    AgentViewer,
    DraftMarkdown,
    SortableVerificationList,
    ContentInput,
    BadgeSelect,
    PlanDiffView,
    ChatWidget,
    WebViewer,
    TendrilShell,
    ShellSidebarHeader,
    ShellNewPlanButton,
    ShellAgentButton,
    ShellNav,
    ShellSidebarSection,
    ShellSettingsButton,
    ShellTabs,
  };
}

export {
  TendrilProcessViewer,
  AgentViewer,
  DraftMarkdown,
  SortableVerificationList,
  ContentInput,
  BadgeSelect,
  PlanDiffView,
  ChatWidget,
  WebViewer,
  TendrilShell,
  ShellSidebarHeader,
  ShellNewPlanButton,
  ShellAgentButton,
  ShellNav,
  ShellSidebarSection,
  ShellSettingsButton,
  ShellTabs,
};
