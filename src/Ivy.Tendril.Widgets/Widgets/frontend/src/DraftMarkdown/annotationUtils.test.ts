import { describe, it, expect, beforeEach } from "vitest";
import { getPlainTextOffset, getPlainText } from "./annotationUtils";

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
