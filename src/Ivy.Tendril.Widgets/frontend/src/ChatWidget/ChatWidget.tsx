import React, { useState, useRef, useEffect, useLayoutEffect } from "react";
import { createPortal } from "react-dom";
import { Mic, Bot, Cpu, MessageSquare, ChevronDown, Check, Pencil, Paperclip, X, Square, ArrowRight, Trash2 } from "lucide-react";
import ReactMarkdown from "react-markdown";
import { AgentViewer } from "../AgentViewer";
import { getMarkdownPlugins } from "../math";
import "./chat-widget.css";

export interface ChatMessageDto {
  id: string;
  role: "user" | "assistant";
  content: string;
  timestamp: string;
  agentId?: string;
  modelId?: string;
  rawStream?: string;
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
}

export interface AgentOptionDto {
  id: string;
  label: string;
}

export interface ModelOptionDto {
  id: string;
  displayName: string;
}

export interface ChatAttachmentDto {
  name: string;
  contentType: string;
  size: number;
  base64Data?: string;
  localPath?: string;
}

type IvyEventHandler = (eventName: string, widgetId: string, args: unknown[]) => void;

export interface ChatWidgetProps {
  id: string;
  activeSessionId?: string | null;
  streamingSessionId?: string | null;
  sessions?: ChatSessionDto[];
  agents?: AgentOptionDto[];
  models?: ModelOptionDto[];
  selectedAgent?: string;
  selectedModel?: string;
  isStreaming?: boolean;
  streamingText?: string;
  streamingStream?: { id: string };
  subscribeToStream?: (streamId: string, onData: (data: unknown) => void) => () => void;
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

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

interface QueuedItem {
  id: string;
  prompt: string;
  attachments: ChatAttachmentDto[];
}

export function ChatWidget({
  id,
  activeSessionId,
  streamingSessionId: _streamingSessionId,
  sessions = [],
  agents = [],
  models = [],
  selectedAgent = "claude",
  selectedModel = "opus",
  isStreaming = false,
  streamingText = "",
  streamingStream: _streamingStream,
  subscribeToStream: _subscribeToStream,
  events = [],
  eventHandler,
}: ChatWidgetProps) {
  const [promptText, setPromptText] = useState("");
  const [isRecording, setIsRecording] = useState(false);
  const [isEditingTitle, setIsEditingTitle] = useState(false);
  const [editingTitleText, setEditingTitleText] = useState("");
  const [attachments, setAttachments] = useState<ChatAttachmentDto[]>([]);
  const [queuedMessages, setQueuedMessages] = useState<QueuedItem[]>([]);
  const [collapsedQueue, setCollapsedQueue] = useState(false);
  const [editingQueuedId, setEditingQueuedId] = useState<string | null>(null);
  const [editingQueuedText, setEditingQueuedText] = useState("");
  const [pendingRenames, setPendingRenames] = useState<Record<string, string>>({});

  const messagesEndRef = useRef<HTMLDivElement>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const recognitionRef = useRef<any>(null);
  const initialPromptRef = useRef<string>("");

  const activeSession = sessions.find((s) => s.id === activeSessionId);

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

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [activeSession?.messages, isStreaming, streamingText, queuedMessages]);

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

  const handleSendMessage = () => {
    const trimmed = promptText.trim();
    if (!trimmed && attachments.length === 0) return;

    const payload = { prompt: trimmed, attachments, sessionId: activeSessionId };
    if (isStreaming) {
      setQueuedMessages((prev) => [
        ...prev,
        { id: `q-${Date.now()}-${Math.random()}`, prompt: trimmed, attachments },
      ]);
    }

    emit("OnSendMessage", payload);
    setPromptText("");
    setAttachments([]);
    if (textareaRef.current) {
      textareaRef.current.style.height = "auto";
    }
  };

  const handleCancelStream = () => {
    setQueuedMessages([]);
    emit("OnCancelStream");
  };

  const handleSendQueuedNow = (queueId: string) => {
    const item = queuedMessages.find((q) => q.id === queueId);
    if (!item) return;

    const payload = { prompt: item.prompt, attachments: item.attachments, sessionId: activeSessionId };
    emit("OnSendMessage", payload);
    setQueuedMessages((prev) => prev.filter((q) => q.id !== queueId));
  };

  const handleStartEditQueued = (item: QueuedItem) => {
    setEditingQueuedId(item.id);
    setEditingQueuedText(item.prompt);
  };

  const handleSaveEditQueued = (queueId: string) => {
    const trimmed = editingQueuedText.trim();
    if (!trimmed) {
      handleDeleteQueued(queueId);
    } else {
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
    setQueuedMessages((prev) => prev.filter((q) => q.id !== queueId));
    if (editingQueuedId === queueId) {
      setEditingQueuedId(null);
      setEditingQueuedText("");
    }
  };

  const handleFileSelect = (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = e.target.files;
    if (!files || files.length === 0) return;

    Array.from(files).forEach((file) => {
      const reader = new FileReader();
      reader.onload = (evt) => {
        const base64Data = evt.target?.result as string;
        setAttachments((prev) => [
          ...prev,
          {
            name: file.name,
            contentType: file.type || "application/octet-stream",
            size: file.size,
            base64Data,
          },
        ]);
      };
      reader.readAsDataURL(file);
    });

    e.target.value = "";
  };

  const removeAttachment = (index: number) => {
    setAttachments((prev) => prev.filter((_, i) => i !== index));
  };

  const handlePaste = (e: React.ClipboardEvent<HTMLTextAreaElement>) => {
    const items = e.clipboardData?.items;
    const files = e.clipboardData?.files;

    let hasImage = false;

    if (files && files.length > 0) {
      Array.from(files).forEach((file) => {
        if (file.type.startsWith("image/")) {
          hasImage = true;
          const reader = new FileReader();
          reader.onload = (evt) => {
            const base64Data = evt.target?.result as string;
            setAttachments((prev) => [
              ...prev,
              {
                name: file.name || `screenshot-${Date.now()}.png`,
                contentType: file.type || "image/png",
                size: file.size,
                base64Data,
              },
            ]);
          };
          reader.readAsDataURL(file);
        }
      });
    }

    if (!hasImage && items && items.length > 0) {
      Array.from(items).forEach((item) => {
        if (item.type.startsWith("image/")) {
          hasImage = true;
          const file = item.getAsFile();
          if (file) {
            const reader = new FileReader();
            reader.onload = (evt) => {
              const base64Data = evt.target?.result as string;
              setAttachments((prev) => [
                ...prev,
                {
                  name: file.name || `screenshot-${Date.now()}.png`,
                  contentType: file.type || "image/png",
                  size: file.size,
                  base64Data,
                },
              ]);
            };
            reader.readAsDataURL(file);
          }
        }
      });
    }

    if (hasImage) {
      e.preventDefault();
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

  const handleAgentChange = (val: string) => {
    emit("OnAgentChanged", val);
  };

  const handleModelChange = (val: string) => {
    emit("OnModelChanged", val);
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
                onClick={(e) => {
                  e.stopPropagation();
                  emit("OnDeleteSession", activeSession.id);
                }}
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
          {activeSession && activeSession.messages && activeSession.messages.length > 0 ? (
            activeSession.messages.map((msg) => (
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
                    <div>{msg.content}</div>
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
                        <ReactMarkdown {...getMarkdownPlugins(msg.content)}>
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

          {isStreaming && (
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
                            {q.prompt}
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
          <div className="chat-input-box">
            {/* Attachment preview pills */}
            {attachments.length > 0 && (
              <div className="chat-attachments-row">
                {attachments.map((att, idx) => (
                  <div key={idx} className="chat-attachment-chip">
                    <Paperclip size={12} />
                    <span className="chat-attachment-name">{att.name}</span>
                    <span className="chat-attachment-size">({formatFileSize(att.size)})</span>
                    <button
                      type="button"
                      className="chat-attachment-remove"
                      onClick={() => removeAttachment(idx)}
                    >
                      <X size={12} />
                    </button>
                  </div>
                ))}
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
                {isStreaming ? (
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
                      disabled={!promptText.trim() && attachments.length === 0}
                      onClick={handleSendMessage}
                      title="Queue message"
                    >
                      <span>Queue</span>
                      <kbd className="chat-shortcut-hint">↵</kbd>
                    </button>
                  </>
                ) : (
                  <button
                    type="button"
                    className="chat-send-btn"
                    disabled={!promptText.trim() && attachments.length === 0}
                    onClick={handleSendMessage}
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
