import { describe, it, expect, beforeEach } from "vitest";
import { getPlainTextOffset, getPlainText, createRangeFromOffsets } from "./annotationUtils";

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

describe("createRangeFromOffsets", () => {
  let container: HTMLDivElement;

  beforeEach(() => {
    container = document.createElement("div");
    document.body.appendChild(container);
  });

  it("builds a range within a single text node", () => {
    container.textContent = "Hello world";
    const range = createRangeFromOffsets(container, 0, 5);
    expect(range?.toString()).toBe("Hello");
  });

  it("builds a range spanning nested elements", () => {
    container.innerHTML = "<p>Hello </p><p>world</p>";
    const range = createRangeFromOffsets(container, 0, 11);
    expect(range?.toString()).toBe("Hello world");
  });

  it("builds a range spanning inline formatting", () => {
    container.innerHTML = "<p>Hello <strong>bold</strong> text</p>";
    const range = createRangeFromOffsets(container, 6, 10);
    expect(range?.toString()).toBe("bold");
  });

  it("builds a range spanning an existing annotation mark", () => {
    container.innerHTML = 'Hello <mark data-annotation-id="a1">bold</mark> text';
    const range = createRangeFromOffsets(container, 0, 15);
    expect(range?.toString()).toBe("Hello bold text");
  });

  it("returns null for an empty container", () => {
    container.innerHTML = "";
    expect(createRangeFromOffsets(container, 0, 5)).toBeNull();
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
