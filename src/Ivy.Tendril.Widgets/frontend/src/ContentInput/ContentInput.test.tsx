import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import "@testing-library/jest-dom";

vi.mock("pdfjs-dist", () => ({ GlobalWorkerOptions: {}, getDocument: vi.fn() }));
vi.mock("pdfjs-dist/build/pdf.worker.mjs?url", () => ({ default: "" }));

import { ContentInput } from "./ContentInput";

describe("ContentInput", () => {
  it("enables the submit button when only a file is attached (no text)", () => {
    render(<ContentInput id="civ-1" value=" [file: /tmp/foo.png]" />);
    const submitButton = screen.getByTitle("Send");
    expect(submitButton).toBeEnabled();
  });

  it("dispatches OnSubmit with a value containing the file reference when clicked", () => {
    const onIvyEvent = vi.fn();
    render(<ContentInput id="civ-1" value=" [file: /tmp/foo.png]" onIvyEvent={onIvyEvent} />);

    const submitButton = screen.getByTitle("Send");
    fireEvent.click(submitButton);

    expect(onIvyEvent).toHaveBeenCalledWith(
      "OnSubmit",
      "civ-1",
      expect.arrayContaining([
        expect.objectContaining({
          value: expect.stringContaining("[file: /tmp/foo.png]"),
        }),
      ])
    );
  });

  it("keeps the submit button disabled when there is no text and no file", () => {
    render(<ContentInput id="civ-1" value="" />);
    const submitButton = screen.getByTitle("Send");
    expect(submitButton).toBeDisabled();
  });

  it("renders the enter symbol (↵) in the shortcut label on non-Mac platforms", () => {
    render(<ContentInput id="civ-1" value="test" submitLabel="Send" />);
    const shortcut = document.querySelector(".civ-submit-shortcut");
    expect(shortcut).toBeTruthy();
    expect(shortcut?.textContent).toContain("↵");
  });

  it("does not overwrite the input text with incoming value prop while focused", () => {
    const { rerender } = render(<ContentInput id="civ-1" value="initial" autoFocus={true} />);
    const textarea = screen.getByPlaceholderText("How can I help you today?") as HTMLTextAreaElement;
    
    // Simulate user typing (which updates local state)
    fireEvent.change(textarea, { target: { value: "initial typed" } });
    
    // Simulate backend sending updated value (which includes typed text or whatever)
    rerender(<ContentInput id="civ-1" value="initial" autoFocus={true} />);
    expect(textarea.value).toBe("initial typed");
  });

  it("overwrites the input text with incoming value prop when not focused", () => {
    const { rerender } = render(<ContentInput id="civ-1" value="initial" autoFocus={false} />);
    const textarea = screen.getByPlaceholderText("How can I help you today?") as HTMLTextAreaElement;

    // Simulate backend sending updated value
    rerender(<ContentInput id="civ-1" value="updated" autoFocus={false} />);
    expect(textarea.value).toBe("updated");
  });

  it("shows an actionable error instead of a TypeError when navigator.mediaDevices is undefined", async () => {
    Object.defineProperty(global.navigator, "mediaDevices", {
      configurable: true,
      value: undefined,
    });

    const onIvyEvent = vi.fn();
    render(<ContentInput id="civ-1" value="" onIvyEvent={onIvyEvent} transcriptionEndpoint="ws://test" />);

    const micButton = screen.getByTitle("Voice input transcription");
    fireEvent.click(micButton);

    await vi.waitFor(() => {
      const errorBanner = document.querySelector(".civ-error-banner");
      expect(errorBanner?.textContent).toContain("not available in this window");
      expect(errorBanner?.textContent).not.toContain("TypeError");
    });
  });

  it("does not open a WebSocket when the mediaDevices API is missing", async () => {
    Object.defineProperty(global.navigator, "mediaDevices", {
      configurable: true,
      value: undefined,
    });

    const mockWebSocket = vi.fn();
    vi.stubGlobal("WebSocket", mockWebSocket);

    render(<ContentInput id="civ-1" value="" transcriptionEndpoint="ws://test" />);

    const micButton = screen.getByTitle("Voice input transcription");
    fireEvent.click(micButton);

    await vi.waitFor(() => {
      expect(mockWebSocket).not.toHaveBeenCalled();
    });
  });

  it("returns to idle status when the mediaDevices API is missing", async () => {
    Object.defineProperty(global.navigator, "mediaDevices", {
      configurable: true,
      value: undefined,
    });

    render(<ContentInput id="civ-1" value="" transcriptionEndpoint="ws://test" />);

    const micButton = screen.getByTitle("Voice input transcription");
    fireEvent.click(micButton);

    await vi.waitFor(() => {
      expect(micButton).toHaveClass("civ-status-idle");
      expect(micButton).not.toHaveClass("civ-status-connecting");
    });
  });

  it("reports a permission denial distinctly from an unsupported environment", async () => {
    Object.defineProperty(global.navigator, "mediaDevices", {
      configurable: true,
      value: {
        getUserMedia: vi.fn().mockRejectedValue(new DOMException("denied", "NotAllowedError")),
      },
    });

    render(<ContentInput id="civ-1" value="" transcriptionEndpoint="ws://test" />);

    const micButton = screen.getByTitle("Voice input transcription");
    fireEvent.click(micButton);

    await vi.waitFor(() => {
      const errorBanner = document.querySelector(".civ-error-banner");
      expect(errorBanner?.textContent).toContain("System Settings");
    });
  });
});
