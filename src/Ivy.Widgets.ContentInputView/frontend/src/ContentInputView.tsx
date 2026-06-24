import React, { useState, useRef, useEffect } from "react";
import * as pdfjsLib from "pdfjs-dist";
import pdfjsWorker from "pdfjs-dist/build/pdf.worker.mjs?url";
import { VoiceRecorder, type VoiceStatus } from "./voice-recorder";
import "./content-input-view.css";

const isMac = typeof navigator !== "undefined" && /Mac|iPod|iPhone|iPad/.test(navigator.userAgent);

if (typeof window !== "undefined") {
  pdfjsLib.GlobalWorkerOptions.workerSrc = pdfjsWorker;
}

const PdfThumbnail: React.FC<{ url: string }> = ({ url }) => {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const [error, setError] = useState(false);

  useEffect(() => {
    let active = true;

    const renderPdf = async () => {
      try {
        const loadingTask = pdfjsLib.getDocument({ url });
        const pdf = await loadingTask.promise;
        if (!active) return;
        const page = await pdf.getPage(1);
        if (!active) return;

        const canvas = canvasRef.current;
        if (!canvas) return;
        const context = canvas.getContext("2d");
        if (!context) return;

        const unscaledViewport = page.getViewport({ scale: 1.0 });
        const scaleX = 140 / unscaledViewport.width;
        const scaleY = 105 / unscaledViewport.height;
        const baseScale = Math.max(scaleX, scaleY);
        const scale = baseScale * 3;
        const viewport = page.getViewport({ scale });

        canvas.width = viewport.width;
        canvas.height = viewport.height;

        const renderContext = {
          canvasContext: context,
          viewport: viewport,
          canvas: canvas,
        };
        await page.render(renderContext).promise;
      } catch (err) {
        console.error("PDF.js render failed:", err);
        if (active) setError(true);
      }
    };

    renderPdf();

    return () => {
      active = false;
    };
  }, [url]);

  if (error) {
    return (
      <div
        style={{
          width: "100%",
          height: "100%",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          fontSize: "1.5rem",
          background: "var(--civ-pill-bg)",
        }}
      >
        📄
      </div>
    );
  }

  return <canvas ref={canvasRef} style={{ width: "100%", height: "100%", objectFit: "cover", objectPosition: "top", display: "block" }} />;
};

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
  menuOptions?: string[];
  onIvyEvent?: (eventName: string, id: string, argumentsArray: unknown[]) => void;
  eventHandler?: (eventName: string, id: string, argumentsArray: unknown[]) => void;
  events?: string[];
}

const fileRegExp = /\s?\[file:\s*([^\]]+)\]/g;

const parseValue = (val: string) => {
  const filePaths: string[] = [];
  const cleanText = val.replace(fileRegExp, (_match, path) => {
    filePaths.push(path.trim());
    return "";
  });
  return { cleanText, filePaths };
};

const renderShortcut = (isMac: boolean) => {
  if (isMac) {
    return (
      <>
        <span>⌘</span>
        <span className="civ-shortcut-enter">↵</span>
      </>
    );
  }
  return "Ctrl+Enter";
};

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
  menuOptions = [],
  onIvyEvent,
  eventHandler,
}) => {
  const dispatchEvent = onIvyEvent || eventHandler;

  const parsed = parseValue(value);
  const [text, setText] = useState(parsed.cleanText);
  const [files, setFiles] = useState<string[]>(parsed.filePaths);
  const [voiceStatus, setVoiceStatus] = useState<VoiceStatus>("idle");
  const [duration, setDuration] = useState(0);
  const [volume, setVolume] = useState(0);
  const [recordError, setRecordError] = useState<string | null>(null);
  const [isDragging, setIsDragging] = useState(false);
  const [menuOpen, setMenuOpen] = useState(false);

  const [previews, setPreviews] = useState<Record<string, string>>({});
  const [fileMeta, setFileMeta] = useState<Record<string, { lineCount?: number; size: string }>>({});

  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const recorderRef = useRef<VoiceRecorder | null>(null);
  const timerRef = useRef<number | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const filesRef = useRef(files);

  useEffect(() => {
    filesRef.current = files;
  }, [files]);

  const isImageFile = (path: string) => {
    const ext = path.split(".").pop()?.toLowerCase();
    return ["png", "jpg", "jpeg", "webp", "gif", "svg"].includes(ext || "");
  };

  const isPdfFile = (path: string) => {
    const ext = path.split(".").pop()?.toLowerCase();
    return ext === "pdf";
  };


  const getFileMetadata = (filePath: string) => {
    const fileName = filePath.split(/[/\\]/).pop() || "";
    const lowerName = fileName.toLowerCase();
    const ext = fileName.split(".").pop()?.toUpperCase() || "";

    for (const [origName, meta] of Object.entries(fileMeta)) {
      const origBase = origName.split(".")[0].toLowerCase().replace(/\s+/g, "_");
      if (lowerName.includes(origBase)) {
        return {
          name: origName,
          metaText: meta.lineCount !== undefined ? `${meta.lineCount} lines` : meta.size,
          badge: ext,
        };
      }
    }

    const cleanName = fileName.replace(/_[a-f0-9]{8}(\.[^.]+)$/i, "$1");
    return {
      name: cleanName,
      metaText: "Document",
      badge: ext,
    };
  };

  const getPreviewUrl = (filePath: string) => {
    const fileName = filePath.split(/[/\\]/).pop() || "";
    const lowerName = fileName.toLowerCase();

    for (const [origName, url] of Object.entries(previews)) {
      const origBase = origName.split(".")[0].toLowerCase().replace(/\s+/g, "_");
      if (lowerName.includes(origBase)) {
        return url;
      }
    }
    return null;
  };

  // Close menu when clicking outside
  useEffect(() => {
    if (!menuOpen) return;
    const handleClickOutside = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) {
        setMenuOpen(false);
      }
    };
    document.addEventListener("mousedown", handleClickOutside);
    return () => {
      document.removeEventListener("mousedown", handleClickOutside);
    };
  }, [menuOpen]);

  // Sync value prop to text and files state
  useEffect(() => {
    const { cleanText, filePaths } = parseValue(value);
    setText(cleanText);
    setFiles(filePaths);
  }, [value]);

  // Textarea Auto-Growing Height
  useEffect(() => {
    const textarea = textareaRef.current;
    if (textarea) {
      textarea.style.height = "auto";
      textarea.style.height = `${textarea.scrollHeight}px`;
    }
  }, [text, files]);

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
          onStatusChange: () => { },
          onResult: () => { },
          onError: () => { },
          onVolumeChange: () => { },
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
    const fullText = text + files.map((f) => ` [file: ${f}]`).join("");
    if (dispatchEvent) {
      dispatchEvent("OnSubmit", id, [
        {
          value: fullText,
          Value: fullText,
          selectedModel: selectedModel,
          SelectedModel: selectedModel,
          attachedFiles: attachedFiles,
          AttachedFiles: attachedFiles,
        },
      ]);
    }
    setText("");
    setFiles([]);
  };

  const handleTextChange = (val: string) => {
    setText(val);
    if (dispatchEvent) {
      const fullText = val + files.map((f) => ` [file: ${f}]`).join("");
      dispatchEvent("OnChange", id, [fullText]);
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
            dispatchEvent("OnUploadFile", id, [
              {
                name: file.name,
                Name: file.name,
                base64Data: base64Data,
                Base64Data: base64Data,
              },
            ]);
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

  const formatSize = (bytes: number) => {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  };

  const handleFiles = async (filesList: FileList | File[]) => {
    const list = Array.from(filesList);

    for (const file of list) {
      const sizeStr = formatSize(file.size);
      let lineCount: number | undefined;

      if (file.type.startsWith("text/") || file.name.endsWith(".txt") || file.name.endsWith(".log") || file.name.endsWith(".json") || file.name.endsWith(".csv")) {
        try {
          const textContent = await file.text();
          lineCount = textContent.split("\n").length;
        } catch (e) {
          // ignore
        }
      }

      setFileMeta((prev) => ({
        ...prev,
        [file.name]: { lineCount, size: sizeStr },
      }));

      if (file.type.startsWith("image/") || file.type === "application/pdf" || file.name.toLowerCase().endsWith(".pdf")) {
        const objectUrl = URL.createObjectURL(file);
        setPreviews((prev) => ({
          ...prev,
          [file.name]: objectUrl,
        }));
      }
    }

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


  const handleRemoveFile = (filePath: string) => {
    const fileName = filePath.split(/[/\\]/).pop() || "";
    const lowerName = fileName.toLowerCase();

    for (const [origName, url] of Object.entries(previews)) {
      const origBase = origName.split(".")[0].toLowerCase().replace(/\s+/g, "_");
      if (lowerName.includes(origBase)) {
        URL.revokeObjectURL(url);
        const newPreviews = { ...previews };
        delete newPreviews[origName];
        setPreviews(newPreviews);
        break;
      }
    }

    const newFiles = files.filter((f) => f !== filePath);
    setFiles(newFiles);
    if (dispatchEvent) {
      dispatchEvent("OnRemoveAttachment", id, [filePath]);
    }
    const fullText = text + newFiles.map((f) => ` [file: ${f}]`).join("");
    if (dispatchEvent) {
      dispatchEvent("OnChange", id, [fullText]);
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
              const fullText = next + filesRef.current.map((f) => ` [file: ${f}]`).join("");
              dispatchEvent("OnChange", id, [fullText]);
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
            <path
              fillRule="evenodd"
              d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7 4a1 1 0 11-2 0 1 1 0 012 0zm-1-9a1 1 0 00-1 1v4a1 1 0 102 0V6a1 1 0 00-1-1z"
              clipRule="evenodd"
            />
          </svg>
          <span>{recordError}</span>
          <button className="civ-error-close" onClick={() => setRecordError(null)}>
            ×
          </button>
        </div>
      )}

      <div
        className={`civ-input-card ${isDragging ? "dragging" : ""}`}
        onDragEnter={handleDragEnter}
        onDragLeave={handleDragLeave}
        onDragOver={handleDragOver}
        onDrop={handleDrop}
      >
        {/* Render Attached Files as Thumbnails */}
        {files.length > 0 && (
          <div className="civ-attachments-list">
            {files.map((filePath, idx) => {
              const isImage = isImageFile(filePath);
              const isPdf = isPdfFile(filePath);
              const previewUrl = (isImage || isPdf) ? getPreviewUrl(filePath) : null;
              const meta = getFileMetadata(filePath);

              return (
                <div key={idx} className="civ-thumbnail-card">
                  {/* Background Preview for images/PDFs */}
                  {(isImage || isPdf) && previewUrl && (
                    <div className="civ-thumbnail-preview-container">
                      {isImage ? (
                        <img className="civ-thumbnail-image-preview" src={previewUrl} alt="preview" />
                      ) : (
                        <PdfThumbnail url={previewUrl} />
                      )}
                      <div className="civ-thumbnail-preview-overlay" />
                    </div>
                  )}

                  {/* Overlaid Close Button */}
                  <button
                    className="civ-thumbnail-card-remove"
                    onClick={() => handleRemoveFile(filePath)}
                    type="button"
                    title="Remove file"
                  >
                    ×
                  </button>

                  {/* Overlaid File Metadata & Badge */}
                  <div className="civ-thumbnail-content">
                    {!(previewUrl && (isImage || isPdf)) ? (
                      <div style={{ minWidth: 0 }}>
                        <div className="civ-thumbnail-doc-name">{meta.name}</div>
                        <div className="civ-thumbnail-doc-meta">{meta.metaText}</div>
                      </div>
                    ) : (
                      <div />
                    )}
                    <div className="civ-thumbnail-doc-badge">{meta.badge}</div>
                  </div>
                </div>
              );
            })}
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
          {/* Attachment upload button & pills */}
          <div className="civ-attachment-row">
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
                      <div key={i} className="civ-eq-bar" style={{ height: `${h}px` }} />
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
                  <svg
                    viewBox="0 0 24 24"
                    fill="currentColor"
                    stroke="currentColor"
                    strokeWidth="2"
                  >
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

            {/* Submit Button or Split Button */}
            {menuOptions && menuOptions.length > 0 ? (
              <div
                className={`civ-split-btn-container ${!text.trim() || voiceStatus !== "idle" ? "disabled" : ""}`}
                ref={menuRef}
              >
                <button
                  className="civ-submit-btn civ-submit-btn-labeled civ-split-btn-left"
                  onClick={handleSubmit}
                  disabled={!text.trim() || voiceStatus !== "idle"}
                  type="button"
                  title={submitLabel || "Send"}
                >
                  <span className="civ-submit-text">{submitLabel}</span>
                  <kbd className="civ-submit-shortcut">{renderShortcut(isMac)}</kbd>
                </button>
                <button
                  className="civ-split-btn-arrow"
                  onClick={() => setMenuOpen(!menuOpen)}
                  disabled={!text.trim() || voiceStatus !== "idle"}
                  type="button"
                  title="More options"
                >
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5">
                    <polyline points="6 9 12 15 18 9" />
                  </svg>
                </button>
                {menuOpen && (
                  <div className="civ-dropdown-menu">
                    {menuOptions.map((option, idx) => (
                      <button
                        key={idx}
                        className="civ-dropdown-item"
                        type="button"
                        onClick={() => {
                          setMenuOpen(false);
                          if (dispatchEvent) {
                            dispatchEvent("OnMenuAction", id, [option]);
                          }
                        }}
                      >
                        {option}
                      </button>
                    ))}
                  </div>
                )}
              </div>
            ) : (
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
                    <kbd className="civ-submit-shortcut">{renderShortcut(isMac)}</kbd>
                  </>
                ) : (
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5">
                    <line x1="12" y1="19" x2="12" y2="5" />
                    <polyline points="5 12 12 5 19 12" />
                  </svg>
                )}
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};
