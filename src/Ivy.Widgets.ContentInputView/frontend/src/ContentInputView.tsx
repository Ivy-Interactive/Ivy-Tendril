import React, { useState, useRef, useEffect } from "react";
import { VoiceRecorder, type VoiceStatus } from "./voice-recorder";
import "./content-input-view.css";

interface AttachedFile {
  name: string;
  type: string;
  size?: string;
}

interface ContentInputViewProps {
  id: string;
  width?: string;
  height?: string;
  placeholder?: string;
  value?: string;
  transcriptionUrl?: string;
  models?: string[];
  selectedModel?: string;
  attachedFiles?: AttachedFile[];
  onIvyEvent?: (eventName: string, id: string, argumentsArray: unknown[]) => void;
  eventHandler?: (eventName: string, id: string, argumentsArray: unknown[]) => void;
  events?: string[];
}

export const ContentInputView: React.FC<ContentInputViewProps> = ({
  id,
  width = "100%",
  height = "auto",
  placeholder = "How can I help you today?",
  value = "",
  transcriptionUrl = "wss://tendril-api.ivy.app/transcribe/ws",
  selectedModel = "Build",
  attachedFiles = [],
  onIvyEvent,
  eventHandler,
  events = [],
}) => {
  const dispatchEvent = onIvyEvent || eventHandler;
  const [text, setText] = useState(value);
  const [menuOpen, setMenuOpen] = useState(false);
  const [voiceStatus, setVoiceStatus] = useState<VoiceStatus>("idle");
  const [duration, setDuration] = useState(0);
  const [volume, setVolume] = useState(0);
  const [recordError, setRecordError] = useState<string | null>(null);

  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const recorderRef = useRef<VoiceRecorder | null>(null);
  const timerRef = useRef<number | null>(null);
  const menuRef = useRef<HTMLDivElement>(null);

  // Sync value prop to text state
  useEffect(() => {
    setText(value);
  }, [value]);

  // Close plus-menu on click outside
  useEffect(() => {
    const handleOutsideClick = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) {
        setMenuOpen(false);
      }
    };
    document.addEventListener("mousedown", handleOutsideClick);
    return () => document.removeEventListener("mousedown", handleOutsideClick);
  }, []);

  // Textarea Auto-Growing Height
  useEffect(() => {
    const textarea = textareaRef.current;
    if (textarea) {
      textarea.style.height = "auto";
      textarea.style.height = `${textarea.scrollHeight}px`;
    }
  }, [text]);

  // Timer logic for voice recording
  useEffect(() => {
    if (voiceStatus === "recording") {
      setDuration(0);
      timerRef.current = window.setInterval(() => {
        setDuration((d) => d + 1);
      }, 1000);
    } else {
      if (timerRef.current) {
        clearInterval(timerRef.current);
        timerRef.current = null;
      }
    }
    return () => {
      if (timerRef.current) clearInterval(timerRef.current);
    };
  }, [voiceStatus]);

  const handleSubmit = () => {
    if (voiceStatus !== "idle") return;
    if (events.includes("OnSubmit") && dispatchEvent) {
      dispatchEvent("OnSubmit", id, [{
        Value: text,
        SelectedModel: selectedModel,
        AttachedFiles: attachedFiles,
      }]);
    }
    setText("");
  };

  const handleTextChange = (val: string) => {
    setText(val);
    if (events.includes("OnChange") && dispatchEvent) {
      dispatchEvent("OnChange", id, [val]);
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      handleSubmit();
    }
  };

  const handleMenuAction = (action: string) => {
    setMenuOpen(false);
    if (events.includes("OnMenuAction") && dispatchEvent) {
      dispatchEvent("OnMenuAction", id, [action]);
    }
  };

  const handleRemoveAttachment = (fileName: string) => {
    if (events.includes("OnRemoveAttachment") && dispatchEvent) {
      dispatchEvent("OnRemoveAttachment", id, [fileName]);
    }
  };

  // Recording management
  const toggleRecording = async () => {
    if (voiceStatus === "idle") {
      setRecordError(null);
      setVolume(0);
      const recorder = new VoiceRecorder({
        endpoint: transcriptionUrl,
        onStatusChange: (status) => setVoiceStatus(status),
        onResult: (transcription) => {
          setText((prev) => {
            const next = prev ? `${prev} ${transcription}` : transcription;
            if (events.includes("OnChange") && dispatchEvent) {
              dispatchEvent("OnChange", id, [next]);
            }
            return next;
          });
        },
        onError: (err) => setRecordError(err),
        onVolumeChange: (vol) => setVolume(vol),
      });
      recorderRef.current = recorder;
      await recorder.start();
    } else {
      recorderRef.current?.stop();
    }
  };

  const formatTime = (secs: number) => {
    const mins = Math.floor(secs / 60);
    const remaining = secs % 60;
    return `${mins.toString().padStart(2, "0")}:${remaining.toString().padStart(2, "0")}`;
  };

  // Generate real audio level height modifications
  const barCount = 10;
  const bars = Array.from({ length: barCount }, (_, i) => {
    const baseHeight = 4 + (i % 3) * 6; // base height between 4px and 16px
    const dynamicScale = 1 + volume * 18; // scale based on volume
    return Math.min(28, baseHeight * dynamicScale);
  });

  return (
    <div className="civ-shell" style={{ width, height }}>
      {recordError && (
        <div className="civ-error-banner">
          <svg className="civ-icon-error" viewBox="0 0 20 20" fill="currentColor">
            <path fillRule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7 4a1 1 0 11-2 0 1 1 0 012 0zm-1-9a1 1 0 00-1 1v4a1 1 0 102 0V6a1 1 0 00-1-1z" clipRule="evenodd"/>
          </svg>
          <span>{recordError}</span>
          <button className="civ-error-close" onClick={() => setRecordError(null)}>×</button>
        </div>
      )}

      <div className="civ-input-card">
        {/* Render Attached Files */}
        {attachedFiles.length > 0 && (
          <div className="civ-attachments-list">
            {attachedFiles.map((file, idx) => (
              <div key={idx} className="civ-attachment-item">
                <svg className="civ-icon-attachment" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                  <path d="m21.44 11.05-9.19 9.19a6 6 0 0 1-8.49-8.49l9.19-9.19a4 4 0 0 1 5.66 5.66l-9.2 9.19a2 2 0 0 1-2.83-2.83l8.49-8.48" />
                </svg>
                <div className="civ-attachment-details">
                  <span className="civ-attachment-name">{file.name}</span>
                  <span className="civ-attachment-meta">
                    {file.type} {file.size ? `• ${file.size}` : ""}
                  </span>
                </div>
                <button
                  className="civ-attachment-remove"
                  onClick={() => handleRemoveAttachment(file.name)}
                >
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5">
                    <line x1="18" y1="6" x2="6" y2="18" />
                    <line x1="6" y1="6" x2="18" y2="18" />
                  </svg>
                </button>
              </div>
            ))}
          </div>
        )}

        {/* Text Area Input */}
        <textarea
          ref={textareaRef}
          className="civ-textarea"
          value={text}
          onChange={(e) => handleTextChange(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder={placeholder}
          rows={1}
          disabled={voiceStatus === "connecting" || voiceStatus === "processing"}
        />

        {/* Action Controls Row */}
        <div className="civ-control-bar">
          {/* Plus Menu dropdown */}
          <div className="civ-plus-container" ref={menuRef}>
            <button
              className={`civ-plus-btn ${menuOpen ? "active" : ""}`}
              onClick={() => setMenuOpen(!menuOpen)}
              type="button"
            >
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                <line x1="12" y1="5" x2="12" y2="19" />
                <line x1="5" y1="12" x2="19" y2="12" />
              </svg>
            </button>

            {menuOpen && (
              <div className="civ-menu-dropdown">
                <div className="civ-menu-search">
                  <svg className="civ-icon-search" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                    <circle cx="11" cy="11" r="8" />
                    <line x1="21" y1="21" x2="16.65" y2="16.65" />
                  </svg>
                  <input type="text" placeholder="Search..." onClick={(e) => e.stopPropagation()} />
                </div>
                <div className="civ-menu-divider" />
                <button onClick={() => handleMenuAction("Attach")} className="civ-menu-item">
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                    <path d="m21.44 11.05-9.19 9.19a6 6 0 0 1-8.49-8.49l9.19-9.19a4 4 0 0 1 5.66 5.66l-9.2 9.19a2 2 0 0 1-2.83-2.83l8.49-8.48" />
                  </svg>
                  <span>Attach</span>
                  <span className="civ-menu-arrow">›</span>
                </button>
                <button onClick={() => handleMenuAction("Design")} className="civ-menu-item">
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                    <path d="M12 20h9M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4Z"/>
                  </svg>
                  <span>Design</span>
                  <span className="civ-menu-arrow">›</span>
                </button>
                <button onClick={() => handleMenuAction("Connectors")} className="civ-menu-item">
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                    <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9M13.73 21a2 2 0 0 1-3.46 0"/>
                  </svg>
                  <span>Connectors</span>
                  <span className="civ-menu-arrow">›</span>
                </button>
                <button onClick={() => handleMenuAction("Databases")} className="civ-menu-item">
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                    <ellipse cx="12" cy="5" rx="9" ry="3" />
                    <path d="M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5" />
                    <path d="M3 12c0 1.66 4 3 9 3s9-1.34 9-3" />
                  </svg>
                  <span>Databases</span>
                  <span className="civ-menu-arrow">›</span>
                </button>
              </div>
            )}
          </div>

          {/* Right Side Buttons */}
          <div className="civ-right-actions">
            {/* Voice Input Container */}
            <div className={`civ-voice-container ${voiceStatus !== "idle" ? "active" : ""}`}>
              {voiceStatus !== "idle" && (
                <div className="civ-recording-bar">
                  <span className="civ-dot-pulse" />
                  <span className="civ-timer">{formatTime(duration)}</span>
                  <div className="civ-equalizer">
                    {bars.map((h, i) => (
                      <div
                        key={i}
                        className="civ-eq-bar"
                        style={{ height: `${h}px` }}
                      />
                    ))}
                  </div>
                </div>
              )}
              <button
                className={`civ-mic-btn civ-status-${voiceStatus}`}
                onClick={toggleRecording}
                type="button"
                title="Voice input transcription"
              >
                {voiceStatus === "connecting" ? (
                  <div className="civ-spinner" />
                ) : voiceStatus === "processing" ? (
                  <div className="civ-spinner processing" />
                ) : (
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                    <path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z" />
                    <path d="M19 10v1a7 7 0 0 1-14 0v-1M12 19v3M8 22h8" />
                  </svg>
                )}
              </button>
            </div>

            {/* Submit Button */}
            <button
              className="civ-submit-btn"
              onClick={handleSubmit}
              disabled={!text.trim() || voiceStatus !== "idle"}
              type="button"
            >
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5">
                <line x1="12" y1="19" x2="12" y2="5" />
                <polyline points="5 12 12 5 19 12" />
              </svg>
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};
