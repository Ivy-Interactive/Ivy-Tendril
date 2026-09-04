import React, { useState, useRef, useEffect, useLayoutEffect } from "react";
import { createPortal } from "react-dom";
import { Mic, Bot, Cpu, Zap, MessageSquare, ChevronDown, Check, Pencil, Paperclip, X, Square, ArrowRight, Trash2 } from "lucide-react";
import ReactMarkdown from "react-markdown";
import * as pdfjsLib from "pdfjs-dist";
import pdfjsWorker from "pdfjs-dist/build/pdf.worker.mjs?url";
import { AgentViewer } from "../AgentViewer";
import { getMarkdownPlugins } from "../math";
import { BlockHandler } from "../BlockHandler";
import { AlertBlockquote } from "../PlanMarkdown/AlertBlockquote";
import { isImageFile, processImageFile } from "../imageUtils";
import "./chat-widget.css";

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
          background: "var(--muted)",
        }}
      >
        📄
      </div>
    );
  }

  return <canvas ref={canvasRef} style={{ width: "100%", height: "100%", objectFit: "cover", objectPosition: "top", display: "block" }} />;
};

const isPdfFile = (nameOrType: string) => {
  const lower = nameOrType.toLowerCase();
  return lower === "application/pdf" || lower.endsWith(".pdf");
};

const getFileExtBadge = (name: string): string => {
  const ext = name.split(".").pop()?.toUpperCase() || "FILE";
  return ext.length > 5 ? ext.slice(0, 5) : ext;
};

const parseUserMessageContent = (content: string) => {
  if (!content) return { prompt: "", attachedPaths: [] };
  const marker = "\n\n[Attached Files]:";
  const markerIndex = content.indexOf(marker);
  if (markerIndex !== -1) {
    const prompt = content.substring(0, markerIndex).trim();
    const filesSection = content.substring(markerIndex + marker.length);
    const paths = filesSection
      .split("\n")
      .map((l) => l.trim())
      .filter((l) => l.startsWith("- "))
      .map((l) => l.substring(2).trim())
      .filter(Boolean);
    return { prompt, attachedPaths: paths };
  }
  const altMarker = "[Attached Files]:";
  const altIndex = content.indexOf(altMarker);
  if (altIndex !== -1) {
    const prompt = content.substring(0, altIndex).trim();
    const filesSection = content.substring(altIndex + altMarker.length);
    const paths = filesSection
      .split("\n")
      .map((l) => l.trim())
      .filter((l) => l.startsWith("- "))
      .map((l) => l.substring(2).trim())
      .filter(Boolean);
    return { prompt, attachedPaths: paths };
  }
  return { prompt: content, attachedPaths: [] };
};

export interface ChatMessageDto {
  id: string;
  role: "user" | "assistant";
  content: string;
  timestamp: string;
  agentId?: string;
  modelId?: string;
  rawStream?: string;
  effort?: string;
}

export interface ChatSessionDto {
  id: string;
  title: string;
  agentId: string;
  modelId: string;
  createdAt: string;
  updatedAt: string;
  messages: ChatMessageDto[];
  status?: "generating" | "waiting" | "done";
  effort?: string;
}

export interface AgentOptionDto {
  id: string;
  label: string;
}

export interface ModelOptionDto {
  id: string;
  displayName: string;
}

export interface EffortOptionDto {
  id: string;
  displayName: string;
}

export interface ChatAttachmentDto {
  name: string;
  contentType: string;
  size: number;
  base64Data?: string;
  localPath?: string;
  lineCount?: number;
  previewUrl?: string;
  fileId?: string;
  uploadProgress?: number;
  uploadStatus?: "pending" | "uploading" | "finished" | "failed";
  error?: string;
}

export interface ChatQueuedMessageDto {
  id: string;
  prompt: string;
  attachments?: ChatAttachmentDto[];
}

type IvyEventHandler = (eventName: string, widgetId: string, args: unknown[]) => void;

export interface ChatWidgetProps {
  id: string;
  activeSessionId?: string | null;
  streamingSessionId?: string | null;
  uploadUrl?: string;
  sessions?: ChatSessionDto[];
  agents?: AgentOptionDto[];
  models?: ModelOptionDto[];
  efforts?: EffortOptionDto[];
  selectedAgent?: string;
  selectedModel?: string;
  selectedEffort?: string;
  supportsEffort?: boolean;
  isStreaming?: boolean;
  streamingText?: string;
  queuedMessages?: ChatQueuedMessageDto[];
  events?: string[];
  eventHandler?: IvyEventHandler;
}

interface InlineSelectOption {
  value: string;
  label: string;
}

interface InlineSelectProps {
  icon?: React.ReactNode;
  value: string;
  options: InlineSelectOption[];
  onChange: (value: string) => void;
  title?: string;
}

function InlineSelect({ icon, value, options, onChange, title }: InlineSelectProps) {
  const [open, setOpen] = useState(false);
  const [menuStyle, setMenuStyle] = useState<React.CSSProperties>({});
  const triggerRef = useRef<HTMLDivElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);

  const selectedOption = options.find((o) => o.value === value) || { value, label: value };

  const updatePosition = () => {
    if (!triggerRef.current) return;
    const rect = triggerRef.current.getBoundingClientRect();
    const spaceBelow = window.innerHeight - rect.bottom - 4;
    const openUp = spaceBelow < 150 && rect.top > spaceBelow;

    setMenuStyle({
      position: "fixed",
      left: rect.left,
      minWidth: Math.max(rect.width, 140),
      maxHeight: 220,
      top: openUp ? undefined : rect.bottom + 4,
      bottom: openUp ? window.innerHeight - rect.top + 4 : undefined,
      zIndex: 10000,
    });
  };

  useLayoutEffect(() => {
    if (open) {
      updatePosition();
    }
  }, [open, options.length]);

  useEffect(() => {
    if (!open) return;
    const onMouseDown = (e: MouseEvent) => {
      const target = e.target as Node;
      if (triggerRef.current?.contains(target) || menuRef.current?.contains(target)) return;
      setOpen(false);
    };
    const onReposition = () => updatePosition();

    document.addEventListener("mousedown", onMouseDown);
    window.addEventListener("resize", onReposition);
    window.addEventListener("scroll", onReposition, true);
    return () => {
      document.removeEventListener("mousedown", onMouseDown);
      window.removeEventListener("resize", onReposition);
      window.removeEventListener("scroll", onReposition, true);
    };
  }, [open]);

  return (
    <div ref={triggerRef} className="chat-inline-select-container" title={title}>
      <button
        type="button"
        className={`chat-inline-select-trigger ${open ? "open" : ""}`}
        onClick={() => setOpen((v) => !v)}
      >
        {icon}
        <span>{selectedOption.label}</span>
        <ChevronDown size={11} className="chat-select-chevron" />
      </button>

      {open &&
        createPortal(
          <div ref={menuRef} className="chat-inline-select-menu" style={menuStyle}>
            {options.map((opt) => {
              const isSelected = opt.value === value;
              return (
                <button
                  key={opt.value}
                  type="button"
                  className={`chat-inline-select-item ${isSelected ? "selected" : ""}`}
                  onClick={() => {
                    onChange(opt.value);
                    setOpen(false);
                  }}
                >
                  <span>{opt.label}</span>
                  {isSelected && <Check size={12} className="chat-select-check" />}
                </button>
              );
            })}
          </div>,
          document.body
        )}
    </div>
  );
}

const noopEventHandler: IvyEventHandler = () => {};

export const MAX_PAYLOAD_BYTES = 50 * 1024 * 1024;

export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export function ChatWidget({
  id,
  activeSessionId,
  streamingSessionId: _streamingSessionId,
  uploadUrl,
  sessions = [],
  agents = [],
  models = [],
  efforts = [],
  selectedAgent = "claude",
  selectedModel = "opus",
  selectedEffort = "default",
  supportsEffort = true,
  isStreaming = false,
  streamingText = "",
  queuedMessages: queuedMessagesProp,
  events = [],
  eventHandler,
}: ChatWidgetProps) {
  const [promptText, setPromptText] = useState("");
  const [isRecording, setIsRecording] = useState(false);
  const [isEditingTitle, setIsEditingTitle] = useState(false);
  const [editingTitleText, setEditingTitleText] = useState("");
  const [attachments, setAttachments] = useState<ChatAttachmentDto[]>([]);
  const [queuedMessages, setQueuedMessages] = useState<ChatQueuedMessageDto[]>(queuedMessagesProp || []);
  const [collapsedQueue, setCollapsedQueue] = useState(false);
  const [editingQueuedId, setEditingQueuedId] = useState<string | null>(null);
  const [editingQueuedText, setEditingQueuedText] = useState("");
  const [pendingRenames, setPendingRenames] = useState<Record<string, string>>({});
  const [optimisticMessages, setOptimisticMessages] = useState<Record<string, ChatMessageDto[]>>({});
  const [optimisticStreaming, setOptimisticStreaming] = useState<string | null>(null);

  const messagesEndRef = useRef<HTMLDivElement>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const recognitionRef = useRef<any>(null);
  const initialPromptRef = useRef<string>("");

  const activeSession = sessions.find((s) => s.id === activeSessionId);
  const currentOptimistic = (activeSessionId && optimisticMessages[activeSessionId]) || [];
  const displayMessages = [...(activeSession?.messages || []), ...currentOptimistic];
  const totalAttachmentSize = attachments.reduce((sum, att) => sum + (att.size || 0), 0);
  const isPayloadOversized = totalAttachmentSize > MAX_PAYLOAD_BYTES;
  const isUploading = attachments.some((att) => att.uploadStatus === "uploading");
  const isAnyFailed = attachments.some((att) => att.uploadStatus === "failed");
  const hasValidAttachments = attachments.some((att) => att.uploadStatus === "finished" || !att.uploadStatus);
  const isSendDisabled = isPayloadOversized || isUploading || isAnyFailed || (!promptText.trim() && !hasValidAttachments);
  const effectiveIsStreaming = isStreaming || (optimisticStreaming !== null && (optimisticStreaming === activeSessionId || optimisticStreaming === "__active__"));
  const sendTitle = isPayloadOversized
    ? "Attachments exceed the 50 MB limit"
    : isUploading
    ? "Files are uploading..."
    : isAnyFailed
    ? "Some attachments failed to upload"
    : effectiveIsStreaming
    ? "Queue message"
    : "Send message";

  useEffect(() => {
    if (queuedMessagesProp !== undefined) {
      setQueuedMessages(queuedMessagesProp);
    }
  }, [queuedMessagesProp]);

  const handleDeleteClick = (e: React.MouseEvent) => {
    e.stopPropagation();
    if (activeSession) {
      emit("OnDeleteSession", activeSession.id);
    }
  };

  // Clear pending renames once they appear in props
  useEffect(() => {
    const updatedPending = { ...pendingRenames };
    let changed = false;
    for (const [sessionId, expectedTitle] of Object.entries(pendingRenames)) {
      const session = sessions.find((s) => s.id === sessionId);
      if (session && session.title === expectedTitle) {
        delete updatedPending[sessionId];
        changed = true;
      }
    }
    if (changed) {
      setPendingRenames(updatedPending);
    }
  }, [sessions, pendingRenames]);

  const prevIsStreamingRef = useRef(isStreaming);
  useEffect(() => {
    if (prevIsStreamingRef.current && !isStreaming) {
      setOptimisticStreaming(null);
    }
    prevIsStreamingRef.current = isStreaming;
  }, [isStreaming]);

  useEffect(() => {
    setOptimisticStreaming(null);
  }, [activeSessionId]);

  useEffect(() => {
    if (!isStreaming && optimisticStreaming) {
      const msgs = activeSession?.messages;
      if (msgs && msgs.length > 0 && msgs[msgs.length - 1].role === "assistant") {
        setOptimisticStreaming(null);
      }
    }
  }, [isStreaming, optimisticStreaming, activeSession?.messages]);

  useEffect(() => {
    if (optimisticStreaming) {
      const timer = setTimeout(() => {
        setOptimisticStreaming(null);
      }, 60000);
      return () => clearTimeout(timer);
    }
  }, [optimisticStreaming]);

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [activeSession?.messages, displayMessages.length, effectiveIsStreaming, streamingText, queuedMessages]);

  useEffect(() => {
    if (activeSessionId && sessions.length > 0) {
      const sess = sessions.find((s) => s.id === activeSessionId);
      if (sess?.messages && sess.messages.length > 0) {
        setOptimisticMessages((prev) => {
          const current = prev[activeSessionId];
          if (!current || current.length === 0) return prev;
          const remaining = current.filter(
            (opt) => !sess.messages.some((m) => m.role === "user" && m.content.startsWith(opt.content))
          );
          if (remaining.length === current.length) return prev;
          return { ...prev, [activeSessionId]: remaining };
        });
      }
    }
  }, [sessions, activeSessionId]);

  useEffect(() => {
    if (activeSession?.messages) {
      setQueuedMessages((prev) =>
        prev.filter((q) => !activeSession.messages.some((m) => m.content === q.prompt))
      );
    }
  }, [activeSession?.messages]);

  const adjustTextareaHeight = () => {
    const el = textareaRef.current;
    if (el) {
      el.style.height = "auto";
      el.style.height = `${Math.min(el.scrollHeight, 200)}px`;
    }
  };

  const emit = (eventName: string, ...args: unknown[]) => {
    if (eventHandler && events.includes(eventName)) {
      eventHandler(eventName, id, args);
    }
  };

  const startHeaderTitleEdit = () => {
    if (!activeSession) return;
    setEditingTitleText(activeSession.title || "New Chat");
    setIsEditingTitle(true);
  };

  const saveHeaderTitleEdit = () => {
    if (activeSession && editingTitleText.trim() && editingTitleText.trim() !== activeSession.title) {
      const newTitle = editingTitleText.trim();
      setPendingRenames((prev) => ({ ...prev, [activeSession.id]: newTitle }));
      emit("OnRenameSession", activeSession.id, newTitle);
    }
    setIsEditingTitle(false);
  };

  const attachmentsRef = useRef(attachments);
  useEffect(() => {
    attachmentsRef.current = attachments;
  }, [attachments]);

  useEffect(() => {
    return () => {
      attachmentsRef.current.forEach((att) => {
        if (att.previewUrl) {
          try {
            URL.revokeObjectURL(att.previewUrl);
          } catch {
            // ignore
          }
        }
      });
    };
  }, []);

  const handleSendMessage = () => {
    const trimmed = promptText.trim();
    if (!trimmed && attachments.length === 0) return;
    if (isPayloadOversized || isUploading) return;

    const validAttachments = attachments.filter((att) => att.uploadStatus !== "failed");
    const payloadAttachments = validAttachments.map((att) => ({
      name: att.name,
      contentType: att.contentType,
      size: att.size,
      localPath: att.localPath,
      fileId: att.fileId,
      base64Data: (uploadUrl && att.uploadStatus === "finished") ? undefined : (att.base64Data || undefined),
    }));

    const payload = { prompt: trimmed, attachments: payloadAttachments, sessionId: activeSessionId };
    if (effectiveIsStreaming) {
      setQueuedMessages((prev) => [
        ...prev,
        { id: `q-${Date.now()}-${Math.random()}`, prompt: trimmed, attachments: payloadAttachments },
      ]);
    } else {
      setOptimisticStreaming(activeSessionId || "__active__");
      if (activeSessionId) {
        const optMsg: ChatMessageDto = {
          id: `opt-${Date.now()}-${Math.random()}`,
          role: "user",
          content: trimmed,
          timestamp: new Date().toLocaleTimeString([], { hour: "numeric", minute: "2-digit" }),
          agentId: selectedAgent,
          modelId: selectedModel,
        };
        setOptimisticMessages((prev) => ({
          ...prev,
          [activeSessionId]: [...(prev[activeSessionId] || []), optMsg],
        }));
      }
    }

    emit("OnSendMessage", payload);
    setPromptText("");
    setAttachments([]);
    if (textareaRef.current) {
      textareaRef.current.style.height = "auto";
    }
  };

  const handleCancelStream = () => {
    setOptimisticStreaming(null);
    setQueuedMessages([]);
    emit("OnCancelStream");
  };

  const handleSendQueuedNow = (queueId: string) => {
    const item = queuedMessages.find((q) => q.id === queueId);
    if (!item) return;

    if (events.includes("OnSendQueuedNow")) {
      emit("OnSendQueuedNow", queueId);
    } else {
      const payload = { prompt: item.prompt, attachments: item.attachments, sessionId: activeSessionId };
      emit("OnSendMessage", payload);
    }
    setQueuedMessages((prev) => prev.filter((q) => q.id !== queueId));
  };

  const handleStartEditQueued = (item: ChatQueuedMessageDto) => {
    setEditingQueuedId(item.id);
    setEditingQueuedText(item.prompt);
  };

  const handleSaveEditQueued = (queueId: string) => {
    const trimmed = editingQueuedText.trim();
    if (!trimmed) {
      handleDeleteQueued(queueId);
    } else {
      emit("OnUpdateQueuedMessage", queueId, trimmed);
      setQueuedMessages((prev) =>
        prev.map((q) => (q.id === queueId ? { ...q, prompt: trimmed } : q))
      );
    }
    setEditingQueuedId(null);
    setEditingQueuedText("");
  };

  const handleCancelEditQueued = () => {
    setEditingQueuedId(null);
    setEditingQueuedText("");
  };

  const handleDeleteQueued = (queueId: string) => {
    emit("OnDeleteQueuedMessage", queueId);
    setQueuedMessages((prev) => prev.filter((q) => q.id !== queueId));
    if (editingQueuedId === queueId) {
      setEditingQueuedId(null);
      setEditingQueuedText("");
    }
  };

  const handleProcessFiles = async (filesList: FileList | File[]) => {
    const list = Array.from(filesList);
    if (list.length === 0) return;

    const newAttachments: ChatAttachmentDto[] = [];
    const filesToUpload: { file: File; fileId: string; fileName: string }[] = [];

    for (let i = 0; i < list.length; i++) {
      let file = list[i];
      if (isImageFile(file.type || file.name)) {
        try {
          file = await processImageFile(file);
        } catch {
          // ignore, keep original file
        }
      }
      const mimeType = file.type || "application/octet-stream";
      const ext = mimeType.split("/")[1] || file.name?.split(".").pop() || "bin";
      const fileName =
        file.name && file.name.trim() !== "" && file.name !== "blob"
          ? file.name
          : `file_${Date.now()}_${i}.${ext}`;
      const fileId = `att-${Date.now()}-${i}-${Math.random().toString(36).substring(2, 9)}`;

      let lineCount: number | undefined;
      if (
        (mimeType.startsWith("text/") ||
          fileName.endsWith(".txt") ||
          fileName.endsWith(".log") ||
          fileName.endsWith(".json") ||
          fileName.endsWith(".csv") ||
          fileName.endsWith(".md") ||
          fileName.endsWith(".cs") ||
          fileName.endsWith(".ts") ||
          fileName.endsWith(".tsx") ||
          fileName.endsWith(".js") ||
          fileName.endsWith(".py") ||
          fileName.endsWith(".yaml") ||
          fileName.endsWith(".yml") ||
          fileName.endsWith(".xml") ||
          fileName.endsWith(".html")) &&
        typeof file.text === "function"
      ) {
        try {
          const textContent = await file.text();
          lineCount = textContent.split("\n").length;
        } catch {
          // ignore
        }
      }

      let previewUrl: string | undefined;
      if (isImageFile(mimeType || fileName) || isPdfFile(mimeType || fileName)) {
        try {
          if (typeof URL !== "undefined" && typeof URL.createObjectURL === "function") {
            previewUrl = URL.createObjectURL(file);
          }
        } catch {
          // ignore
        }
      }

      if (uploadUrl) {
        newAttachments.push({
          name: fileName,
          contentType: mimeType,
          size: file.size || 0,
          lineCount,
          previewUrl,
          fileId,
          uploadStatus: "uploading",
          uploadProgress: 0,
        });
        filesToUpload.push({ file, fileId, fileName });
      } else {
        let base64Data = "";
        try {
          if (typeof FileReader !== "undefined") {
            base64Data = await new Promise<string>((resolve) => {
              const reader = new FileReader();
              reader.onload = (evt) => {
                resolve((evt.target?.result as string) || "");
              };
              reader.onerror = () => resolve("");
              reader.readAsDataURL(file);
            });
          }
        } catch {
          base64Data = "";
        }

        newAttachments.push({
          name: fileName,
          contentType: mimeType,
          size: file.size || 0,
          base64Data,
          lineCount,
          previewUrl,
          fileId,
          uploadStatus: "finished",
          uploadProgress: 100,
        });
      }
    }

    setAttachments((prev) => [...prev, ...newAttachments]);

    if (uploadUrl && filesToUpload.length > 0) {
      for (const { file, fileId, fileName } of filesToUpload) {
        const formData = new FormData();
        formData.append("file", file, fileName);

        if (typeof XMLHttpRequest !== "undefined") {
          const xhr = new XMLHttpRequest();
          xhr.open("POST", uploadUrl, true);

          if (xhr.upload) {
            xhr.upload.onprogress = (evt) => {
              if (evt.lengthComputable) {
                const percent = Math.round((evt.loaded / evt.total) * 100);
                setAttachments((prev) =>
                  prev.map((att) =>
                    att.fileId === fileId ? { ...att, uploadProgress: percent } : att
                  )
                );
              }
            };
          }

          xhr.onload = () => {
            if (xhr.status >= 200 && xhr.status < 300) {
              setAttachments((prev) =>
                prev.map((att) =>
                  att.fileId === fileId
                    ? { ...att, uploadStatus: "finished", uploadProgress: 100 }
                    : att
                )
              );
            } else {
              setAttachments((prev) =>
                prev.map((att) =>
                  att.fileId === fileId
                    ? { ...att, uploadStatus: "failed", error: `Upload failed (status ${xhr.status})` }
                    : att
                )
              );
            }
          };

          xhr.onerror = () => {
            setAttachments((prev) =>
              prev.map((att) =>
                att.fileId === fileId
                  ? { ...att, uploadStatus: "failed", error: "Upload failed: Network error" }
                  : att
              )
            );
          };

          xhr.send(formData);
        } else if (typeof fetch !== "undefined") {
          try {
            const resp = await fetch(uploadUrl, {
              method: "POST",
              body: formData,
            });
            if (resp.ok) {
              setAttachments((prev) =>
                prev.map((att) =>
                  att.fileId === fileId
                    ? { ...att, uploadStatus: "finished", uploadProgress: 100 }
                    : att
                )
              );
            } else {
              setAttachments((prev) =>
                prev.map((att) =>
                  att.fileId === fileId
                    ? { ...att, uploadStatus: "failed", error: `Upload failed (status ${resp.status})` }
                    : att
                )
              );
            }
          } catch (err) {
            setAttachments((prev) =>
              prev.map((att) =>
                att.fileId === fileId
                  ? { ...att, uploadStatus: "failed", error: `Upload failed: ${err}` }
                  : att
              )
            );
          }
        }
      }
    }
  };

  const handleFileSelect = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = e.target.files;
    if (!files || files.length === 0) return;
    await handleProcessFiles(files);
    e.target.value = "";
  };

  const removeAttachment = (index: number) => {
    setAttachments((prev) => {
      const target = prev[index];
      if (target?.previewUrl) {
        try {
          URL.revokeObjectURL(target.previewUrl);
        } catch {
          // ignore
        }
      }
      return prev.filter((_, i) => i !== index);
    });
  };

  const handlePaste = async (e: React.ClipboardEvent<HTMLTextAreaElement>) => {
    const items = e.clipboardData?.items;
    const files = e.clipboardData?.files;

    const pastedFiles: File[] = [];

    if (files && files.length > 0) {
      for (let i = 0; i < files.length; i++) {
        pastedFiles.push(files[i]);
      }
    } else if (items && items.length > 0) {
      for (let i = 0; i < items.length; i++) {
        if (items[i].kind === "file") {
          const file = items[i].getAsFile();
          if (file) {
            pastedFiles.push(file);
          }
        }
      }
    }

    if (pastedFiles.length > 0) {
      e.preventDefault();
      await handleProcessFiles(pastedFiles);
    }
  };

  const [isDragging, setIsDragging] = useState(false);

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
    setIsDragging(true);
  };

  const handleDrop = async (e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragging(false);
    if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
      await handleProcessFiles(e.dataTransfer.files);
    }
  };

  const handleTextChange = (e: React.ChangeEvent<HTMLTextAreaElement>) => {
    setPromptText(e.target.value);
    adjustTextareaHeight();
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      handleSendMessage();
    }
  };

  const toggleVoiceRecording = () => {
    if (!("webkitSpeechRecognition" in window || "SpeechRecognition" in window)) {
      alert("Speech recognition is not supported in this browser.");
      return;
    }

    if (isRecording) {
      recognitionRef.current?.stop();
      setIsRecording(false);
      return;
    }

    initialPromptRef.current = promptText;

    const SpeechRecognition =
      (window as any).SpeechRecognition || (window as any).webkitSpeechRecognition;
    const recognition = new SpeechRecognition();
    recognitionRef.current = recognition;
    recognition.continuous = true;
    recognition.interimResults = true;

    recognition.onstart = () => setIsRecording(true);
    recognition.onend = () => setIsRecording(false);
    recognition.onerror = () => setIsRecording(false);

    recognition.onresult = (event: any) => {
      let speechTranscript = "";
      for (let i = 0; i < event.results.length; i++) {
        speechTranscript += event.results[i][0].transcript;
      }
      const base = initialPromptRef.current.trim();
      const nextText = base
        ? `${base} ${speechTranscript.trimStart()}`
        : speechTranscript;
      setPromptText(nextText);
      setTimeout(adjustTextareaHeight, 0);
    };

    recognition.start();
  };

  const agentSelectOptions = agents.map((a) => ({ value: a.id, label: a.label }));
  const modelSelectOptions = models.map((m) => ({ value: m.id, label: m.displayName }));
  const effortSelectOptions = (efforts || []).map((e) => ({ value: e.id, label: e.displayName }));

  const handleAgentChange = (agentId: string) => {
    emit("OnAgentChanged", agentId);
  };

  const handleModelChange = (modelId: string) => {
    emit("OnModelChanged", modelId);
  };

  const handleEffortChange = (effortId: string) => {
    emit("OnEffortChanged", effortId);
  };

  return (
    <div className="chat-widget-root">
      <input
        ref={fileInputRef}
        type="file"
        multiple
        style={{ display: "none" }}
        onChange={handleFileSelect}
      />

      {/* Main Chat Area */}
      <div className="chat-main">
        {/* Header */}
        <div className="chat-main-header">
          <div className="chat-header-title-container">
            {isEditingTitle ? (
              <input
                type="text"
                className="chat-main-title-input"
                value={editingTitleText}
                onChange={(e) => setEditingTitleText(e.target.value)}
                onBlur={saveHeaderTitleEdit}
                onKeyDown={(e) => {
                  if (e.key === "Enter") saveHeaderTitleEdit();
                  if (e.key === "Escape") setIsEditingTitle(false);
                }}
                autoFocus
              />
            ) : (
              <div className="chat-header-title-clickable" onClick={startHeaderTitleEdit} title="Click to rename chat">
                <h1 className="chat-main-title">
                  {(activeSession && pendingRenames[activeSession.id]) || activeSession?.title || "New Chat"}
                </h1>
                <Pencil size={13} className="chat-title-pencil" />
              </div>
            )}
          </div>
          {activeSession && (
            <div className="chat-header-actions">
              <button
                type="button"
                className="chat-header-delete-btn"
                onClick={handleDeleteClick}
                title="Delete chat session"
                aria-label="Delete chat session"
              >
                <Trash2 size={16} />
              </button>
            </div>
          )}
        </div>

        {/* Message List Container */}
        <div className="chat-messages-container">
          {activeSession && displayMessages.length > 0 ? (
            displayMessages.map((msg) => (
              <div key={msg.id} className={`chat-message-row ${msg.role}`}>
                <div className="chat-message-bubble" style={msg.role === "assistant" && msg.rawStream ? { width: "85%", maxWidth: "85%" } : undefined}>
                  {msg.role === "assistant" && (
                    <div className="chat-message-header">
                      <Bot size={13} className="chat-message-author" />
                      <span className="chat-message-author">{msg.agentId || selectedAgent}</span>
                      <span className="chat-message-time">{msg.timestamp}</span>
                    </div>
                  )}
                  {msg.role === "user" ? (
                    (() => {
                      const { prompt, attachedPaths } = parseUserMessageContent(msg.content);
                      return (
                        <div className="chat-user-message-body">
                          {prompt && (
                            <div className="chat-user-prompt-text">
                              {prompt}
                            </div>
                          )}
                          {attachedPaths.length > 0 && (
                            <div className="chat-user-message-attachments">
                              {attachedPaths.map((filePath, idx) => {
                                const fileName = filePath.split(/[/\\]/).pop() || filePath;
                                const ext = fileName.split(".").pop()?.toUpperCase() || "FILE";
                                return (
                                  <div key={idx} className="chat-user-attachment-badge" title={filePath}>
                                    <Paperclip size={12} className="chat-user-attachment-icon" />
                                    <span className="chat-user-attachment-name">{fileName}</span>
                                    <span className="chat-user-attachment-ext">{ext}</span>
                                  </div>
                                );
                              })}
                            </div>
                          )}
                        </div>
                      );
                    })()
                  ) : msg.rawStream ? (
                    <AgentViewer
                      id={`msg-${msg.id}`}
                      jsonStream={msg.rawStream}
                      autoScroll={false}
                      showThinking={true}
                      showSystemEvents={false}
                      showStatusLabel={false}
                      groupToolCalls={true}
                      eventHandler={noopEventHandler}
                    />
                  ) : (
                    msg.content && (
                      <div className="chat-markdown-body">
                        <ReactMarkdown
                          {...getMarkdownPlugins(msg.content)}
                          components={{ code: BlockHandler, blockquote: AlertBlockquote, pre: ({ children }) => <>{children}</> }}
                        >
                          {msg.content}
                        </ReactMarkdown>
                      </div>
                    )
                  )}
                </div>
              </div>
            ))
          ) : (
            <div className="chat-empty-state">
              <MessageSquare size={44} strokeWidth={1.5} />
              <h3 style={{ margin: 0, fontSize: "17px", color: "var(--foreground)" }}>Start a conversation</h3>
              <p style={{ margin: 0, fontSize: "13px" }}>
                Choose an agent and model below to begin chatting.
              </p>
            </div>
          )}

          {effectiveIsStreaming && (
            <div className="chat-message-row assistant">
              <div className="chat-message-bubble" style={{ width: "85%", maxWidth: "85%" }}>
                <div className="chat-message-header">
                  <Bot size={13} className="chat-message-author" />
                  <span className="chat-message-author">{selectedAgent}</span>
                </div>
                <AgentViewer
                  id={`live-chat-${activeSessionId}`}
                  jsonStream={streamingText}
                  autoScroll={true}
                  showThinking={true}
                  showSystemEvents={false}
                  showStatusLabel={true}
                  groupToolCalls={true}
                  eventHandler={noopEventHandler}
                />
              </div>
            </div>
          )}


          <div ref={messagesEndRef} />
        </div>

        {/* Footer & Resizable Input Toolbar */}
        <div className="chat-footer">
          {queuedMessages.length > 0 && (
            <div className="chat-queued-panel">
              <div className="chat-queued-header">
                <div className="chat-queued-header-left">
                  <span className="chat-queued-title">Queued Messages</span>
                  <span className="chat-queued-badge">{queuedMessages.length}</span>
                  <span className="chat-queued-subtitle">Sends after agent finishes working</span>
                </div>
                <div className="chat-queued-header-right">
                  <button
                    type="button"
                    className="chat-queued-toggle-btn"
                    onClick={() => setCollapsedQueue(!collapsedQueue)}
                    title={collapsedQueue ? "Expand queued messages" : "Collapse queued messages"}
                    aria-label={collapsedQueue ? "Expand queued messages" : "Collapse queued messages"}
                  >
                    <ChevronDown className={`chat-queued-chevron ${collapsedQueue ? "collapsed" : ""}`} size={16} />
                  </button>
                </div>
              </div>

              {!collapsedQueue && (
                <div className="chat-queued-list">
                  {queuedMessages.map((q) => (
                    <div key={q.id} className="chat-queued-item">
                      {editingQueuedId === q.id ? (
                        <div className="chat-queued-edit-container">
                          <input
                            type="text"
                            className="chat-queued-edit-input"
                            value={editingQueuedText}
                            onChange={(e) => setEditingQueuedText(e.target.value)}
                            onKeyDown={(e) => {
                              if (e.key === "Enter") handleSaveEditQueued(q.id);
                              if (e.key === "Escape") handleCancelEditQueued();
                            }}
                            autoFocus
                          />
                          <button
                            type="button"
                            className="chat-queued-item-btn save"
                            onClick={() => handleSaveEditQueued(q.id)}
                            title="Save"
                            aria-label="Save"
                          >
                            <Check size={14} />
                          </button>
                          <button
                            type="button"
                            className="chat-queued-item-btn cancel"
                            onClick={handleCancelEditQueued}
                            title="Cancel"
                            aria-label="Cancel"
                          >
                            <X size={14} />
                          </button>
                        </div>
                      ) : (
                        <>
                          <div className="chat-queued-item-text">
                            {q.prompt || (q.attachments && q.attachments.length > 0 ? `${q.attachments.length} attachment${q.attachments.length > 1 ? "s" : ""}` : "")}
                            {q.attachments && q.attachments.length > 0 && (
                              <span className="chat-queued-item-att-count">
                                <Paperclip size={11} />
                                {q.attachments.length}
                              </span>
                            )}
                          </div>
                          <div className="chat-queued-item-actions">
                            <button
                              type="button"
                              className="chat-queued-item-btn send"
                              onClick={() => handleSendQueuedNow(q.id)}
                              title="Send now"
                              aria-label="Send now"
                            >
                              <ArrowRight size={15} />
                            </button>
                            <button
                              type="button"
                              className="chat-queued-item-btn edit"
                              onClick={() => handleStartEditQueued(q)}
                              title="Edit message"
                              aria-label="Edit message"
                            >
                              <Pencil size={15} />
                            </button>
                            <button
                              type="button"
                              className="chat-queued-item-btn delete"
                              onClick={() => handleDeleteQueued(q.id)}
                              title="Delete message"
                              aria-label="Delete message"
                            >
                              <Trash2 size={15} />
                            </button>
                          </div>
                        </>
                      )}
                    </div>
                  ))}
                </div>
              )}
            </div>
          )}
          <div
            className={`chat-input-box ${isDragging ? "dragging" : ""} ${isPayloadOversized ? "oversized" : ""}`}
            onDragEnter={handleDragEnter}
            onDragLeave={handleDragLeave}
            onDragOver={handleDragOver}
            onDrop={handleDrop}
          >
            {isPayloadOversized && (
              <div className="chat-payload-warning" role="alert">
                <span>Attachments exceed the 50 MB limit ({formatFileSize(totalAttachmentSize)} / 50 MB). Please remove or downsize files before sending.</span>
              </div>
            )}

            {/* Attachment preview cards */}
            {attachments.length > 0 && (
              <div className="chat-attachments-row">
                {attachments.map((att, idx) => {
                  const isImage = isImageFile(att.contentType || att.name);
                  const isPdf = isPdfFile(att.contentType || att.name);
                  const previewSrc = att.previewUrl || (att.base64Data && att.base64Data.startsWith("data:") ? att.base64Data : undefined);
                  const metaText = att.lineCount !== undefined ? `${att.lineCount} lines` : formatFileSize(att.size);
                  const badge = getFileExtBadge(att.name);

                  return (
                    <div key={att.fileId || idx} className={`chat-thumbnail-card ${att.uploadStatus === "failed" ? "upload-failed" : ""}`} title={att.name}>
                      {/* Background Preview for images/PDFs */}
                      {(isImage || isPdf) && previewSrc && (
                        <div className="chat-thumbnail-preview-container">
                          {isImage ? (
                            <img className="chat-thumbnail-image-preview" src={previewSrc} alt={att.name} />
                          ) : (
                            <PdfThumbnail url={previewSrc} />
                          )}
                          <div className="chat-thumbnail-preview-overlay" />
                        </div>
                      )}

                      {/* Uploading progress overlay */}
                      {att.uploadStatus === "uploading" && (
                        <div className="chat-thumbnail-uploading-overlay">
                          <div className="chat-thumbnail-progress-bar-container">
                            <div className="chat-thumbnail-progress-bar" style={{ width: `${att.uploadProgress ?? 0}%` }} />
                          </div>
                          <span className="chat-thumbnail-progress-text">{att.uploadProgress ?? 0}%</span>
                        </div>
                      )}

                      {/* Failed badge */}
                      {att.uploadStatus === "failed" && (
                        <div className="chat-thumbnail-failed-badge" title={att.error || "Upload failed"}>
                          Failed
                        </div>
                      )}

                      {/* Overlaid Close Button */}
                      <button
                        type="button"
                        className="chat-thumbnail-card-remove"
                        onClick={() => removeAttachment(idx)}
                        title="Remove file"
                        aria-label="Remove attachment"
                      >
                        <X size={12} />
                      </button>

                      {/* Overlaid File Metadata & Badge */}
                      <div className="chat-thumbnail-content">
                        {!(previewSrc && (isImage || isPdf)) ? (
                          <div style={{ minWidth: 0 }}>
                            <div className="chat-thumbnail-doc-name" title={att.name}>{att.name}</div>
                            <div className="chat-thumbnail-doc-meta">{metaText}</div>
                          </div>
                        ) : (
                          <div />
                        )}
                        <div className="chat-thumbnail-doc-badge">{badge}</div>
                      </div>
                    </div>
                  );
                })}
              </div>
            )}

            <textarea
              ref={textareaRef}
              className="chat-textarea"
              placeholder={`Ask ${selectedAgent}...`}
              value={promptText}
              onChange={handleTextChange}
              onKeyDown={handleKeyDown}
              onPaste={handlePaste}
            />
            <div className="chat-input-actions">
              <div className="chat-input-actions-left">
                <InlineSelect
                  icon={<Bot size={13} />}
                  value={selectedAgent}
                  options={agentSelectOptions}
                  onChange={handleAgentChange}
                  title="Agentic CLI"
                />

                <InlineSelect
                  icon={<Cpu size={13} />}
                  value={selectedModel}
                  options={modelSelectOptions}
                  onChange={handleModelChange}
                  title="Model"
                />

                {supportsEffort && effortSelectOptions.length > 0 && (
                  <InlineSelect
                    icon={<Zap size={13} />}
                    value={selectedEffort}
                    options={effortSelectOptions}
                    onChange={handleEffortChange}
                    title="Effort Level"
                  />
                )}

                <button
                  type="button"
                  className="chat-action-btn"
                  title="Attach file"
                  onClick={() => fileInputRef.current?.click()}
                >
                  <Paperclip size={15} />
                </button>

                <button
                  type="button"
                  className={`chat-voice-btn ${isRecording ? "recording" : ""}`}
                  title="Voice input"
                  onClick={toggleVoiceRecording}
                >
                  <Mic size={15} />
                </button>
              </div>

              <div className="chat-input-actions-right">
                {effectiveIsStreaming ? (
                  <>
                    <button
                      type="button"
                      className="chat-cancel-btn"
                      onClick={handleCancelStream}
                      title="Stop agent"
                    >
                      <Square size={11} fill="#ef4444" />
                      <span>Stop</span>
                    </button>

                    <button
                      type="button"
                      className="chat-send-btn"
                      disabled={isSendDisabled}
                      onClick={handleSendMessage}
                      title={sendTitle}
                    >
                      <span>Queue</span>
                      <kbd className="chat-shortcut-hint">↵</kbd>
                    </button>
                  </>
                ) : (
                  <button
                    type="button"
                    className="chat-send-btn"
                    disabled={isSendDisabled}
                    onClick={handleSendMessage}
                    title={sendTitle}
                  >
                    <span>Send</span>
                    <kbd className="chat-shortcut-hint">↵</kbd>
                  </button>
                )}
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
