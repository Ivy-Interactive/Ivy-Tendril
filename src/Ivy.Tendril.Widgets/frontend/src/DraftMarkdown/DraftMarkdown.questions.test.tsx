import { describe, it, expect, vi } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { DraftMarkdown } from "./DraftMarkdown";

const renderContent = (content: string) => {
  const { container } = render(<DraftMarkdown id="w1" content={content} />);
  return container;
};

describe("DraftMarkdown questions callout", () => {
  it("renders a questions fence as a callout containing the fence text", () => {
    const content = "```questions\nShould the retry budget be per-request or per-session?\n```";
    const container = renderContent(content);

    const callout = container.querySelector(".pmv-questions");
    expect(callout).not.toBeNull();
    expect(callout?.textContent).toContain("Should the retry budget be per-request or per-session?");
  });

  it("renders no code block or Prism tokens, proving it bypassed Prism", () => {
    const content = "```questions\nWhat is the retention policy for read notifications?\n```";
    const container = renderContent(content);

    expect(container.querySelector(".pmv-code-block")).toBeNull();
    expect(container.querySelectorAll(".token").length).toBe(0);
  });

  it("still renders a regular js fence as a code block, so the dispatch does not regress", () => {
    const content = "```questions\nWhat about retries?\n```\n\n```js\nconst x = 1;\n```";
    const container = renderContent(content);

    expect(container.querySelector(".pmv-questions")).not.toBeNull();
    expect(container.querySelector(".pmv-code-block")).not.toBeNull();
  });

  it("keeps line breaks for multi-line fence content", () => {
    const content = "```questions\nFirst question?\nSecond question?\n```";
    const container = renderContent(content);

    const content_ = container.querySelector(".pmv-questions-content");
    expect(content_).not.toBeNull();
    expect(content_?.textContent).toBe("First question?\nSecond question?");
  });
});

// ─── Interactive picker ───────────────────────────────────────────────────

const fence = (...body: string[]) => ["```questions", ...body, "```"].join("\n");

const SINGLE = fence(
  "questions:",
  "  - id: budget",
  "    title: Should the retry budget be per-request or per-session?",
  "    options:",
  "      - title: Per request",
  "        value: per-request",
  "      - title: Per session",
  "        value: per-session",
  "        recommended: true",
);

const SINGLE_NO_OTHER = fence(
  "questions:",
  "  - id: budget",
  "    title: Retry budget scope?",
  "    other: false",
  "    options:",
  "      - title: Per request",
  "        value: per-request",
  "      - title: Per session",
  "        value: per-session",
);

const SINGLE_ANSWERED = fence(
  "questions:",
  "  - id: budget",
  "    title: Retry budget scope?",
  "    options:",
  "      - title: Per request",
  "        value: per-request",
  "      - title: Per session",
  "        value: per-session",
  "    answer: per-request",
);

const MULTI_ANSWERED = fence(
  "questions:",
  "  - id: channels",
  "    title: Which channels ship first?",
  "    multiple: true",
  "    options:",
  "      - title: In-app",
  "        value: in-app",
  "      - title: Email",
  "        value: email",
  "      - title: Push",
  "        value: push",
  "    answer:",
  "      - in-app",
);

const TWO_QUESTIONS = fence(
  "questions:",
  "  - id: naming",
  "    title: What should it be called?",
  "    header: Naming",
  "  - id: owner",
  "    title: Who owns the rollout?",
  "    header: Owner",
  "    answer: platform",
);

const renderInteractive = (content: string) => {
  const eventHandler = vi.fn();
  const { container } = render(
    <DraftMarkdown id="w1" content={content} events={["OnAnswersChange"]} eventHandler={eventHandler} />,
  );
  return { container, eventHandler };
};

const checks = (container: HTMLElement) =>
  Array.from(container.querySelectorAll<HTMLInputElement>(".pmv-question-check"));

const answerCalls = (eventHandler: ReturnType<typeof vi.fn>) =>
  eventHandler.mock.calls.map((call) => call[2][0]);

describe("DraftMarkdown interactive questions", () => {
  it("renders option rows rather than raw YAML when the host subscribes", () => {
    const { container } = renderInteractive(SINGLE);

    expect(container.querySelectorAll(".pmv-question-option").length).toBeGreaterThan(0);
    expect(container.querySelector(".pmv-questions-content")).toBeNull();
    expect(container.textContent).not.toContain("questions:");
    expect(container.textContent).toContain("Per request");
  });

  it("renders the static callout when the host does not subscribe", () => {
    const container = renderContent(SINGLE);

    expect(container.querySelector(".pmv-question-option")).toBeNull();
    expect(container.querySelector(".pmv-questions-content")?.textContent).toContain("questions:");
  });

  it("fires once with the question id and a one-element list when an option is clicked", () => {
    const { container, eventHandler } = renderInteractive(SINGLE);

    fireEvent.click(checks(container)[0]);

    expect(eventHandler).toHaveBeenCalledTimes(1);
    expect(eventHandler).toHaveBeenCalledWith("OnAnswersChange", "w1", [
      { QuestionId: "budget", Answer: ["per-request"] },
    ]);
  });

  it("replaces the previous value on a single-select question", () => {
    const { container, eventHandler } = renderInteractive(SINGLE_ANSWERED);

    fireEvent.click(checks(container)[1]);

    expect(answerCalls(eventHandler)).toEqual([{ QuestionId: "budget", Answer: ["per-session"] }]);
  });

  it("accumulates values on a multi-select question", () => {
    const { container, eventHandler } = renderInteractive(MULTI_ANSWERED);

    fireEvent.click(checks(container)[1]);

    expect(answerCalls(eventHandler)).toEqual([
      { QuestionId: "channels", Answer: ["in-app", "email"] },
    ]);
  });

  it("removes a value when an already-selected multi-select option is clicked", () => {
    const { container, eventHandler } = renderInteractive(MULTI_ANSWERED);

    fireEvent.click(checks(container)[0]);

    expect(answerCalls(eventHandler)).toEqual([{ QuestionId: "channels", Answer: [] }]);
  });

  it("fires the typed text when Other is chosen", () => {
    const { container, eventHandler } = renderInteractive(SINGLE);

    const otherRow = container.querySelector(".pmv-question-option--other");
    expect(otherRow).not.toBeNull();
    fireEvent.click(otherRow!.querySelector<HTMLInputElement>(".pmv-question-check")!);

    const input = container.querySelector<HTMLInputElement>(".pmv-question-other-input");
    expect(input).not.toBeNull();
    fireEvent.change(input!, { target: { value: "per-tenant" } });

    expect(answerCalls(eventHandler)).toEqual([{ QuestionId: "budget", Answer: ["per-tenant"] }]);
  });

  it("fires a null answer when Clear is used", () => {
    const { container, eventHandler } = renderInteractive(SINGLE);

    fireEvent.click(container.querySelector<HTMLButtonElement>(".pmv-question-clear")!);

    expect(answerCalls(eventHandler)).toEqual([{ QuestionId: "budget", Answer: null }]);
  });

  it("renders no Other row when other is false", () => {
    const { container } = renderInteractive(SINGLE_NO_OTHER);

    expect(container.querySelector(".pmv-question-option--other")).toBeNull();
    expect(container.querySelector(".pmv-question-other-input")).toBeNull();
    expect(container.querySelectorAll(".pmv-question-option")).toHaveLength(2);
  });

  it("renders a free-text input and no options for a question with no options", () => {
    const { container, eventHandler } = renderInteractive(
      fence("questions:", "  - id: name", "    title: What should it be called?"),
    );

    expect(container.querySelector(".pmv-question-option--other")).toBeNull();
    const input = container.querySelector<HTMLInputElement>(".pmv-question-other-input");
    expect(input).not.toBeNull();

    fireEvent.change(input!, { target: { value: "Notifier" } });
    expect(answerCalls(eventHandler)).toEqual([{ QuestionId: "name", Answer: ["Notifier"] }]);
  });

  it("marks the recommended option with a chip", () => {
    const { container } = renderInteractive(SINGLE);

    const chips = container.querySelectorAll(".pmv-question-option-recommended");
    expect(chips).toHaveLength(1);
    expect(chips[0].textContent).toBe("Recommended");
    expect(chips[0].closest(".pmv-question-option")?.textContent).toContain("Per session");
  });

  it("renders a tab per question, badged once answered", () => {
    const { container } = renderInteractive(TWO_QUESTIONS);

    const tabs = container.querySelectorAll(".pmv-questions-tab");
    expect(tabs).toHaveLength(2);
    expect(Array.from(tabs).map((t) => t.textContent?.trim())).toEqual(["Naming", "Owner"]);
    expect(container.querySelectorAll(".pmv-questions-tab-badge")).toHaveLength(1);
    expect(tabs[1].querySelector(".pmv-questions-tab-badge")).not.toBeNull();
  });

  it("switches the visible question when a tab is clicked", () => {
    const { container } = renderInteractive(TWO_QUESTIONS);

    expect(container.querySelector(".pmv-question-title")?.textContent).toBe(
      "What should it be called?",
    );

    fireEvent.click(container.querySelectorAll(".pmv-questions-tab")[1]);

    expect(container.querySelector(".pmv-question-title")?.textContent).toBe(
      "Who owns the rollout?",
    );
  });

  it("renders no tab strip for a single question", () => {
    const { container } = renderInteractive(SINGLE);

    expect(container.querySelector(".pmv-questions-tabs")).toBeNull();
  });

  it("keeps a legacy prose fence static next to a structured one", () => {
    const legacy = "```questions\nWhat is the retention policy?\n```";
    const { container } = renderInteractive(`${legacy}\n\n${SINGLE}`);

    const callouts = container.querySelectorAll(".pmv-questions");
    expect(callouts).toHaveLength(2);

    expect(callouts[0].querySelector(".pmv-questions-content")?.textContent).toBe(
      "What is the retention policy?",
    );
    expect(callouts[0].querySelector(".pmv-question-option")).toBeNull();
    expect(callouts[1].querySelector(".pmv-questions-content")).toBeNull();
    expect(callouts[1].querySelectorAll(".pmv-question-option").length).toBeGreaterThan(0);
  });

  it("gives each block its own radio group so two blocks do not interfere", () => {
    const { container } = renderInteractive(`${SINGLE}\n\n${SINGLE_NO_OTHER}`);

    const names = new Set(checks(container).map((input) => input.name));
    expect(names.size).toBe(2);
  });

  it("does not wrap the question UI in a pre", () => {
    const { container } = renderInteractive(SINGLE);

    expect(container.querySelector(".pmv-questions")).not.toBeNull();
    expect(container.querySelector("pre .pmv-questions")).toBeNull();
    expect(container.querySelector("pre")).toBeNull();
  });

  it("still wraps an ordinary fence in its code block after the pre override", () => {
    const { container } = renderInteractive("```js\nconst x = 1;\n```");

    expect(container.querySelector(".pmv-code-block")).not.toBeNull();
    expect(container.querySelector("pre")).not.toBeNull();
  });
});
