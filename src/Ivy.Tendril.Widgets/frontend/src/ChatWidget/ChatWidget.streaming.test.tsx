import "./ChatWidget.testUtils";
import { render, fireEvent } from "@testing-library/react";
import { describe, it, expect, vi } from "vitest";
import "@testing-library/jest-dom";
import { ChatWidget } from "./ChatWidget";

describe("ChatWidget Streaming Scroll Behavior", () => {
  const session = {
    id: "sess-scroll",
    title: "Scroll Test Chat",
    agentId: "codex",
    modelId: "gpt-5",
    createdAt: "2026-09-03T10:00:00Z",
    updatedAt: "2026-09-03T10:00:00Z",
    messages: [
      {
        id: "m-1",
        role: "user" as const,
        content: "Explain markdown streaming",
        timestamp: "10:00 AM",
      },
    ],
  };

  it("scrolls to bottom without triggering continuous smooth scroll calls when container is at bottom", () => {
    const scrollIntoViewMock = vi.fn();
    window.HTMLElement.prototype.scrollIntoView = scrollIntoViewMock;

    const { rerender } = render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-scroll"
        sessions={[session]}
        isStreaming={true}
        streamingText='{"type":"text_delta","content":"First chunk"}'
      />,
    );

    const container = document.querySelector(".chat-messages-container") as HTMLDivElement;
    expect(container).toBeInTheDocument();

    let currentScrollTop = 600;
    Object.defineProperty(container, "scrollHeight", {
      value: 1000,
      configurable: true,
      writable: true,
    });
    Object.defineProperty(container, "clientHeight", {
      value: 400,
      configurable: true,
      writable: true,
    });
    Object.defineProperty(container, "scrollTop", {
      get: () => currentScrollTop,
      set: (v: number) => {
        currentScrollTop = v;
      },
      configurable: true,
    });

    // Reset any initial scrollIntoView calls from mount
    scrollIntoViewMock.mockClear();

    // Stream next chunk
    rerender(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-scroll"
        sessions={[session]}
        isStreaming={true}
        streamingText='{"type":"text_delta","content":"First chunk and second chunk"}'
      />,
    );

    // Should update scrollTop to bottom (1000 - 400 = 600) instantly without calling scrollIntoView smooth
    expect(scrollIntoViewMock).not.toHaveBeenCalled();
    expect(currentScrollTop).toBe(600);
  });

  it("does not force scroll down when user is scrolled up", () => {
    const { rerender } = render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-scroll"
        sessions={[session]}
        isStreaming={true}
        streamingText='{"type":"text_delta","content":"Initial streaming"}'
      />,
    );

    const container = document.querySelector(".chat-messages-container") as HTMLDivElement;
    let currentScrollTop = 600;
    Object.defineProperty(container, "scrollHeight", {
      value: 1000,
      configurable: true,
      writable: true,
    });
    Object.defineProperty(container, "clientHeight", {
      value: 400,
      configurable: true,
      writable: true,
    });
    Object.defineProperty(container, "scrollTop", {
      get: () => currentScrollTop,
      set: (v: number) => {
        currentScrollTop = v;
      },
      configurable: true,
    });

    // User scrolls up to 200px (distance to bottom is 1000 - 200 - 400 = 400px > 50px threshold)
    currentScrollTop = 200;
    fireEvent.scroll(container);

    // New stream chunk arrives
    rerender(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-scroll"
        sessions={[session]}
        isStreaming={true}
        streamingText='{"type":"text_delta","content":"Initial streaming with more tokens"}'
      />,
    );

    // Container should remain at user scroll offset (200px) and not be forced down
    expect(currentScrollTop).toBe(200);
  });

  it("re-enables auto-scroll when user scrolls back to bottom", () => {
    const { rerender } = render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-scroll"
        sessions={[session]}
        isStreaming={true}
        streamingText='{"type":"text_delta","content":"Starting stream"}'
      />,
    );

    const container = document.querySelector(".chat-messages-container") as HTMLDivElement;
    let currentScrollTop = 600;
    let height = 1000;
    Object.defineProperty(container, "scrollHeight", {
      get: () => height,
      set: (v: number) => {
        height = v;
      },
      configurable: true,
    });
    Object.defineProperty(container, "clientHeight", {
      value: 400,
      configurable: true,
      writable: true,
    });
    Object.defineProperty(container, "scrollTop", {
      get: () => currentScrollTop,
      set: (v: number) => {
        currentScrollTop = v;
      },
      configurable: true,
    });

    // User scrolls up
    currentScrollTop = 150;
    fireEvent.scroll(container);

    // New chunk while scrolled up: stays at 150
    rerender(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-scroll"
        sessions={[session]}
        isStreaming={true}
        streamingText='{"type":"text_delta","content":"Starting stream..."}'
      />,
    );
    expect(currentScrollTop).toBe(150);

    // User scrolls back near bottom: 580px (1000 - 580 - 400 = 20px <= 50px threshold)
    currentScrollTop = 580;
    fireEvent.scroll(container);

    // Height increases with new tokens
    height = 1200;
    rerender(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-scroll"
        sessions={[session]}
        isStreaming={true}
        streamingText='{"type":"text_delta","content":"Starting stream... more tokens generated"}'
      />,
    );

    // Auto-scroll is re-enabled and pins to bottom (1200 - 400 = 800)
    expect(currentScrollTop).toBe(800);
  });
});
