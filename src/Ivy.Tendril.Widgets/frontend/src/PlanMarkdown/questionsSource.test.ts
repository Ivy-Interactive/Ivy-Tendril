import { describe, it, expect } from "vitest";
import { scanQuestionBlocks, setAnswer, tagQuestionBlocks } from "./questionsSource";
import { parseQuestions } from "./questionsSchema";

/** Builds a document without a trailing newline, so offsets stay easy to reason about. */
const doc = (...lines: string[]) => lines.join("\n");

const ONE_BLOCK = doc(
  "# Title",
  "",
  "```questions",
  "questions:",
  "  - id: q1",
  "    title: Pick one",
  "```",
  "",
  "Trailing prose.",
);

const TWO_BLOCKS = doc(
  "Intro prose.",
  "",
  "```questions",
  "questions:",
  "  - id: alpha",
  "    title: First",
  "```",
  "",
  "Middle prose.",
  "",
  "```questions",
  "questions:",
  "  - id: beta",
  "    title: Second",
  "  - id: gamma",
  "    title: Third",
  "```",
  "",
  "Outro prose.",
);

describe("scanQuestionBlocks", () => {
  it("finds a single block", () => {
    const blocks = scanQuestionBlocks(ONE_BLOCK);

    expect(blocks).toHaveLength(1);
    expect(blocks[0].index).toBe(0);
    expect(blocks[0].body).toBe("questions:\n  - id: q1\n    title: Pick one\n");
  });

  it("finds several blocks in document order", () => {
    const blocks = scanQuestionBlocks(TWO_BLOCKS);

    expect(blocks.map((b) => b.index)).toEqual([0, 1]);
    expect(blocks[0].body).toContain("alpha");
    expect(blocks[1].body).toContain("beta");
    expect(blocks[0].bodyEnd).toBeLessThan(blocks[1].bodyStart);
  });

  it("finds none in a document without any", () => {
    expect(scanQuestionBlocks("# Just prose\n\nAnd a paragraph.")).toEqual([]);
    expect(scanQuestionBlocks("")).toEqual([]);
  });

  it("bounds the body exactly, excluding both fence lines", () => {
    const [block] = scanQuestionBlocks(ONE_BLOCK);

    expect(ONE_BLOCK.slice(block.bodyStart, block.bodyEnd)).toBe(block.body);
    expect(block.body).not.toContain("```");
    // The character just before the body is the newline ending the opening fence line.
    expect(ONE_BLOCK.slice(0, block.bodyStart).endsWith("```questions\n")).toBe(true);
    expect(ONE_BLOCK.slice(block.bodyEnd).startsWith("```")).toBe(true);
  });

  it("treats a questions fence inside a four-backtick fence as documentation", () => {
    const md = doc("````", "```questions", "questions:", "  - id: nope", "```", "````");

    expect(scanQuestionBlocks(md)).toEqual([]);
  });

  it("finds a tilde fence", () => {
    const md = doc("~~~questions", "questions:", "  - id: t1", "~~~");
    const blocks = scanQuestionBlocks(md);

    expect(blocks).toHaveLength(1);
    expect(blocks[0].body).toBe("questions:\n  - id: t1\n");
  });

  it("ignores a questions info line inside an open tilde fence of greater run length", () => {
    const md = doc("~~~~", "~~~questions", "questions:", "  - id: no", "~~~", "~~~~");

    expect(scanQuestionBlocks(md)).toEqual([]);
  });

  it("does not let a tilde run close a backtick fence", () => {
    const md = doc("```questions", "questions:", "  - id: q1", "~~~", "```");
    const [block] = scanQuestionBlocks(md);

    expect(block.body).toBe("questions:\n  - id: q1\n~~~\n");
  });

  it("runs an unterminated fence to the end of the document", () => {
    const md = doc("```questions", "questions:", "  - id: q1");
    const [block] = scanQuestionBlocks(md);

    expect(block.bodyEnd).toBe(md.length);
    expect(block.body).toBe("questions:\n  - id: q1");
  });

  it("handles an empty body", () => {
    const [block] = scanQuestionBlocks(doc("```questions", "```"));

    expect(block.body).toBe("");
    expect(block.bodyStart).toBe(block.bodyEnd);
  });

  it("keeps offsets valid for CRLF input", () => {
    const md = ["```questions", "questions:", "  - id: q1", "```"].join("\r\n");
    const [block] = scanQuestionBlocks(md);

    expect(md.slice(block.bodyStart, block.bodyEnd)).toBe(block.body);
    expect(block.body).toBe("questions:\r\n  - id: q1\r\n");
  });
});

describe("tagQuestionBlocks", () => {
  it("stamps the index onto each top-level opening info line", () => {
    const tagged = tagQuestionBlocks(TWO_BLOCKS);

    expect(tagged).toContain("```questions_0");
    expect(tagged).toContain("```questions_1");
    expect(tagged).not.toMatch(/```questions\n/);
  });

  it("leaves a nested info line alone", () => {
    const md = doc("````", "```questions", "questions:", "  - id: nope", "```", "````");

    expect(tagQuestionBlocks(md)).toBe(md);
  });

  it("is a no-op on a document with no blocks", () => {
    const md = "# Title\n\nProse only.";

    expect(tagQuestionBlocks(md)).toBe(md);
  });

  it("changes nothing but the info word", () => {
    const tagged = tagQuestionBlocks(ONE_BLOCK);

    expect(tagged).toBe(ONE_BLOCK.replace("```questions", "```questions_0"));
  });

  it("preserves trailing info after the word", () => {
    const md = doc("```questions extra", "questions:", "  - id: q1", "```");

    expect(tagQuestionBlocks(md)).toContain("```questions_0 extra");
  });
});

describe("setAnswer", () => {
  it("writes a scalar answer", () => {
    const out = setAnswer(ONE_BLOCK, 0, "q1", "yes");

    expect(out).toContain("answer: yes");
  });

  it("writes a list answer", () => {
    const out = setAnswer(ONE_BLOCK, 0, "q1", ["a", "b"]);
    const [block] = scanQuestionBlocks(out);

    expect(block.body).toContain("- a");
    expect(block.body).toContain("- b");
  });

  it("writes an explicit null", () => {
    const out = setAnswer(ONE_BLOCK, 0, "q1", null);

    expect(out).toContain("answer: null");
  });

  it("removes the key when passed undefined", () => {
    const answered = setAnswer(ONE_BLOCK, 0, "q1", "yes");
    const cleared = setAnswer(answered, 0, "q1", undefined);

    expect(cleared).not.toContain("answer");
    expect(cleared).toBe(ONE_BLOCK);
  });

  it("addresses the question by id rather than position", () => {
    const out = setAnswer(TWO_BLOCKS, 1, "gamma", "third");
    const [, second] = scanQuestionBlocks(out);
    const parsed = parseQuestions(second.body);

    expect(parsed.kind).toBe("questions");
    if (parsed.kind !== "questions") return;
    expect(parsed.questions[0].answerPresent).toBe(false);
    expect(parsed.questions[1].id).toBe("gamma");
    expect(parsed.questions[1].answer).toBe("third");
  });

  it("throws when the question id matches nothing in the block", () => {
    expect(() => setAnswer(TWO_BLOCKS, 0, "gamma", "x")).toThrow(/no question with id "gamma"/);
  });

  it("throws when the block index is out of range", () => {
    expect(() => setAnswer(ONE_BLOCK, 4, "q1", "x")).toThrow(/no questions block at index 4/);
  });

  it("leaves the other block and all prose byte-identical", () => {
    const out = setAnswer(TWO_BLOCKS, 1, "beta", "second");

    const before = scanQuestionBlocks(TWO_BLOCKS);
    const after = scanQuestionBlocks(out);
    expect(after[0].body).toBe(before[0].body);

    // Everything up to the second block's body is untouched, as is everything after it.
    expect(out.slice(0, after[1].bodyStart)).toBe(TWO_BLOCKS.slice(0, before[1].bodyStart));
    expect(out.slice(after[1].bodyEnd)).toBe(TWO_BLOCKS.slice(before[1].bodyEnd));
    expect(out).toContain("Intro prose.");
    expect(out).toContain("Middle prose.");
    expect(out).toContain("Outro prose.");
  });

  it("preserves a comment and the original key order", () => {
    const md = doc(
      "```questions",
      "questions:",
      "  # why we ask this",
      "  - id: alpha",
      "    title: First",
      "    other: false",
      "```",
    );

    const out = setAnswer(md, 0, "alpha", "picked");

    expect(out).toContain("# why we ask this");
    expect(out.indexOf("title: First")).toBeLessThan(out.indexOf("other: false"));
  });

  it("round-trips: set, re-scan, parse yields the written answer", () => {
    const out = setAnswer(ONE_BLOCK, 0, "q1", ["one", "two"]);
    const [block] = scanQuestionBlocks(out);
    const parsed = parseQuestions(block.body);

    expect(parsed.kind).toBe("questions");
    if (parsed.kind !== "questions") return;
    expect(parsed.questions[0].answerPresent).toBe(true);
    expect(parsed.questions[0].answer).toEqual(["one", "two"]);
  });

  it("does not fold a long answer onto a second line", () => {
    const long = "a".repeat(200);
    const out = setAnswer(ONE_BLOCK, 0, "q1", long);

    expect(out).toContain(`answer: ${long}`);
  });

  it("keeps the fence's own indentation", () => {
    const md = doc(
      "- item:",
      "",
      "  ```questions",
      "  questions:",
      "    - id: ind",
      "      title: Indented",
      "  ```",
    );

    const out = setAnswer(md, 0, "ind", "yes");

    expect(out).toContain("  questions:");
    expect(out).toContain("      answer: yes");
    expect(out).toContain("- item:");
  });
});

describe("setAnswer on wrapper-less shapes", () => {
  // Every shape the reader renders as a picker has to be writable here, or answering it throws.

  const BARE_SEQUENCE = doc(
    "```questions",
    "- id: caching-strategy",
    "  title: Which caching strategy?",
    "- id: eviction",
    "  title: How should entries expire?",
    "```",
  );

  const SINGLE_QUESTION = doc(
    "```questions",
    "id: confirmation-prompt",
    "title: Should we prompt before deleting?",
    "```",
  );

  it("writes an answer into a bare sequence", () => {
    const updated = setAnswer(BARE_SEQUENCE, 0, "caching-strategy", "redis");

    const parsed = parseQuestions(scanQuestionBlocks(updated)[0].body);
    expect(parsed.kind).toBe("questions");
    if (parsed.kind !== "questions") return;
    expect(parsed.questions[0].answer).toBe("redis");
    // The question it was not about keeps its shape.
    expect(parsed.questions[1].answerPresent).toBe(false);
  });

  it("writes an answer into a single question written without any wrapper", () => {
    const updated = setAnswer(SINGLE_QUESTION, 0, "confirmation-prompt", ["prompt"]);

    const parsed = parseQuestions(scanQuestionBlocks(updated)[0].body);
    expect(parsed.kind).toBe("questions");
    if (parsed.kind !== "questions") return;
    expect(parsed.questions[0].answer).toEqual(["prompt"]);
  });

  it("deletes an answer in a wrapper-less block", () => {
    const answered = setAnswer(BARE_SEQUENCE, 0, "eviction", "after-a-day");
    const cleared = setAnswer(answered, 0, "eviction", undefined);

    expect(cleared).not.toContain("answer:");
    expect(cleared).toContain("id: eviction");
  });

  it("throws for an id no shape in the block carries", () => {
    expect(() => setAnswer(BARE_SEQUENCE, 0, "nope", "x")).toThrow(/no question with id "nope"/);
    expect(() => setAnswer(SINGLE_QUESTION, 0, "nope", "x")).toThrow(/no question with id "nope"/);
  });
});
