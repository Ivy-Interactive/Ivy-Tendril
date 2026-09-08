import { render, screen, fireEvent, within } from "@testing-library/react";
import { describe, it, expect, vi } from "vitest";
import "@testing-library/jest-dom";
import { TendrilQuestions } from "./TendrilQuestions";
import { QuestionsForm } from "./QuestionsForm";
import { parseQuestions } from "../PlanMarkdown/questionsSchema";
import { buildAnswersSummary } from "./answers";

const yaml = (...lines: string[]) => lines.join("\n");

const singleSelect = yaml(
  "- id: proceed",
  "  title: How should we proceed?",
  "  other: false",
  "  options:",
  "    - title: Open a PR",
  "      description: Open a new Pull Request against development branch.",
  "      value: pr",
  "      recommended: true",
  "    - title: Review the diff first",
  "      description: Stop the process and wait for my review first.",
  "      value: review",
);

const multiSelect = yaml(
  "- id: checks",
  "  title: Which checks should run?",
  "  multiple: true",
  "  options:",
  "    - title: Lint",
  "      value: lint",
  "    - title: Build",
  "      value: build",
  "    - title: Test",
  "      value: test",
);

const withOther = yaml(
  "- id: env",
  "  header: Deployment",
  "  title: Which environment?",
  "  description: Pick the target for the first rollout.",
  "  options:",
  "    - title: Staging",
  "      value: staging",
  "    - title: Production",
  "      value: prod",
);

const freeText = yaml("- id: notes", "  title: Anything else?", "  optional: true");

const answered = yaml(
  "- id: proceed",
  "  title: How should we proceed?",
  "  options:",
  "    - title: Open a PR",
  "      value: pr",
  "    - title: Review the diff first",
  "      value: review",
  "  answer: review",
  "- id: notes",
  "  title: Anything else?",
  "  optional: true",
);

const questionsOf = (content: string) => {
  const parsed = parseQuestions(content);
  if (parsed.kind !== "questions") throw new Error("expected questions");
  return parsed.questions;
};

describe("TendrilQuestions widget", () => {
  it("renders single-select option cards with descriptions and the recommended badge", () => {
    render(<TendrilQuestions id="q" eventHandler={vi.fn()} content={singleSelect} />);

    expect(screen.getByText("How should we proceed?")).toBeInTheDocument();
    expect(screen.getByText("Open a new Pull Request against development branch.")).toBeInTheDocument();
    expect(screen.getByText("Recommended")).toBeInTheDocument();
    expect(screen.getAllByRole("radio")).toHaveLength(2);
    expect(screen.queryByRole("radio", { name: /^Other$/i })).not.toBeInTheDocument();
  });

  it("selects an option on card click and reports it through OnAnswer", () => {
    const handler = vi.fn();
    render(<TendrilQuestions id="q" events={["OnAnswer"]} eventHandler={handler} content={singleSelect} />);

    fireEvent.click(screen.getByText("Review the diff first"));

    expect(handler).toHaveBeenCalledWith("OnAnswer", "q", [{ questionId: "proceed", values: ["review"] }]);
    const card = screen.getByText("Review the diff first").closest(".tq-option");
    expect(card).toHaveAttribute("data-selected", "true");
    expect(screen.getByRole("radio", { name: /Review the diff first/i })).toBeChecked();
  });

  it("toggles multi-select options and keeps every selected entry in the answer", () => {
    const handler = vi.fn();
    render(<TendrilQuestions id="q" events={["OnAnswer"]} eventHandler={handler} content={multiSelect} />);

    expect(screen.getByText("Select all that apply")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("checkbox", { name: /Lint/i }));
    fireEvent.click(screen.getByRole("checkbox", { name: /Test/i }));
    expect(handler).toHaveBeenLastCalledWith("OnAnswer", "q", [{ questionId: "checks", values: ["lint", "test"] }]);

    fireEvent.click(screen.getByRole("checkbox", { name: /Lint/i }));
    expect(handler).toHaveBeenLastCalledWith("OnAnswer", "q", [{ questionId: "checks", values: ["test"] }]);
  });

  it("opens the Other field and reports the typed text as the answer", () => {
    const handler = vi.fn();
    render(<TendrilQuestions id="q" events={["OnAnswer"]} eventHandler={handler} content={withOther} />);

    expect(screen.getByText("Deployment")).toBeInTheDocument();
    expect(screen.getByText("Pick the target for the first rollout.")).toBeInTheDocument();
    expect(screen.queryByPlaceholderText("Type your answer")).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("radio", { name: /^Other$/i }));
    const input = screen.getByPlaceholderText("Type your answer");
    fireEvent.change(input, { target: { value: "canary" } });

    expect(handler).toHaveBeenLastCalledWith("OnAnswer", "q", [{ questionId: "env", values: ["canary"] }]);

    fireEvent.click(screen.getByRole("radio", { name: /Staging/i }));
    expect(handler).toHaveBeenLastCalledWith("OnAnswer", "q", [{ questionId: "env", values: ["staging"] }]);
    expect(screen.queryByPlaceholderText("Type your answer")).not.toBeInTheDocument();
  });

  it("renders a free-text question with the optional tag and reports typing", () => {
    const handler = vi.fn();
    render(<TendrilQuestions id="q" events={["OnAnswer"]} eventHandler={handler} content={freeText} />);

    expect(screen.getByText("Optional")).toBeInTheDocument();
    fireEvent.change(screen.getByPlaceholderText("Type your answer"), { target: { value: "ship it" } });
    expect(handler).toHaveBeenLastCalledWith("OnAnswer", "q", [{ questionId: "notes", values: ["ship it"] }]);
  });

  it("submits every answer with a markdown summary and clears on request", () => {
    const handler = vi.fn();
    render(
      <TendrilQuestions
        id="q"
        events={["OnAnswer", "OnSubmit"]}
        eventHandler={handler}
        content={singleSelect}
        showSubmit
        submitLabel="Send"
      />,
    );

    const submit = screen.getByRole("button", { name: "Send" });
    expect(submit).toBeDisabled();

    fireEvent.click(screen.getByRole("radio", { name: /Open a PR/i }));
    expect(submit).not.toBeDisabled();
    fireEvent.click(submit);

    expect(handler).toHaveBeenCalledWith("OnSubmit", "q", [
      { answers: { proceed: ["pr"] }, summary: "Answers:\n- **How should we proceed?**: Open a PR" },
    ]);

    fireEvent.click(screen.getByRole("button", { name: /Clear answer/i }));
    expect(handler).toHaveBeenLastCalledWith("OnAnswer", "q", [{ questionId: "proceed", values: [] }]);
    expect(submit).toBeDisabled();
  });

  it("presents settled answers read-only, naming the option and the unanswered optional one", () => {
    render(<TendrilQuestions id="q" eventHandler={vi.fn()} content={answered} readOnly />);

    expect(screen.queryByRole("radio")).not.toBeInTheDocument();
    expect(screen.getByText("Review the diff first")).toHaveClass("tq-answer-value");
    expect(screen.getByText("Not answered (not required)")).toBeInTheDocument();
  });

  it("falls back to the raw text when the body is not a questions block", () => {
    render(<TendrilQuestions id="q" eventHandler={vi.fn()} content={"Just a note for the reader"} />);
    expect(screen.getByText("Just a note for the reader")).toHaveClass("tq-static");
  });
});

describe("QuestionsForm", () => {
  it("stacks several questions and marks the selected single-select card with a check", () => {
    const onAnswer = vi.fn();
    const questions = [...questionsOf(singleSelect), ...questionsOf(freeText)];
    const { container } = render(
      <QuestionsForm questions={questions} answers={{ proceed: ["pr"] }} onAnswer={onAnswer} />,
    );

    expect(container.querySelectorAll(".tq-question")).toHaveLength(2);
    const selected = screen.getByText("Open a PR").closest(".tq-option") as HTMLElement;
    expect(selected).toHaveAttribute("data-selected", "true");
    expect(within(selected).getByRole("radio")).toBeChecked();
    expect(selected.querySelector(".tq-option-check")).toBeInTheDocument();
  });

  it("summarises skipped optional questions and typed answers", () => {
    const questions = [...questionsOf(withOther), ...questionsOf(freeText)];
    expect(buildAnswersSummary(questions, { env: ["canary"] })).toBe(
      "Answers:\n- **Which environment?**: canary\n- **Anything else?**: *(skipped)*",
    );
  });
});
