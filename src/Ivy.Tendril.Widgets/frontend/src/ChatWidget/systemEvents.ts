export type SystemEventKind = "completed" | "failed" | "started" | "info";

export interface SystemEventPlanRef {
  /** The plan id as the job reported it, e.g. "00059"; what OnOpenPlan receives. */
  id: string;
  /** Short display form, e.g. "#59 Add dark mode toggle". */
  label: string;
}

export interface SystemEventView {
  kind: SystemEventKind;
  /** The sentence up to the plan reference, when there is one. */
  text: string;
  plan?: SystemEventPlanRef;
  /** A trailing detail, such as the failure summary. */
  detail?: string;
}

const PREFIX = /^\s*\[System Event\]\s*/i;
const FINISHED = /^Job\s+(\S+)\s+\(([^)]+)\)\s+for\s+'(.+?)'\s+has finished with status:\s*(\w+)(?:\s*\((.*?)\))?/i;
const APPROVED = /^Manual approval granted and execution started for plan '(.+?)'\s*\(Job\s+(\S+)\)/i;
const PLAN_INFO = /^(\d+)\s*:\s*(.+)$/;

const planRef = (info: string): SystemEventPlanRef | undefined => {
  const match = PLAN_INFO.exec(info.trim());
  if (!match) return undefined;
  return { id: match[1], label: `#${parseInt(match[1], 10)} ${match[2].trim()}` };
};

const jobNoun = (type: string): string => {
  const lower = type.toLowerCase();
  if (lower.includes("pr") || lower.includes("pullrequest")) return "pull request";
  if (lower.includes("plan")) return "plan";
  return type;
};

const statusVerb = (status: string): { verb: string; kind: SystemEventKind } => {
  switch (status.toLowerCase()) {
    case "completed":
      return { verb: "Completed", kind: "completed" };
    case "failed":
      return { verb: "Failed", kind: "failed" };
    case "timeout":
      return { verb: "Timed out", kind: "failed" };
    case "cancelled":
    case "canceled":
      return { verb: "Cancelled", kind: "info" };
    default:
      return { verb: status, kind: "info" };
  }
};

/**
 * Condenses the system events the backend posts into a conversation (job completions, manual
 * approvals) into the one line the timeline shows. The trailing instructions those messages carry
 * are addressed to the agent, not the reader, and are dropped.
 */
export function formatSystemEvent(content: string): SystemEventView {
  const body = content.replace(PREFIX, "").trim();

  const finished = FINISHED.exec(body);
  if (finished) {
    const [, , type, info, status, summary] = finished;
    const { verb, kind } = statusVerb(status);
    const plan = planRef(info);
    const text = `${verb} ${jobNoun(type)}`;
    const detail = kind === "failed" && summary && summary.toLowerCase() !== status.toLowerCase() ? summary : undefined;
    return plan ? { kind, text, plan, detail } : { kind, text: `${text} '${info}'`, detail };
  }

  const approved = APPROVED.exec(body);
  if (approved) {
    return { kind: "started", text: `Started plan '${approved[1]}'` };
  }

  return { kind: "info", text: body };
}
