import { debugLog } from "./debug-log";

export type VoiceStatus = "idle" | "connecting" | "recording" | "processing";

export const INSECURE_CONTEXT_ERROR =
  "Voice input needs a secure connection. Open Tendril over HTTPS or on localhost, then try again.";

export const MEDIA_DEVICES_UNAVAILABLE_ERROR =
  "Voice input is not available in this window. On macOS, quit and reopen Ivy Tendril after updating, then allow microphone access when prompted. You can also run 'tendril --web' and use voice input in your browser.";

export const AUDIO_CAPTURE_UNSUPPORTED_ERROR =
  "This browser cannot capture audio for voice input.";

/**
 * Returns the reason voice capture cannot work in this environment, or null when it can.
 * Ordered most-specific-cause first so the message names the actual fix.
 */
export function unsupportedEnvironmentError(): string | null {
  const hostname = typeof window !== "undefined" ? window.location?.hostname : undefined;
  const isLoopback = hostname === "localhost" || hostname === "127.0.0.1" || hostname === "[::1]";
  if (typeof window !== "undefined" && window.isSecureContext === false && !isLoopback) {
    return INSECURE_CONTEXT_ERROR;
  }

  if (typeof navigator === "undefined" || !navigator.mediaDevices || typeof navigator.mediaDevices.getUserMedia !== "function") {
    return MEDIA_DEVICES_UNAVAILABLE_ERROR;
  }

  if (typeof AudioWorkletNode === "undefined") {
    return AUDIO_CAPTURE_UNSUPPORTED_ERROR;
  }

  return null;
}

export interface VoiceRecorderOptions {
  endpoint: string;
  language?: string;
  cleanup?: boolean;
  onStatusChange: (status: VoiceStatus) => void;
  onResult: (text: string) => void;
  onError: (error: string) => void;
  onVolumeChange?: (volume: number) => void;
}

export class VoiceRecorder {
  private options: VoiceRecorderOptions;
  private ws: WebSocket | null = null;
  private audioContext: AudioContext | null = null;
  private workletNode: AudioWorkletNode | null = null;
  private stream: MediaStream | null = null;
  private chunksSent = 0;

  constructor(options: VoiceRecorderOptions) {
    this.options = options;
  }

  async start() {
    debugLog("[VoiceRecorder] start() initiated");
    this.options.onStatusChange("connecting");

    // Feature-detect before constructing anything, so an unsupported browser is not
    // left holding a dangling AudioContext.
    const unsupported = unsupportedEnvironmentError();
    if (unsupported) {
      console.warn("[VoiceRecorder] Environment cannot capture audio:", unsupported);
      this.options.onError(unsupported);
      this.options.onStatusChange("idle");
      return;
    }

    try {
      // Initialize AudioContext synchronously within the user gesture event handler
      // to prevent modern browsers from blocking/suspending the audio context.
      this.audioContext = new AudioContext({ sampleRate: 24000 });
      debugLog("[VoiceRecorder] AudioContext created, state:", this.audioContext.state);
      if (this.audioContext.state === "suspended") {
        await this.audioContext.resume();
        debugLog("[VoiceRecorder] AudioContext resumed, state:", this.audioContext.state);
      }

      this.stream = await navigator.mediaDevices.getUserMedia({
        audio: { sampleRate: 24000, channelCount: 1, echoCancellation: true },
      });
      debugLog("[VoiceRecorder] Microphone stream acquired successfully");

      debugLog("[VoiceRecorder] Connecting to WebSocket endpoint:", this.options.endpoint);
      this.ws = new WebSocket(this.options.endpoint);

      this.ws.onopen = () => {
        debugLog("[VoiceRecorder] WebSocket open. Sending start message.");
        const startMsg: any = {
          type: "start",
          format: "pcm16",
          cleanup: this.options.cleanup !== false,
        };
        if (this.options.language) {
          startMsg.language = this.options.language;
        }
        this.ws?.send(JSON.stringify(startMsg));
      };

      this.ws.onmessage = (event) => {
        try {
          const data = JSON.parse(event.data);
          debugLog("[VoiceRecorder] Received WebSocket message:", data.type, data);
          if (data.type === "ready") {
            this.options.onStatusChange("recording");
            if (this.stream && this.ws) {
              this.beginPcmCapture(this.stream, this.ws).catch((err) => {
                console.error("[VoiceRecorder] Failed to begin PCM capture:", err);
                this.options.onError(`Audio capture initialization failed: ${err}`);
                this.cleanup();
              });
            }
          } else if (data.type === "result") {
            debugLog("[VoiceRecorder] Transcription result:", data.text);
            this.options.onResult(data.text);
            this.cleanup();
          } else if (data.type === "error") {
            console.error("[VoiceRecorder] Transcription server error:", data.message);
            this.options.onError(data.message);
            this.cleanup();
          }
        } catch (err) {
          console.error("[VoiceRecorder] Error parsing WebSocket message:", err);
        }
      };

      this.ws.onerror = (evt) => {
        console.error("[VoiceRecorder] WebSocket error event:", evt);
        this.options.onError("WebSocket error occurred connecting to transcription service.");
        this.cleanup();
      };

      this.ws.onclose = (evt) => {
        debugLog(
          `[VoiceRecorder] WebSocket closed: code=${evt.code}, reason=${evt.reason || "No reason"}, wasClean=${evt.wasClean}`,
        );
        if (!evt.wasClean && evt.code !== 1000 && evt.code !== 1005) {
          this.options.onError(
            `WebSocket connection closed unexpectedly (code ${evt.code}: ${evt.reason || "No reason given"})`,
          );
        }
        this.cleanup();
      };
    } catch (err) {
      console.error("[VoiceRecorder] start() failed:", err);
      let errorMessage = `Microphone access denied or connection failed: ${err}`;
      if (err instanceof DOMException) {
        if (err.name === "NotAllowedError") {
          errorMessage = "Microphone access was denied. Allow microphone access for Ivy Tendril in System Settings > Privacy and Security > Microphone, then try again.";
        } else if (err.name === "NotFoundError") {
          errorMessage = "No microphone was found. Connect an input device and try again.";
        }
      }
      this.options.onError(errorMessage);
      this.options.onStatusChange("idle");
      if (this.audioContext) {
        this.audioContext.close().catch(() => {});
        this.audioContext = null;
      }
    }
  }

  private async beginPcmCapture(stream: MediaStream, ws: WebSocket) {
    this.chunksSent = 0;
    debugLog("[VoiceRecorder] beginPcmCapture initiated");
    if (!this.audioContext) {
      this.audioContext = new AudioContext({ sampleRate: 24000 });
    }
    if (this.audioContext.state === "suspended") {
      await this.audioContext.resume();
    }
    debugLog("[VoiceRecorder] Loading audio worklet module...");
    await this.audioContext.audioWorklet.addModule(pcmWorkletUrl());
    debugLog("[VoiceRecorder] Audio worklet module loaded successfully");

    const source = this.audioContext.createMediaStreamSource(stream);
    this.workletNode = new AudioWorkletNode(this.audioContext, "pcm-capture");

    this.workletNode.port.onmessage = (event) => {
      const msg = event.data;
      if (msg.type === "volume" && this.options.onVolumeChange) {
        this.options.onVolumeChange(msg.volume);
      } else if (msg.type === "pcm16") {
        if (ws.readyState === WebSocket.OPEN) {
          ws.send(msg.buffer);
          this.chunksSent++;
        }
      }
    };

    source.connect(this.workletNode);
    this.workletNode.connect(this.audioContext.destination);
    debugLog("[VoiceRecorder] Audio graph connected and recording started");
  }

  stop() {
    debugLog("[VoiceRecorder] stop() initiated");
    debugLog(`[VoiceRecorder] Sent ${this.chunksSent} PCM16 audio chunks to server`);
    if (this.ws && this.ws.readyState === WebSocket.OPEN) {
      try {
        debugLog("[VoiceRecorder] Sending stop signal to WebSocket");
        this.ws.send(JSON.stringify({ type: "stop" }));
      } catch (err) {
        console.error("[VoiceRecorder] Error sending stop signal:", err);
      }
      this.options.onStatusChange("processing");
    }

    if (this.workletNode) {
      debugLog("[VoiceRecorder] Disconnecting AudioWorkletNode");
      this.workletNode.disconnect();
      this.workletNode = null;
    }

    if (this.audioContext) {
      debugLog("[VoiceRecorder] Closing AudioContext");
      this.audioContext.close().catch(() => {});
      this.audioContext = null;
    }

    if (this.stream) {
      debugLog("[VoiceRecorder] Stopping media stream tracks");
      this.stream.getTracks().forEach((track) => track.stop());
      this.stream = null;
    }
  }

  private cleanup() {
    const socket = this.ws;
    if (!socket) return;
    this.ws = null;

    debugLog("[VoiceRecorder] Cleaning up session");
    this.stop();
    if (socket.readyState === WebSocket.OPEN || socket.readyState === WebSocket.CONNECTING) {
      debugLog("[VoiceRecorder] Closing WebSocket connection");
      socket.close();
    }
    this.options.onStatusChange("idle");
  }
}

function pcmWorkletUrl(): string {
  const code = `
class PcmCaptureProcessor extends AudioWorkletProcessor {
  constructor() {
    super();
    this._buffer = [];
    this._samplesPerChunk = 2400; // 100ms at 24kHz
    this._volumeCounter = 0;
  }

  process(inputs) {
    const input = inputs[0];
    if (!input || !input[0]) return true;

    const samples = input[0];
    for (let i = 0; i < samples.length; i++) {
      this._buffer.push(samples[i]);
    }

    this._volumeCounter++;
    if (this._volumeCounter >= 8) { // Update volume every ~40ms (8 * 128 samples / 24kHz = 42.6ms)
      let sum = 0;
      for (let i = 0; i < samples.length; i++) {
        sum += samples[i] * samples[i];
      }
      const rms = Math.sqrt(sum / samples.length);
      this.port.postMessage({ type: "volume", volume: rms });
      this._volumeCounter = 0;
    }

    while (this._buffer.length >= this._samplesPerChunk) {
      const chunk = this._buffer.splice(0, this._samplesPerChunk);
      const pcm16 = new Int16Array(chunk.length);
      for (let i = 0; i < chunk.length; i++) {
        const s = Math.max(-1, Math.min(1, chunk[i]));
        pcm16[i] = s < 0 ? s * 0x8000 : s * 0x7FFF;
      }
      this.port.postMessage({ type: "pcm16", buffer: pcm16.buffer }, [pcm16.buffer]);
    }

    return true;
  }
}

registerProcessor('pcm-capture', PcmCaptureProcessor);
`;
  const blob = new Blob([code], { type: "application/javascript" });
  return URL.createObjectURL(blob);
}
