import "./ChatWidget.testUtils";
import { render, screen, fireEvent } from "@testing-library/react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import "@testing-library/jest-dom";
import { ChatWidget, type ChatSessionDto } from "./ChatWidget";
import { setupChatWidgetTestEnvironment, questionsFence } from "./ChatWidget.testUtils";

describe("ChatWidget Interactive Question Draft Persistence", () => {
  beforeEach(() => {
    setupChatWidgetTestEnvironment();
  });

  const deployQuestion = questionsFence(
    "- id: deploy_target",
    "  title: Which environment should we deploy to?",
    "  options:",
    "    - title: Staging Environment",
    "      value: staging",
    "    - title: Production Environment",
    "      value: prod",
  );

  const deployQuestionWithOther = questionsFence(
    "- id: deploy_target",
    "  title: Which environment should we deploy to?",
    "  other: true",
    "  options:",
    "    - title: Staging Environment",
    "      value: staging",
    "    - title: Production Environment",
    "      value: prod",
  );

  const freeTextQuestion = questionsFence("- id: comment", "  title: Anything else?");

  const twoBlockMessage = [
    "First block:",
    questionsFence(
      "- id: color",
      "  title: Favorite color?",
      "  options:",
      "    - title: Red",
      "      value: red",
      "    - title: Blue",
      "      value: blue",
    ),
    "Second block:",
    questionsFence(
      "- id: size",
      "  title: Favorite size?",
      "  options:",
      "    - title: Small",
      "      value: small",
      "    - title: Large",
      "      value: large",
    ),
  ].join("\n\n");

  const sessionWith = (id: string, content: string): ChatSessionDto => ({
    id,
    title: id,
    agentId: "claude",
    modelId: "opus",
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    messages: [
      {
        id: `${id}-msg`,
        role: "assistant",
        content,
        timestamp: "12:00 PM",
      },
    ],
  });

  it("keeps a selection through a streamVersion re-render (streaming chunk arriving)", () => {
    const session = sessionWith("sess-stream", deployQuestion);

    const { rerender } = render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-stream"
        sessions={[session]}
        events={["OnAnswerQuestion"]}
      />,
    );

    fireEvent.click(screen.getByRole("radio", { name: /Staging Environment/i }));
    expect(screen.getByRole("radio", { name: /Staging Environment/i })).toBeChecked();

    rerender(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-stream"
        sessions={[session]}
        events={["OnAnswerQuestion"]}
        isStreaming
        streamingText='{"kind":"text","text":"Working on it...","delta":false}'
      />,
    );

    expect(screen.getByRole("radio", { name: /Staging Environment/i })).toBeChecked();
    expect(screen.getByRole("button", { name: /Submit Response/i })).not.toBeDisabled();
  });

  it("never remounts the callout's input across a streamVersion re-render", () => {
    const session = sessionWith("sess-remount", freeTextQuestion);

    const { rerender } = render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-remount"
        sessions={[session]}
        events={["OnAnswerQuestion"]}
      />,
    );

    const inputBefore = screen.getByPlaceholderText("Type your answer");

    rerender(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-remount"
        sessions={[session]}
        events={["OnAnswerQuestion"]}
        isStreaming
        streamingText='{"kind":"text","text":"Still working...","delta":false}'
      />,
    );

    expect(screen.getByPlaceholderText("Type your answer")).toBe(inputBefore);
  });

  it("keeps typed Other text and focus through a sessionVersion re-render (runningJobs change)", () => {
    const session = sessionWith("sess-other", deployQuestionWithOther);

    const { rerender } = render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-other"
        sessions={[session]}
        events={["OnAnswerQuestion"]}
      />,
    );

    fireEvent.click(screen.getByRole("radio", { name: /^Other$/i }));
    const otherInput = screen.getByPlaceholderText("Type your answer");
    otherInput.focus();
    fireEvent.change(otherInput, { target: { value: "A canary environment" } });
    expect(otherInput).toHaveValue("A canary environment");

    rerender(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-other"
        sessions={[session]}
        events={["OnAnswerQuestion"]}
        runningJobs={[{ id: "job-1", type: "ExecutePlan", status: "Running" }]}
      />,
    );

    const otherInputAfter = screen.getByPlaceholderText("Type your answer");
    expect(otherInputAfter).toBe(otherInput);
    expect(otherInputAfter).toHaveValue("A canary environment");
    expect(document.activeElement).toBe(otherInputAfter);
  });

  it("restores a selection from the draft store after a real remount (switching sessions and back)", () => {
    const sessionA = sessionWith("sess-a", deployQuestion);
    const sessionB = sessionWith("sess-b", "Just a plain message, no questions here.");

    const { rerender } = render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-a"
        sessions={[sessionA, sessionB]}
        events={["OnAnswerQuestion"]}
      />,
    );

    fireEvent.click(screen.getByRole("radio", { name: /Staging Environment/i }));
    expect(screen.getByRole("radio", { name: /Staging Environment/i })).toBeChecked();

    rerender(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-b"
        sessions={[sessionA, sessionB]}
        events={["OnAnswerQuestion"]}
      />,
    );
    expect(screen.queryByRole("radio", { name: /Staging Environment/i })).toBeNull();

    rerender(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-a"
        sessions={[sessionA, sessionB]}
        events={["OnAnswerQuestion"]}
      />,
    );

    expect(screen.getByRole("radio", { name: /Staging Environment/i })).toBeChecked();
  });

  it("clears the draft on submit, so a later remount starts empty rather than re-offering the old selection", () => {
    const sessionA = sessionWith("sess-a", deployQuestion);
    const sessionB = sessionWith("sess-b", "Just a plain message, no questions here.");
    const handleEvent = vi.fn();

    const { rerender } = render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-a"
        sessions={[sessionA, sessionB]}
        eventHandler={handleEvent}
        events={["OnAnswerQuestion"]}
      />,
    );

    fireEvent.click(screen.getByRole("radio", { name: /Staging Environment/i }));
    fireEvent.click(screen.getByRole("button", { name: /Submit Response/i }));
    expect(handleEvent).toHaveBeenCalled();

    // Switch away and back to force a remount of the message row.
    rerender(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-b"
        sessions={[sessionA, sessionB]}
        eventHandler={handleEvent}
        events={["OnAnswerQuestion"]}
      />,
    );
    rerender(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-a"
        sessions={[sessionA, sessionB]}
        eventHandler={handleEvent}
        events={["OnAnswerQuestion"]}
      />,
    );

    expect(screen.getByRole("radio", { name: /Staging Environment/i })).not.toBeChecked();
    expect(screen.getByRole("button", { name: /Submit Response/i })).toBeDisabled();
  });

  it("keeps two question blocks in one message independent when only one is answered", () => {
    const session = sessionWith("sess-two-blocks", twoBlockMessage);

    const { rerender } = render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-two-blocks"
        sessions={[session]}
        events={["OnAnswerQuestion"]}
      />,
    );

    fireEvent.click(screen.getByRole("radio", { name: /^Red$/i }));
    expect(screen.getByRole("radio", { name: /^Red$/i })).toBeChecked();
    expect(screen.getByRole("radio", { name: /^Small$/i })).not.toBeChecked();

    rerender(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-two-blocks"
        sessions={[session]}
        events={["OnAnswerQuestion"]}
        isStreaming
        streamingText='{"kind":"text","text":"Working...","delta":false}'
      />,
    );

    expect(screen.getByRole("radio", { name: /^Red$/i })).toBeChecked();
    expect(screen.getByRole("radio", { name: /^Small$/i })).not.toBeChecked();
  });

  it("keeps a selection made inside a rawStream-rendered questions block through a re-render", () => {
    const rawStream = JSON.stringify({ kind: "text", text: deployQuestion, delta: false });
    const session: ChatSessionDto = {
      id: "sess-raw",
      title: "Raw Stream Chat",
      agentId: "claude",
      modelId: "opus",
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      messages: [
        {
          id: "msg-raw",
          role: "assistant",
          content: "",
          rawStream,
          timestamp: "12:00 PM",
        },
      ],
    };

    const { rerender } = render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-raw"
        sessions={[session]}
        events={["OnAnswerQuestion"]}
      />,
    );

    fireEvent.click(screen.getByRole("radio", { name: /Staging Environment/i }));
    expect(screen.getByRole("radio", { name: /Staging Environment/i })).toBeChecked();

    rerender(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-raw"
        sessions={[session]}
        events={["OnAnswerQuestion"]}
        runningJobs={[{ id: "job-2", type: "ExecutePlan", status: "Running" }]}
      />,
    );

    expect(screen.getByRole("radio", { name: /Staging Environment/i })).toBeChecked();
  });
});
