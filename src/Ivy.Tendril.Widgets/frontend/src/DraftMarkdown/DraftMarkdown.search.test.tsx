import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, act } from "@testing-library/react";
import { DraftMarkdown } from "./DraftMarkdown";

describe("DraftMarkdown in-page search functionality", () => {
  beforeEach(() => {
    // Mock scrollIntoView in jsdom
    Element.prototype.scrollIntoView = vi.fn();
  });

  const sampleMarkdown = `
# Title of Document

This is the first paragraph with some keyword content to search.

## Section Header with keyword

Another paragraph mentioning keyword multiple times: keyword and KEYWORD.
`;

  it("opens search overlay and focuses search input when Ctrl+F or Cmd+F is pressed", () => {
    const { container } = render(<DraftMarkdown id="w1" content={sampleMarkdown} />);

    // Initially search overlay is not in the DOM
    expect(container.querySelector(".pmv-search-overlay")).toBeNull();

    // Trigger Ctrl+F
    act(() => {
      fireEvent.keyDown(document, { key: "f", ctrlKey: true });
    });

    const searchOverlay = container.querySelector(".pmv-search-overlay");
    expect(searchOverlay).not.toBeNull();

    const searchInput = screen.getByPlaceholderText("Find in document...");
    expect(searchInput).not.toBeNull();
    expect(document.activeElement).toBe(searchInput);
  });

  it("opens search overlay when Cmd+F is pressed (macOS shortcut)", () => {
    const { container } = render(<DraftMarkdown id="w1" content={sampleMarkdown} />);

    act(() => {
      fireEvent.keyDown(document, { key: "f", metaKey: true });
    });

    expect(container.querySelector(".pmv-search-overlay")).not.toBeNull();
  });

  it("highlights matching text elements and displays correct match counts when typing query", () => {
    const { container } = render(<DraftMarkdown id="w1" content={sampleMarkdown} />);

    // Open search overlay
    act(() => {
      fireEvent.keyDown(document, { key: "f", ctrlKey: true });
    });

    const searchInput = screen.getByPlaceholderText("Find in document...");

    // Type query "keyword"
    act(() => {
      fireEvent.change(searchInput, { target: { value: "keyword" } });
    });

    const highlights = container.querySelectorAll(".pmv-search-highlight");
    // "keyword" appears 5 times in sampleMarkdown (case-insensitive)
    expect(highlights.length).toBe(5);

    const counter = container.querySelector(".pmv-search-count");
    expect(counter?.textContent).toBe("1 of 5");

    // The first match should have the active highlight class
    expect(highlights[0].classList.contains("pmv-search-highlight--active")).toBe(true);
    expect(Element.prototype.scrollIntoView).toHaveBeenCalled();
  });

  it("displays 'No matches' when query does not match any content", () => {
    const { container } = render(<DraftMarkdown id="w1" content={sampleMarkdown} />);

    act(() => {
      fireEvent.keyDown(document, { key: "f", ctrlKey: true });
    });

    const searchInput = screen.getByPlaceholderText("Find in document...");

    act(() => {
      fireEvent.change(searchInput, { target: { value: "nonexistentterm" } });
    });

    const highlights = container.querySelectorAll(".pmv-search-highlight");
    expect(highlights.length).toBe(0);

    const counter = container.querySelector(".pmv-search-count");
    expect(counter?.textContent).toBe("No matches");
  });

  it("cycles through matching elements using Enter / Shift+Enter in search input", () => {
    const { container } = render(<DraftMarkdown id="w1" content={sampleMarkdown} />);

    act(() => {
      fireEvent.keyDown(document, { key: "f", ctrlKey: true });
    });

    const searchInput = screen.getByPlaceholderText("Find in document...");
    act(() => {
      fireEvent.change(searchInput, { target: { value: "keyword" } });
    });

    let highlights = container.querySelectorAll(".pmv-search-highlight");
    expect(highlights[0].classList.contains("pmv-search-highlight--active")).toBe(true);
    let counter = container.querySelector(".pmv-search-count");
    expect(counter?.textContent).toBe("1 of 5");

    // Press Enter to go to next match (index 1)
    act(() => {
      fireEvent.keyDown(searchInput, { key: "Enter" });
    });

    highlights = container.querySelectorAll(".pmv-search-highlight");
    expect(highlights[1].classList.contains("pmv-search-highlight--active")).toBe(true);
    expect(highlights[0].classList.contains("pmv-search-highlight--active")).toBe(false);
    counter = container.querySelector(".pmv-search-count");
    expect(counter?.textContent).toBe("2 of 5");

    // Press Shift+Enter to go to previous match (index 0)
    act(() => {
      fireEvent.keyDown(searchInput, { key: "Enter", shiftKey: true });
    });

    highlights = container.querySelectorAll(".pmv-search-highlight");
    expect(highlights[0].classList.contains("pmv-search-highlight--active")).toBe(true);
    counter = container.querySelector(".pmv-search-count");
    expect(counter?.textContent).toBe("1 of 5");

    // Press Shift+Enter again to wrap around to last match (index 4)
    act(() => {
      fireEvent.keyDown(searchInput, { key: "Enter", shiftKey: true });
    });

    highlights = container.querySelectorAll(".pmv-search-highlight");
    expect(highlights[4].classList.contains("pmv-search-highlight--active")).toBe(true);
    counter = container.querySelector(".pmv-search-count");
    expect(counter?.textContent).toBe("5 of 5");
  });

  it("cycles through matching elements using Next and Previous buttons", () => {
    const { container } = render(<DraftMarkdown id="w1" content={sampleMarkdown} />);

    act(() => {
      fireEvent.keyDown(document, { key: "f", ctrlKey: true });
    });

    const searchInput = screen.getByPlaceholderText("Find in document...");
    act(() => {
      fireEvent.change(searchInput, { target: { value: "keyword" } });
    });

    const nextBtn = screen.getByTitle("Next match (Enter)");
    const prevBtn = screen.getByTitle("Previous match (Shift+Enter)");

    // Click Next button
    act(() => {
      fireEvent.click(nextBtn);
    });

    let highlights = container.querySelectorAll(".pmv-search-highlight");
    expect(highlights[1].classList.contains("pmv-search-highlight--active")).toBe(true);
    let counter = container.querySelector(".pmv-search-count");
    expect(counter?.textContent).toBe("2 of 5");

    // Click Previous button
    act(() => {
      fireEvent.click(prevBtn);
    });

    highlights = container.querySelectorAll(".pmv-search-highlight");
    expect(highlights[0].classList.contains("pmv-search-highlight--active")).toBe(true);
    counter = container.querySelector(".pmv-search-count");
    expect(counter?.textContent).toBe("1 of 5");
  });

  it("closes search overlay and clears all highlight spans on Escape key in search input", () => {
    const { container } = render(<DraftMarkdown id="w1" content={sampleMarkdown} />);

    act(() => {
      fireEvent.keyDown(document, { key: "f", ctrlKey: true });
    });

    const searchInput = screen.getByPlaceholderText("Find in document...");
    act(() => {
      fireEvent.change(searchInput, { target: { value: "keyword" } });
    });

    expect(container.querySelectorAll(".pmv-search-highlight").length).toBe(5);

    // Press Escape
    act(() => {
      fireEvent.keyDown(searchInput, { key: "Escape" });
    });

    expect(container.querySelector(".pmv-search-overlay")).toBeNull();
    expect(container.querySelectorAll(".pmv-search-highlight").length).toBe(0);
  });

  it("closes search overlay and clears all highlight spans when clicking Close button", () => {
    const { container } = render(<DraftMarkdown id="w1" content={sampleMarkdown} />);

    act(() => {
      fireEvent.keyDown(document, { key: "f", ctrlKey: true });
    });

    const searchInput = screen.getByPlaceholderText("Find in document...");
    act(() => {
      fireEvent.change(searchInput, { target: { value: "keyword" } });
    });

    expect(container.querySelectorAll(".pmv-search-highlight").length).toBe(5);

    const closeBtn = screen.getByTitle("Close (Escape)");
    act(() => {
      fireEvent.click(closeBtn);
    });

    expect(container.querySelector(".pmv-search-overlay")).toBeNull();
    expect(container.querySelectorAll(".pmv-search-highlight").length).toBe(0);
  });

  it("renders search overlay inside sticky container above TOC content", () => {
    const { container } = render(
      <DraftMarkdown
        id="w1"
        content={sampleMarkdown}
        slots={{ StickyContent: [<div key="toc" className="sample-toc">TOC</div>] }}
      />,
    );

    act(() => {
      fireEvent.keyDown(document, { key: "f", ctrlKey: true });
    });

    const sticky = container.querySelector(".pmv-sticky");
    const searchOverlay = container.querySelector(".pmv-search-overlay");
    const toc = container.querySelector(".sample-toc");

    expect(sticky).not.toBeNull();
    expect(searchOverlay).not.toBeNull();
    expect(toc).not.toBeNull();
    expect(sticky?.contains(searchOverlay)).toBe(true);
    expect(sticky?.contains(toc)).toBe(true);
    // Search overlay precedes TOC in DOM order inside sticky container
    expect(searchOverlay?.compareDocumentPosition(toc!)).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
  });
});
