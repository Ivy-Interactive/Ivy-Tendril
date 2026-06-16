import React, { useState, useRef, useEffect } from "react";
import { VoiceRecorder, type VoiceStatus } from "./voice-recorder";
import "./content-input-view.css";

const isMac = typeof navigator !== "undefined" && /Mac|iPod|iPhone|iPad/.test(navigator.userAgent);

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
  uploadUrl?: string;
  models?: string[];
  selectedModel?: string;
  attachedFiles?: AttachedFile[];
  submitLabel?: string;
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
  uploadUrl,
  selectedModel = "Build",
  attachedFiles = [],
  submitLabel,
  onIvyEvent,
  eventHandler,
}) => {
  const dispatchEvent = onIvyEvent || eventHandler;
  const [text, setText] = useState(value);
  const [voiceStatus, setVoiceStatus] = useState<VoiceStatus>("idle");
  const [duration, setDuration] = useState(0);
  const [volume, setVolume] = useState(0);
  const [recordError, setRecordError] = useState<string | null>(null);
  const [isDragging, setIsDragging] = useState(false);

  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const recorderRef = useRef<VoiceRecorder | null>(null);
  const timerRef = useRef<number | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  // Sync value prop to text state
  useEffect(() => {
    setText(value);
  }, [value]);

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
  // Stop recording when component unmounts
  useEffect(() => {
    return () => {
      if (recorderRef.current) {
        recorderRef.current.stop();
        // Prevent state updates on unmounted component
        (recorderRef.current as any).options = {
          onStatusChange: () => {},
          onResult: () => {},
          onError: () => {},
          onVolumeChange: () => {},
        };
      }
    };
  }, []);

  // Prevent dialog from closing on drag-and-drop operations
  useEffect(() => {
    let dragCounter = 0;
    let isDraggingGlobal = false;
    let dragTimeout: any = null;

    const resetDragTimeout = () => {
      if (dragTimeout) {
        clearTimeout(dragTimeout);
      }
      dragTimeout = setTimeout(() => {
        isDraggingGlobal = false;
        dragCounter = 0;
      }, 500); // 500ms watchdog
    };

    const handleDragEnterWindow = () => {
      dragCounter++;
      isDraggingGlobal = true;
      resetDragTimeout();
    };

    const handleDragOverWindow = (e: DragEvent) => {
      e.preventDefault(); // Required to allow drop events and prevent browser navigation
      isDraggingGlobal = true;
      resetDragTimeout();
    };

    const handleDragLeaveWindow = () => {
      dragCounter--;
      if (dragCounter <= 0) {
        dragCounter = 0;
        isDraggingGlobal = false;
        if (dragTimeout) {
          clearTimeout(dragTimeout);
          dragTimeout = null;
        }
      }
    };

    const handleDropWindow = (e: DragEvent) => {
      e.preventDefault(); // Prevent browser default navigation
      dragCounter = 0;
      isDraggingGlobal = false;
      if (dragTimeout) {
        clearTimeout(dragTimeout);
        dragTimeout = null;
      }
    };

    const handleFocusInWindow = (e: FocusEvent) => {
      if (isDraggingGlobal) {
        // Stop Radix's DismissableLayer from closing the dialog when window gains focus due to drag-and-drop
        e.stopImmediatePropagation();
      }
    };

    const handlePointerDownWindow = (e: Event) => {
      if (isDraggingGlobal) {
        // Stop Radix's DismissableLayer from closing the dialog on pointer downs during a drag
        e.stopImmediatePropagation();
      }
    };

    window.addEventListener("dragenter", handleDragEnterWindow, true);
    window.addEventListener("dragover", handleDragOverWindow, true);
    window.addEventListener("dragleave", handleDragLeaveWindow, true);
    window.addEventListener("drop", handleDropWindow, true);
    window.addEventListener("focusin", handleFocusInWindow, true);
    window.addEventListener("pointerdown", handlePointerDownWindow, true);
    window.addEventListener("mousedown", handlePointerDownWindow, true);

    return () => {
      if (dragTimeout) clearTimeout(dragTimeout);
      window.removeEventListener("dragenter", handleDragEnterWindow, true);
      window.removeEventListener("dragover", handleDragOverWindow, true);
      window.removeEventListener("dragleave", handleDragLeaveWindow, true);
      window.removeEventListener("drop", handleDropWindow, true);
      window.removeEventListener("focusin", handleFocusInWindow, true);
      window.removeEventListener("pointerdown", handlePointerDownWindow, true);
      window.removeEventListener("mousedown", handlePointerDownWindow, true);
    };
  }, []);

  const handleSubmit = () => {
    if (voiceStatus !== "idle") return;
    if (dispatchEvent) {
      dispatchEvent("OnSubmit", id, [{
        value: text,
        Value: text,
        selectedModel: selectedModel,
        SelectedModel: selectedModel,
        attachedFiles: attachedFiles,
        AttachedFiles: attachedFiles,
      }]);
    }
    setText("");
  };

  const handleTextChange = (val: string) => {
    setText(val);
    if (dispatchEvent) {
      dispatchEvent("OnChange", id, [val]);
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === "Enter" && (e.metaKey || e.ctrlKey)) {
      e.preventDefault();
      handleSubmit();
    }
  };

  const handleUploadFile = async (file: File) => {
    if (uploadUrl) {
      try {
        const formData = new FormData();
        formData.append("file", file);

        const response = await fetch(uploadUrl, {
          method: "POST",
          body: formData,
        });

        if (!response.ok) {
          throw new Error(`Upload failed with status ${response.status}`);
        }
      } catch (err) {
        console.error("[ContentInputView] File upload failed:", err);
        setRecordError(`Failed to upload file: ${err instanceof Error ? err.message : err}`);
        throw err;
      }
    } else {
      // Fallback to base64 WebSocket transfer
      return new Promise<void>((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = async () => {
          const result = reader.result as string;
          const base64Data = result.split(",")[1];
          if (dispatchEvent) {
            dispatchEvent("OnUploadFile", id, [{
              name: file.name,
              Name: file.name,
              base64Data: base64Data,
              Base64Data: base64Data,
            }]);
          }
          resolve();
        };
        reader.onerror = (err) => {
          setRecordError("Failed to read file");
          reject(err);
        };
        reader.readAsDataURL(file);
      });
    }
  };

  const handleFiles = async (files: FileList | File[]) => {
    const list = Array.from(files);
    for (const file of list) {
      await handleUploadFile(file);
    }
  };

  const handlePaste = async (e: React.ClipboardEvent<HTMLTextAreaElement>) => {
    const items = e.clipboardData.items;
    const files: File[] = [];
    for (let i = 0; i < items.length; i++) {
      if (items[i].kind === "file") {
        const file = items[i].getAsFile();
        if (file) {
          files.push(file);
        }
      }
    }
    if (files.length > 0) {
      e.preventDefault();
      await handleFiles(files);
    }
  };

  const handleDragEnter = (e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    if (e.dataTransfer) {
      e.dataTransfer.dropEffect = "copy";
    }
    setIsDragging(true);
  };

  const handleDragLeave = (e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragging(false);
  };

  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    if (e.dataTransfer) {
      e.dataTransfer.dropEffect = "copy";
    }
  };

  const handleDrop = async (e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragging(false);
    if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
      await handleFiles(e.dataTransfer.files);
    }
  };

  const handleRemoveAttachment = (fileName: string) => {
    if (dispatchEvent) {
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
          console.log("[ContentInputView] Transcription result received:", transcription);
          setText((prev) => {
            const next = prev ? `${prev} ${transcription}` : transcription;
            console.log("[ContentInputView] Next text state:", next);
            if (dispatchEvent) {
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

      <div
        className={`civ-input-card ${isDragging ? "dragging" : ""}`}
        onDragEnter={handleDragEnter}
        onDragLeave={handleDragLeave}
        onDragOver={handleDragOver}
        onDrop={handleDrop}
      >
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
          onPaste={handlePaste}
          placeholder={placeholder}
          rows={1}
          disabled={voiceStatus === "connecting" || voiceStatus === "processing"}
        />

        {/* Action Controls Row */}
        <div className="civ-control-bar">
          {/* Attachment upload button */}
          <div className="civ-attachment-upload-container">
            <input
              type="file"
              multiple
              ref={fileInputRef}
              style={{ display: "none" }}
              onChange={(e) => {
                if (e.target.files) {
                  handleFiles(e.target.files);
                  e.target.value = "";
                }
              }}
            />
            <button
              className="civ-plus-btn"
              onClick={() => fileInputRef.current?.click()}
              type="button"
              title="Attach files"
            >
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                <path d="m21.44 11.05-9.19 9.19a6 6 0 0 1-8.49-8.49l9.19-9.19a4 4 0 0 1 5.66 5.66l-9.2 9.19a2 2 0 0 1-2.83-2.83l8.49-8.48" />
              </svg>
            </button>
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
                ) : voiceStatus === "recording" ? (
                  <svg viewBox="0 0 24 24" fill="currentColor" stroke="currentColor" strokeWidth="2">
                    <rect x="6" y="6" width="12" height="12" rx="2" ry="2" />
                  </svg>
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
              className={`civ-submit-btn ${submitLabel ? "civ-submit-btn-labeled" : ""}`}
              onClick={handleSubmit}
              disabled={!text.trim() || voiceStatus !== "idle"}
              type="button"
              title={submitLabel || "Send"}
            >
              {submitLabel ? (
                <>
                  <span className="civ-submit-text">{submitLabel}</span>
                  <kbd className="civ-submit-shortcut">
                    {isMac ? "⌘↵" : "Ctrl+Enter"}
                  </kbd>
                </>
              ) : (
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5">
                  <line x1="12" y1="19" x2="12" y2="5" />
                  <polyline points="5 12 12 5 19 12" />
                </svg>
              )}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};
