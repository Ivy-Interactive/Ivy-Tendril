import { useCallback, useEffect, useRef, useState } from "react";
import { VoiceRecorder, type VoiceStatus } from "../voice-recorder";

export const DEFAULT_TRANSCRIPTION_URL = "wss://tendril-api.ivy.app/transcribe/ws";

interface SpeechRecognitionEventLike {
  results: ArrayLike<{ 0: { transcript: string } }>;
}

interface SpeechRecognitionErrorEventLike {
  error?: string;
}

interface SpeechRecognitionLike {
  continuous: boolean;
  interimResults: boolean;
  onstart: (() => void) | null;
  onend: (() => void) | null;
  onerror: ((event: SpeechRecognitionErrorEventLike) => void) | null;
  onresult: ((event: SpeechRecognitionEventLike) => void) | null;
  start(): void;
  stop(): void;
}

type SpeechRecognitionCtor = new () => SpeechRecognitionLike;

const getSpeechRecognition = (): SpeechRecognitionCtor | undefined => {
  const w = window as unknown as {
    SpeechRecognition?: SpeechRecognitionCtor;
    webkitSpeechRecognition?: SpeechRecognitionCtor;
  };
  return w.SpeechRecognition ?? w.webkitSpeechRecognition;
};

/**
 * Error codes a Chromium build without Google's speech-service API keys produces. The constructor
 * exists but never yields a result, which is how voice input silently does nothing in embedded
 * browsers such as Tesla's. Both are grounds to retry on the server transcription path.
 */
const FALLBACK_ERROR_CODES = ["network", "service-not-allowed"];

const MIC_PERMISSION_ERROR =
  "Microphone access was denied. Allow microphone access for this browser, then try again.";

const SPEECH_FAILED_ERROR = "Voice input could not start. Please try again.";

/**
 * Dictation into the composer: the transcript is appended to whatever was typed before it started.
 *
 * The Web Speech API is tried first, so browsers where it works (Safari, official Chrome builds)
 * keep their live interim results and need no network round trip. Only when there is direct evidence
 * Web Speech cannot work here — the constructor is missing, or it errors out before producing a
 * single result — does this drop to {@link VoiceRecorder}, which streams mic audio to the
 * transcription service over a WebSocket using baseline Chromium APIs.
 */
export function useSpeechInput(
  promptText: string,
  setPromptText: (text: string) => void,
  onTextApplied: () => void,
  transcriptionUrl: string = DEFAULT_TRANSCRIPTION_URL,
) {
  const [voiceStatus, setVoiceStatus] = useState<VoiceStatus>("idle");
  const [voiceError, setVoiceError] = useState<string | null>(null);
  const recognitionRef = useRef<SpeechRecognitionLike | null>(null);
  const recorderRef = useRef<VoiceRecorder | null>(null);
  const hasReceivedSpeechResultRef = useRef(false);
  const initialPromptRef = useRef("");

  const applyTranscript = useCallback(
    (transcript: string) => {
      const base = initialPromptRef.current.trim();
      setPromptText(base ? `${base} ${transcript.trimStart()}` : transcript);
      setTimeout(onTextApplied, 0);
    },
    [setPromptText, onTextApplied],
  );

  const startServerRecording = useCallback(() => {
    const recorder = new VoiceRecorder({
      endpoint: transcriptionUrl,
      onStatusChange: (status) => setVoiceStatus(status),
      onError: (message) => setVoiceError(message),
      onResult: (transcript) => applyTranscript(transcript),
    });
    recorderRef.current = recorder;
    void recorder.start();
  }, [transcriptionUrl, applyTranscript]);

  const toggle = useCallback(() => {
    if (voiceStatus !== "idle") {
      recognitionRef.current?.stop();
      recorderRef.current?.stop();
      return;
    }

    setVoiceError(null);
    initialPromptRef.current = promptText;

    const Recognition = getSpeechRecognition();
    if (!Recognition) {
      // No Web Speech API at all — go straight to server transcription.
      startServerRecording();
      return;
    }

    hasReceivedSpeechResultRef.current = false;
    const recognition = new Recognition();
    recognitionRef.current = recognition;
    recognition.continuous = true;
    recognition.interimResults = true;
    recognition.onstart = () => setVoiceStatus("recording");
    recognition.onend = () => setVoiceStatus("idle");
    recognition.onerror = (event) => {
      const code = event?.error;
      if (!hasReceivedSpeechResultRef.current && code && FALLBACK_ERROR_CODES.includes(code)) {
        // Web Speech is present but non-functional in this browser. Retry on the server path
        // silently — from the user's perspective the mic button should just keep working.
        // Detach the handlers first: the `onend` that follows `onerror` would otherwise reset the
        // status to "idle" on top of the recorder that is already connecting.
        recognition.onstart = null;
        recognition.onend = null;
        recognition.onresult = null;
        recognition.onerror = null;
        recognitionRef.current = null;
        startServerRecording();
        return;
      }
      setVoiceError(code === "not-allowed" ? MIC_PERMISSION_ERROR : SPEECH_FAILED_ERROR);
      setVoiceStatus("idle");
    };
    recognition.onresult = (event) => {
      hasReceivedSpeechResultRef.current = true;
      let transcript = "";
      for (let i = 0; i < event.results.length; i++) {
        transcript += event.results[i][0].transcript;
      }
      applyTranscript(transcript);
    };
    recognition.start();
  }, [voiceStatus, promptText, startServerRecording, applyTranscript]);

  // Never let a mic stream or recognition session outlive the widget.
  useEffect(
    () => () => {
      recognitionRef.current?.stop();
      recorderRef.current?.stop();
    },
    [],
  );

  const dismissVoiceError = useCallback(() => setVoiceError(null), []);

  return { voiceStatus, voiceError, dismissVoiceError, toggle };
}
