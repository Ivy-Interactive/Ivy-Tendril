import { describe, it, expect } from "vitest";
import { formatSystemEvent } from "./systemEvents";

describe("formatSystemEvent", () => {
  it("condenses a completed job into a verb and a plan reference", () => {
    const view = formatSystemEvent(
      "[System Event] Job 00148 (ExecutePlan) for '00059: Add dark mode toggle' has finished with status: Completed (Completed successfully). Please inspect the outcome.",
    );
    expect(view).toEqual({
      kind: "completed",
      text: "Completed plan",
      plan: { id: "00059", label: "#59 Add dark mode toggle" },
      detail: undefined,
    });
  });

  it("keeps the failure summary of a failed job as its detail", () => {
    const view = formatSystemEvent(
      "[System Event] Job 00150 (CreatePr) for '00060: Ship it' has finished with status: Failed (git push rejected). Please inspect.",
    );
    expect(view.kind).toBe("failed");
    expect(view.text).toBe("Failed pull request");
    expect(view.plan?.label).toBe("#60 Ship it");
    expect(view.detail).toBe("git push rejected");
  });

  it("quotes the job's subject when it names no plan id", () => {
    const view = formatSystemEvent("[System Event] Job 7 (Custom) for 'nightly sync' has finished with status: Timeout.");
    expect(view).toEqual({ kind: "failed", text: "Timed out Custom 'nightly sync'", detail: undefined });
  });

  it("reads a manual approval as the plan starting", () => {
    const view = formatSystemEvent(
      "[System Event] Manual approval granted and execution started for plan 'Revamp auth' (Job 00151).",
    );
    expect(view).toEqual({ kind: "started", text: "Started plan 'Revamp auth'" });
  });

  it("passes any other text through without the prefix", () => {
    expect(formatSystemEvent("[System Event] Something else happened.")).toEqual({
      kind: "info",
      text: "Something else happened.",
    });
    expect(formatSystemEvent("Job 00148 (CreatePlan) has completed successfully.")).toEqual({
      kind: "info",
      text: "Job 00148 (CreatePlan) has completed successfully.",
    });
  });
});
