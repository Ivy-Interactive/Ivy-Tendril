import "./ChatWidget.testUtils";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import "@testing-library/jest-dom";
import { ChatWidget } from "./ChatWidget";
import { setupChatWidgetTestEnvironment } from "./ChatWidget.testUtils";

describe("ChatWidget voice input", () => {
  /** A Web Speech stub whose behaviour on start() the test decides. */
  class FakeRecognition {
    static instances: FakeRecognition[] = [];
    static onStart: ((recognition: FakeRecognition) => void) | null = null;

    continuous = false;
    interimResults = false;
    onstart: (() => void) | null = null;
    onend: (() => void) | null = null;
    onerror: ((event: { error?: string }) => void) | null = null;
    onresult: ((event: { results: ArrayLike<{ 0: { transcript: string } }> }) => void) | null =
      null;
    stopped = false;

    constructor() {
      FakeRecognition.instances.push(this);
    }

    start() {
      FakeRecognition.onStart?.(this);
    }

    stop() {
      this.stopped = true;
      this.onend?.();
    }

    emitResult(transcript: string) {
      this.onstart?.();
      this.onresult?.({ results: [{ 0: { transcript } }] });
    }

    emitError(error: string) {
      this.onerror?.({ error });
      // Real browsers always follow onerror with onend.
      this.onend?.();
    }
  }

  /** A WebSocket stub that records construction and lets the test push server messages. */
  class FakeWebSocket {
    static instances: FakeWebSocket[] = [];
    static readonly OPEN = 1;

    readonly OPEN = 1;
    readyState = 1;
    onopen: (() => void) | null = null;
    onmessage: ((event: { data: string }) => void) | null = null;
    onerror: (() => void) | null = null;
    onclose: ((event: { code: number; reason: string; wasClean: boolean }) => void) | null = null;
    sent: unknown[] = [];

    constructor(public url: string) {
      FakeWebSocket.instances.push(this);
    }

    send(data: unknown) {
      this.sent.push(data);
    }

    close() {
      this.readyState = 3;
    }

    emitServerMessage(payload: unknown) {
      this.onmessage?.({ data: JSON.stringify(payload) });
    }
  }

  const stubMediaDevices = (value: unknown) => {
    Object.defineProperty(global.navigator, "mediaDevices", { configurable: true, value });
  };

  const stubWorkingCaptureEnvironment = () => {
    stubMediaDevices({ getUserMedia: vi.fn().mockResolvedValue({ getTracks: () => [] }) });
    vi.stubGlobal("AudioWorkletNode", class {});
    vi.stubGlobal(
      "AudioContext",
      class {
        state = "running";
        audioWorklet = { addModule: vi.fn().mockResolvedValue(undefined) };
        close() {
          return Promise.resolve();
        }
        resume() {
          return Promise.resolve();
        }
        createMediaStreamSource() {
          return { connect: vi.fn() };
        }
      },
    );
  };

  const renderChat = () =>
    render(<ChatWidget id="test-chat" transcriptionUrl="ws://transcribe-test" />);

  const micButton = () => screen.getByRole("button", { name: /Voice input/i });
  const errorBanner = () => document.querySelector(".chat-voice-error");

  beforeEach(() => {
    setupChatWidgetTestEnvironment();

    FakeRecognition.instances = [];
    FakeRecognition.onStart = null;
    FakeWebSocket.instances = [];

    vi.stubGlobal("alert", vi.fn());
    vi.stubGlobal("WebSocket", FakeWebSocket);
    stubWorkingCaptureEnvironment();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    stubMediaDevices(undefined);
  });

  it("uses the Web Speech API alone when it produces results", async () => {
    FakeRecognition.onStart = (recognition) => recognition.emitResult("hello from web speech");
    vi.stubGlobal("SpeechRecognition", FakeRecognition);

    renderChat();
    fireEvent.click(micButton());

    const textarea = screen.getByPlaceholderText(/Ask/i) as HTMLTextAreaElement;
    await waitFor(() => expect(textarea.value).toBe("hello from web speech"));

    expect(FakeWebSocket.instances).toHaveLength(0);
    expect(window.alert).not.toHaveBeenCalled();
    expect(errorBanner()).toBeNull();
  });

  it("falls back to server transcription when Web Speech errors with 'network' before any result", async () => {
    FakeRecognition.onStart = (recognition) => recognition.emitError("network");
    vi.stubGlobal("SpeechRecognition", FakeRecognition);

    renderChat();
    fireEvent.click(micButton());

    await waitFor(() => expect(FakeWebSocket.instances).toHaveLength(1));
    expect(FakeWebSocket.instances[0].url).toBe("ws://transcribe-test");
    expect(window.alert).not.toHaveBeenCalled();
    expect(errorBanner()).toBeNull();
  });

  it("falls back to server transcription when Web Speech errors with 'service-not-allowed'", async () => {
    FakeRecognition.onStart = (recognition) => recognition.emitError("service-not-allowed");
    vi.stubGlobal("webkitSpeechRecognition", FakeRecognition);

    renderChat();
    fireEvent.click(micButton());

    await waitFor(() => expect(FakeWebSocket.instances).toHaveLength(1));
    expect(errorBanner()).toBeNull();
  });

  it("opens a WebSocket directly when no SpeechRecognition constructor exists", async () => {
    expect((window as any).SpeechRecognition).toBeUndefined();
    expect((window as any).webkitSpeechRecognition).toBeUndefined();

    renderChat();
    fireEvent.click(micButton());

    await waitFor(() => expect(FakeWebSocket.instances).toHaveLength(1));
    expect(FakeWebSocket.instances[0].url).toBe("ws://transcribe-test");
    expect(window.alert).not.toHaveBeenCalled();
  });

  it("shows the error banner and returns to idle when the fallback finds no mediaDevices", async () => {
    FakeRecognition.onStart = (recognition) => recognition.emitError("network");
    vi.stubGlobal("SpeechRecognition", FakeRecognition);
    stubMediaDevices(undefined);

    renderChat();
    const button = micButton();
    fireEvent.click(button);

    await waitFor(() =>
      expect(errorBanner()?.textContent).toContain("not available in this window"),
    );
    expect(FakeWebSocket.instances).toHaveLength(0);
    expect(button).toHaveClass("chat-voice-idle");
  });

  it("blames the connection when the page is not a secure context", async () => {
    FakeRecognition.onStart = (recognition) => recognition.emitError("network");
    vi.stubGlobal("SpeechRecognition", FakeRecognition);
    vi.stubGlobal("isSecureContext", false);
    vi.stubGlobal("location", { hostname: "192.168.1.42", href: "http://192.168.1.42:5000/" });

    renderChat();
    fireEvent.click(micButton());

    await waitFor(() => expect(errorBanner()?.textContent).toContain("secure connection"));
    expect(errorBanner()?.textContent).toContain("HTTPS");
    expect(FakeWebSocket.instances).toHaveLength(0);
  });

  it("reports a denied microphone without attempting the server fallback", async () => {
    FakeRecognition.onStart = (recognition) => recognition.emitError("not-allowed");
    vi.stubGlobal("SpeechRecognition", FakeRecognition);

    renderChat();
    const button = micButton();
    fireEvent.click(button);

    await waitFor(() =>
      expect(errorBanner()?.textContent).toContain("Microphone access was denied"),
    );
    expect(FakeWebSocket.instances).toHaveLength(0);
    expect(button).toHaveClass("chat-voice-idle");
  });

  it("appends a server transcript to text already in the composer", async () => {
    renderChat();

    const textarea = screen.getByPlaceholderText(/Ask/i) as HTMLTextAreaElement;
    fireEvent.change(textarea, { target: { value: "look into" } });

    fireEvent.click(micButton());
    await waitFor(() => expect(FakeWebSocket.instances).toHaveLength(1));

    const socket = FakeWebSocket.instances[0];
    socket.emitServerMessage({ type: "result", text: "the flaky test" });

    await waitFor(() => expect(textarea.value).toBe("look into the flaky test"));
  });

  it("dismisses the error banner when the close button is clicked", async () => {
    FakeRecognition.onStart = (recognition) => recognition.emitError("not-allowed");
    vi.stubGlobal("SpeechRecognition", FakeRecognition);

    renderChat();
    fireEvent.click(micButton());

    await waitFor(() => expect(errorBanner()).not.toBeNull());
    fireEvent.click(screen.getByRole("button", { name: /Dismiss voice input error/i }));
    expect(errorBanner()).toBeNull();
  });
});
