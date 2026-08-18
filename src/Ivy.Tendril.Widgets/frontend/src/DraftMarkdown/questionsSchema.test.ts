import { describe, it, expect } from "vitest";
import {
  answerEntries,
  isSkipped,
  otherEntry,
  parseQuestions,
  questionLabel,
} from "./questionsSchema";

const body = (...lines: string[]) => lines.join("\n");

const SINGLE_SELECT = body(
  "questions:",
  "  - id: budget",
  "    title: Should the retry budget be per-request or per-session?",
  "    header: Retry scope",
  "    description: Affects how a burst of failures is absorbed.",
  "    other: false",
  "    options:",
  "      - title: Per request",
  "        description: Each call gets its **own** budget.",
  "        value: per-request",
  "      - title: Per session",
  "        value: per-session",
  "        recommended: true",
  "      - title: Both",
  "        value: both",
);

const MULTI_SELECT = body(
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
  "      - title: SMS",
  "        value: sms",
);

const FREE_TEXT = body(
  "questions:",
  "  - id: name",
  "    title: What should the feature be called?",
  "  - id: owner",
  "    title: Who owns the rollout?",
);

describe("parseQuestions shapes", () => {
  it("reads the single-select fixed set", () => {
    const parsed = parseQuestions(SINGLE_SELECT);

    expect(parsed.kind).toBe("questions");
    if (parsed.kind !== "questions") return;

    const [question] = parsed.questions;
    expect(question.id).toBe("budget");
    expect(question.header).toBe("Retry scope");
    expect(question.description).toBe("Affects how a burst of failures is absorbed.");
    expect(question.other).toBe(false);
    expect(question.multiple).toBe(false);
    expect(question.options).toHaveLength(3);
    expect(question.options?.[0].description).toBe("Each call gets its **own** budget.");
    expect(question.options?.[1].recommended).toBe(true);
    expect(question.options?.[0].recommended).toBe(false);
  });

  it("reads the multi-select open set", () => {
    const parsed = parseQuestions(MULTI_SELECT);

    expect(parsed.kind).toBe("questions");
    if (parsed.kind !== "questions") return;

    const [question] = parsed.questions;
    expect(question.multiple).toBe(true);
    expect(question.options).toHaveLength(4);
  });

  it("reads the pure free-text shape", () => {
    const parsed = parseQuestions(FREE_TEXT);

    expect(parsed.kind).toBe("questions");
    if (parsed.kind !== "questions") return;

    expect(parsed.questions).toHaveLength(2);
    expect(parsed.questions[0].options).toBeUndefined();
    expect(parsed.questions.map((q) => q.id)).toEqual(["name", "owner"]);
  });
});

describe("parseQuestions defaults", () => {
  it("treats other as true and multiple as false when absent", () => {
    const parsed = parseQuestions(MULTI_SELECT);

    expect(parsed.kind).toBe("questions");
    if (parsed.kind !== "questions") return;
    expect(parsed.questions[0].other).toBe(true);

    const free = parseQuestions(FREE_TEXT);
    if (free.kind !== "questions") return;
    expect(free.questions[0].other).toBe(true);
    expect(free.questions[0].multiple).toBe(false);
  });
});

describe("parseQuestions answer states", () => {
  const withAnswer = (line: string) =>
    parseQuestions(body("questions:", "  - id: q", "    title: T", line));

  it("distinguishes an absent answer from an explicit null", () => {
    const absent = parseQuestions(body("questions:", "  - id: q", "    title: T"));
    if (absent.kind !== "questions") throw new Error("expected questions");
    expect(absent.questions[0].answerPresent).toBe(false);
    expect(isSkipped(absent.questions[0])).toBe(false);
    expect(answerEntries(absent.questions[0])).toEqual([]);

    const skipped = withAnswer("    answer: null");
    if (skipped.kind !== "questions") throw new Error("expected questions");
    expect(skipped.questions[0].answerPresent).toBe(true);
    expect(isSkipped(skipped.questions[0])).toBe(true);
    expect(answerEntries(skipped.questions[0])).toEqual([]);
  });

  it("reads a scalar answer and a list answer", () => {
    const scalar = withAnswer("    answer: per-session");
    if (scalar.kind !== "questions") throw new Error("expected questions");
    expect(scalar.questions[0].answerPresent).toBe(true);
    expect(answerEntries(scalar.questions[0])).toEqual(["per-session"]);

    const list = parseQuestions(
      body("questions:", "  - id: q", "    title: T", "    answer:", "      - a", "      - b"),
    );
    if (list.kind !== "questions") throw new Error("expected questions");
    expect(answerEntries(list.questions[0])).toEqual(["a", "b"]);
  });

  it("reports the entry matching no option as the user's own text", () => {
    const parsed = parseQuestions(`${MULTI_SELECT}\n    answer:\n      - email\n      - carrier pigeon`);
    if (parsed.kind !== "questions") throw new Error("expected questions");

    expect(otherEntry(parsed.questions[0])).toBe("carrier pigeon");
  });

  it("reports no other entry when every entry is an option value", () => {
    const parsed = parseQuestions(`${MULTI_SELECT}\n    answer:\n      - email`);
    if (parsed.kind !== "questions") throw new Error("expected questions");

    expect(otherEntry(parsed.questions[0])).toBeUndefined();
  });
});

describe("parseQuestions tolerance", () => {
  it("reports a prose body as invalid rather than throwing", () => {
    expect(parseQuestions("Should the retry budget be per-request?\nAnd what about jitter?")).toEqual({
      kind: "invalid",
    });
  });

  it("reports valid YAML with no questions key as invalid", () => {
    expect(parseQuestions("title: not a question block\ncount: 3")).toEqual({ kind: "invalid" });
  });

  it("reports malformed YAML as invalid rather than throwing", () => {
    expect(parseQuestions("questions:\n  - id: [unclosed\n   bad: : :")).toEqual({ kind: "invalid" });
  });

  it("reports an empty body as invalid", () => {
    expect(parseQuestions("")).toEqual({ kind: "invalid" });
  });

  it("reports a missing id as invalid", () => {
    expect(parseQuestions(body("questions:", "  - title: No id here"))).toEqual({ kind: "invalid" });
    expect(parseQuestions(body("questions:", "  - id: ''", "    title: Empty id"))).toEqual({
      kind: "invalid",
    });
  });

  it("reports two questions sharing an id within the block as invalid", () => {
    const duplicate = body(
      "questions:",
      "  - id: same",
      "    title: First",
      "  - id: same",
      "    title: Second",
    );

    expect(parseQuestions(duplicate)).toEqual({ kind: "invalid" });
  });
});

describe("questionLabel", () => {
  it("prefers the header", () => {
    const parsed = parseQuestions(SINGLE_SELECT);
    if (parsed.kind !== "questions") throw new Error("expected questions");

    expect(questionLabel(parsed.questions[0], 0)).toBe("Retry scope");
  });

  it("falls back to the first few words of the title", () => {
    const parsed = parseQuestions(FREE_TEXT);
    if (parsed.kind !== "questions") throw new Error("expected questions");

    expect(questionLabel(parsed.questions[0], 0)).toBe("What should the");
  });

  it("falls back to the position when there is no title either", () => {
    const parsed = parseQuestions(body("questions:", "  - id: bare"));
    if (parsed.kind !== "questions") throw new Error("expected questions");

    expect(questionLabel(parsed.questions[0], 2)).toBe("Question 3");
  });
});
