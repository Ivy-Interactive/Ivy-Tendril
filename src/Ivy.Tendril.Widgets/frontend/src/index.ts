import { TendrilProcessViewer } from "./TendrilProcessViewer";
import { TendrilDashboard } from "./TendrilDashboard/TendrilDashboard";
import { withTooltipScope } from "./ui/withTooltipScope";
import { AgentViewer } from "./AgentViewer";
import { PlanMarkdown, DraftMarkdown } from "./PlanMarkdown";
import { SortableVerificationList } from "./SortableVerificationList";
import { ContentInput } from "./ContentInput/ContentInput";
import { BadgeSelect } from "./BadgeSelect";
import { PlanDiffView } from "./PlanDiffView/PlanDiffView";
import { ChatWidget as ChatWidgetBase } from "./ChatWidget/ChatWidget";
import { TerminalSessionHeader } from "./ChatWidget/TerminalSessionHeader";
import { WebViewer } from "./WebViewer";
import { TendrilShell as TendrilShellBase } from "./Shell/TendrilShell";
import { ShellSidebarHeader } from "./Shell/ShellSidebarHeader";
import { ShellNewPlanButton } from "./Shell/ShellNewPlanButton";
import { ShellAgentButton } from "./Shell/ShellAgentButton";
import { ShellNav } from "./Shell/ShellNav";
import { ShellSidebarSection } from "./Shell/ShellSidebarSection";
import { ShellSettingsButton } from "./Shell/ShellSettingsButton";
import { ShellTabs } from "./Shell/ShellTabs";
import { TendrilQuestions } from "./TendrilQuestions/TendrilQuestions";
import {
  TendrilBadge,
  TendrilIconButton,
  TendrilKbd,
  TendrilStatusLine,
  TendrilTooltip,
} from "./ui";

/* The two surfaces that host rows of tooltips own the scope their tooltips share; every other
   widget either sits inside one of them or brings its own, per tooltip. */
const ChatWidget = withTooltipScope(ChatWidgetBase);
const TendrilShell = withTooltipScope(TendrilShellBase);

if (typeof window !== "undefined") {
  (window as unknown as Record<string, unknown>).IvyTendrilWidgets = {
    TendrilProcessViewer,
    TendrilDashboard,
    AgentViewer,
    PlanMarkdown,
    DraftMarkdown,
    SortableVerificationList,
    ContentInput,
    BadgeSelect,
    PlanDiffView,
    ChatWidget,
    TerminalSessionHeader,
    WebViewer,
    TendrilShell,
    ShellSidebarHeader,
    ShellNewPlanButton,
    ShellAgentButton,
    ShellNav,
    ShellSidebarSection,
    ShellSettingsButton,
    ShellTabs,
    TendrilQuestions,
    TendrilTooltip,
    TendrilKbd,
    TendrilBadge,
    TendrilIconButton,
    TendrilStatusLine,
  };
}

export {
  TendrilProcessViewer,
  TendrilDashboard,
  AgentViewer,
  PlanMarkdown,
  DraftMarkdown,
  SortableVerificationList,
  ContentInput,
  BadgeSelect,
  PlanDiffView,
  ChatWidget,
  TerminalSessionHeader,
  WebViewer,
  TendrilShell,
  ShellSidebarHeader,
  ShellNewPlanButton,
  ShellAgentButton,
  ShellNav,
  ShellSidebarSection,
  ShellSettingsButton,
  ShellTabs,
  TendrilQuestions,
  TendrilTooltip,
  TendrilKbd,
  TendrilBadge,
  TendrilIconButton,
  TendrilStatusLine,
};
