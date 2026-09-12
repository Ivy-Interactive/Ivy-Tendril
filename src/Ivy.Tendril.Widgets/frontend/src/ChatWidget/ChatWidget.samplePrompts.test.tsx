import { describe, it, expect, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ChatWidget } from "./ChatWidget";
import { setupChatWidgetTestEnvironment } from "./ChatWidget.test";
import type { ChatSamplePromptDto, ChatSessionDto } from "./types";

describe("ChatWidget - Sample Prompts", () => {
  beforeEach(() => {
    setupChatWidgetTestEnvironment();
  });

  it("renders one button per sample prompt in the empty state", () => {
    const samplePrompts: ChatSamplePromptDto[] = [
      { label: "Review plans", prompt: "Review all the plans waiting for review." },
      { label: "What's next?", prompt: "What should I work on next?" },
    ];

    render(
      <ChatWidget
        id="test-widget"
        headline="Test Headline"
        samplePrompts={samplePrompts}
        sessions={[]}
      />
    );

    const buttons = screen.getAllByRole("button");
    const promptButtons = buttons.filter((btn) => btn.className.includes("chat-sample-prompt"));

    expect(promptButtons).toHaveLength(2);
    expect(screen.getByText("Review plans")).toBeInTheDocument();
    expect(screen.getByText("What's next?")).toBeInTheDocument();
  });

  it("clicking a chip fills the textarea without sending", async () => {
    const user = userEvent.setup();
    const samplePrompts: ChatSamplePromptDto[] = [
      { label: "Review plans", prompt: "Review all the plans waiting for review." },
    ];

    const { container } = render(
      <ChatWidget
        id="test-widget"
        headline="Test Headline"
        samplePrompts={samplePrompts}
        sessions={[]}
      />
    );

    const promptButton = screen.getByText("Review plans");
    await user.click(promptButton);

    const textarea = container.querySelector("textarea");
    expect(textarea).toHaveValue("Review all the plans waiting for review.");
  });

  it("renders no container when samplePrompts is empty", () => {
    const { container } = render(
      <ChatWidget id="test-widget" headline="Test Headline" sessions={[]} />
    );

    const promptsContainer = container.querySelector(".chat-sample-prompts");
    expect(promptsContainer).not.toBeInTheDocument();
  });

  it("renders no chips once the session has messages", () => {
    const samplePrompts: ChatSamplePromptDto[] = [
      { label: "Review plans", prompt: "Review all the plans waiting for review." },
    ];

    const sessions: ChatSessionDto[] = [
      {
        id: "session1",
        title: "Test Session",
        agentId: "claude",
        modelId: "opus",
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        messages: [
          {
            id: "msg1",
            role: "user",
            content: "Hello",
            timestamp: new Date().toISOString(),
          },
        ],
      },
    ];

    const { container } = render(
      <ChatWidget
        id="test-widget"
        headline="Test Headline"
        samplePrompts={samplePrompts}
        sessions={sessions}
        activeSessionId="session1"
      />
    );

    const promptsContainer = container.querySelector(".chat-sample-prompts");
    expect(promptsContainer).not.toBeInTheDocument();
  });
});
