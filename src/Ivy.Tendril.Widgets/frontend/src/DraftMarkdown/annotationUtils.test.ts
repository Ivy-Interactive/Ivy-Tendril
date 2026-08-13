import { describe, it, expect, beforeEach } from "vitest";
import { getPlainTextOffset, getPlainText, rangeTouchesQuestions, applyAnnotationHighlights } from "./annotationUtils";
import type { MarkdownAnnotation } from "./annotationUtils";

describe("getPlainTextOffset", () => {
  let container: HTMLDivElement;

  beforeEach(() => {
    container = document.createElement("div");
    document.body.appendChild(container);
  });

  it("computes offset for a single text node", () => {
    container.textContent = "Hello world";
    const textNode = container.firstChild!;
    expect(getPlainTextOffset(container, textNode, 5)).toBe(5);
  });

  it("computes offset across nested elements", () => {
    container.innerHTML = "<p>Hello </p><p>world</p>";
    const secondTextNode = container.querySelector("p:nth-child(2)")!.firstChild!;
    expect(getPlainTextOffset(container, secondTextNode, 3)).toBe(9);
  });

  it("computes offset with inline formatting", () => {
    container.innerHTML = "<p>Hello <strong>bold</strong> text</p>";
    const lastTextNode = container.querySelector("p")!.lastChild!;
    expect(getPlainTextOffset(container, lastTextNode, 0)).toBe(10);
  });

  it("handles empty container", () => {
    container.innerHTML = "";
    expect(getPlainTextOffset(container, container, 0)).toBe(0);
  });
});

describe("getPlainText", () => {
  let container: HTMLDivElement;

  beforeEach(() => {
    container = document.createElement("div");
  });

  it("extracts plain text from nested HTML", () => {
    container.innerHTML = "<p>Hello <strong>bold</strong> world</p>";
    expect(getPlainText(container)).toBe("Hello bold world");
  });

  it("concatenates text across multiple paragraphs", () => {
    container.innerHTML = "<p>First</p><p>Second</p>";
    expect(getPlainText(container)).toBe("FirstSecond");
  });

  it("returns empty string for empty container", () => {
    expect(getPlainText(container)).toBe("");
  });
});

describe("rangeTouchesQuestions", () => {
  let container: HTMLDivElement;

  beforeEach(() => {
    container = document.createElement("div");
    document.body.appendChild(container);
    container.innerHTML =
      '<p>Some prose before.</p><div class="pmv-questions"><div class="pmv-questions-content">A question?</div></div><p>Some prose after.</p>';
  });

  it("returns true for a range fully inside a questions block", () => {
    const questionsText = container.querySelector(".pmv-questions-content")!.firstChild!;
    const range = document.createRange();
    range.setStart(questionsText, 0);
    range.setEnd(questionsText, 3);
    expect(rangeTouchesQuestions(container, range)).toBe(true);
  });

  it("returns true for a range that starts in prose and drags into a questions block", () => {
    const proseText = container.querySelector("p")!.firstChild!;
    const questionsText = container.querySelector(".pmv-questions-content")!.firstChild!;
    const range = document.createRange();
    range.setStart(proseText, 0);
    range.setEnd(questionsText, 3);
    expect(rangeTouchesQuestions(container, range)).toBe(true);
  });

  it("returns false for a prose-only range", () => {
    const firstP = container.querySelectorAll("p")[0].firstChild!;
    const secondP = container.querySelectorAll("p")[1].firstChild!;
    const range = document.createRange();
    range.setStart(firstP, 0);
    range.setEnd(firstP, 4);
    expect(rangeTouchesQuestions(container, range)).toBe(false);

    const rangeAfter = document.createRange();
    rangeAfter.setStart(secondP, 0);
    rangeAfter.setEnd(secondP, 4);
    expect(rangeTouchesQuestions(container, rangeAfter)).toBe(false);
  });
});

describe("applyAnnotationHighlights with questions blocks", () => {
  let container: HTMLDivElement;

  beforeEach(() => {
    container = document.createElement("div");
    document.body.appendChild(container);
  });

  it("creates no mark inside a .pmv-questions element", () => {
    container.innerHTML =
      '<p>Before text.</p><div class="pmv-questions"><div class="pmv-questions-content">A question here?</div></div>';
    const fullText = getPlainText(container);
    const annotation: MarkdownAnnotation = {
      id: "a1",
      startOffset: 0,
      endOffset: fullText.length,
      selectedText: fullText,
      comment: "spans everything",
    };

    applyAnnotationHighlights(container, [annotation]);

    const questionsBlock = container.querySelector(".pmv-questions")!;
    expect(questionsBlock.querySelector("mark[data-annotation-id]")).toBeNull();
  });

  it("keeps offsets stable for prose that follows a questions block", () => {
    // Without the guard, callout text would be skipped from highlighting but still
    // counted for offsets by getPlainTextOffset — this is the exact scenario that
    // regression (b) protects: an annotation on the trailing prose must land on the
    // same characters whether or not a questions block precedes it.
    container.innerHTML =
      '<p>Intro.</p><div class="pmv-questions"><div class="pmv-questions-content">A question?</div></div><p>Trailing prose to highlight.</p>';

    const fullText = getPlainText(container);
    const target = "Trailing prose";
    const startOffset = fullText.indexOf(target);
    const endOffset = startOffset + target.length;

    const annotation: MarkdownAnnotation = {
      id: "a2",
      startOffset,
      endOffset,
      selectedText: target,
      comment: "trailing",
    };

    applyAnnotationHighlights(container, [annotation]);

    const mark = container.querySelector("mark[data-annotation-id]");
    expect(mark).not.toBeNull();
    expect(mark?.textContent).toBe(target);
  });
});
