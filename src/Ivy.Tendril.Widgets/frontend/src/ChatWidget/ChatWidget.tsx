import React, { useState, useRef, useEffect, useLayoutEffect } from "react";
import { createPortal } from "react-dom";
import { Plus, Trash2, Mic, Bot, Cpu, Search, MessageSquare, ChevronDown, Check, Pencil, Paperclip, X, Square, Clock, Loader2 } from "lucide-react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { AgentViewer } from "../AgentViewer";
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
  sessions = [],
  agents = [],
  models = [],
  selectedAgent = "claude",
  selectedModel = "opus",
  isStreaming = false,
  streamingText = "",
  streamingStream,
  subscribeToStream,
  events = [],
  eventHandler,
}: ChatWidgetProps) {
  const [searchTerm, setSearchTerm] = useState("");
  const [promptText, setPromptText] = useState("");
  const [isRecording, setIsRecording] = useState(false);
  const [isEditingTitle, setIsEditingTitle] = useState(false);
  const [editingTitleText, setEditingTitleText] = useState("");
  const [editingSessionId, setEditingSessionId] = useState<string | null>(null);
  const [sidebarEditingText, setSidebarEditingText] = useState("");
  const [attachments, setAttachments] = useState<ChatAttachmentDto[]>([]);
  const [queuedMessages, setQueuedMessages] = useState<QueuedItem[]>([]);

  const messagesEndRef = useRef<HTMLDivElement>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const activeSession = sessions.find((s) => s.id === activeSessionId);

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

  const handleSelectSession = (sessionId: string) => {
    emit("OnSelectSession", sessionId);
  };

  const handleDeleteSession = (sessionId: string, e: React.MouseEvent) => {
    e.stopPropagation();
    emit("OnDeleteSession", sessionId);
  };

  const handleCreateSession = () => {
    emit("OnCreateSession");
  };

  const startHeaderTitleEdit = () => {
    if (!activeSession) return;
    setEditingTitleText(activeSession.title || "New Chat");
    setIsEditingTitle(true);
  };

  const saveHeaderTitleEdit = () => {
    if (activeSession && editingTitleText.trim() && editingTitleText.trim() !== activeSession.title) {
      emit("OnRenameSession", activeSession.id, editingTitleText.trim());
    }
    setIsEditingTitle(false);
  };

  const startSidebarTitleEdit = (sess: ChatSessionDto, e: React.MouseEvent) => {
    e.stopPropagation();
    setEditingSessionId(sess.id);
    setSidebarEditingText(sess.title || "New Chat");
  };

  const saveSidebarTitleEdit = (sessionId: string) => {
    if (sidebarEditingText.trim()) {
      emit("OnRenameSession", sessionId, sidebarEditingText.trim());
    }
    setEditingSessionId(null);
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
      setIsRecording(false);
      return;
    }

    const SpeechRecognition =
      (window as any).SpeechRecognition || (window as any).webkitSpeechRecognition;
    const recognition = new SpeechRecognition();
    recognition.continuous = false;
    recognition.interimResults = true;

    recognition.onstart = () => setIsRecording(true);
    recognition.onend = () => setIsRecording(false);
    recognition.onerror = () => setIsRecording(false);

    recognition.onresult = (event: any) => {
      let transcript = "";
      for (let i = event.resultIndex; i < event.results.length; i++) {
        transcript += event.results[i][0].transcript;
      }
      setPromptText((prev) => {
        const next = prev ? prev + " " + transcript : transcript;
        setTimeout(adjustTextareaHeight, 0);
        return next;
      });
    };

    recognition.start();
  };

  const filteredSessions = sessions.filter(
    (s) =>
      !searchTerm ||
      s.title.toLowerCase().includes(searchTerm.toLowerCase()) ||
      s.messages.some((m) => m.content.toLowerCase().includes(searchTerm.toLowerCase()))
  );

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

      {/* Sidebar */}
      <div className="chat-sidebar">
        <div className="chat-sidebar-header">
          <div className="chat-sidebar-title-row">
            <h2 className="chat-sidebar-title">History</h2>
            <button type="button" className="chat-new-btn" onClick={handleCreateSession} title="New Chat">
              <Plus size={13} />
              <span>New Chat</span>
            </button>
          </div>
          <div className="chat-search-wrapper">
            <Search size={13} className="chat-search-icon" />
            <input
              type="text"
              className="chat-search-input"
              placeholder="Search history..."
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
            />
          </div>
        </div>

        <div className="chat-session-list">
          {filteredSessions.length > 0 ? (
            filteredSessions.map((sess) => {
              const isActive = sess.id === activeSessionId;
              const isEditingThis = editingSessionId === sess.id;
              const status = sess.status || "done";
              const dateStr = new Date(sess.updatedAt).toLocaleTimeString([], {
                month: "numeric",
                day: "numeric",
                hour: "2-digit",
                minute: "2-digit",
              });
              return (
                <div
                  key={sess.id}
                  className={`chat-session-item ${isActive ? "active" : ""}`}
                  onClick={() => handleSelectSession(sess.id)}
                >
                  <div className="chat-session-info">
                    {isEditingThis ? (
                      <input
                        type="text"
                        className="chat-sidebar-rename-input"
                        value={sidebarEditingText}
                        onChange={(e) => setSidebarEditingText(e.target.value)}
                        onBlur={() => saveSidebarTitleEdit(sess.id)}
                        onKeyDown={(e) => {
                          if (e.key === "Enter") saveSidebarTitleEdit(sess.id);
                          if (e.key === "Escape") setEditingSessionId(null);
                        }}
                        onClick={(e) => e.stopPropagation()}
                        autoFocus
                      />
                    ) : (
                      <span className="chat-session-title">{sess.title || "Untitled Chat"}</span>
                    )}
                    <div className="chat-session-meta">
                      {status === "generating" ? (
                        <span className="chat-session-status generating">
                          <Loader2 size={10} className="chat-spin" />
                          <span>Generating</span>
                        </span>
                      ) : status === "waiting" ? (
                        <span className="chat-session-status waiting">
                          <Clock size={10} />
                          <span>Waiting</span>
                        </span>
                      ) : (
                        <span>{dateStr}</span>
                      )}
                      <span>• {sess.agentId}</span>
                    </div>
                  </div>
                  <div className="chat-session-item-actions">
                    <button
                      type="button"
                      className="chat-edit-btn"
                      title="Rename chat"
                      onClick={(e) => startSidebarTitleEdit(sess, e)}
                    >
                      <Pencil size={12} />
                    </button>
                    <button
                      type="button"
                      className="chat-delete-btn"
                      title="Delete chat"
                      onClick={(e) => handleDeleteSession(sess.id, e)}
                    >
                      <Trash2 size={13} />
                    </button>
                  </div>
                </div>
              );
            })
          ) : (
            <div style={{ color: "#71717a", fontSize: "12px", textAlign: "center", padding: "16px" }}>
              No chat history found.
            </div>
          )}
        </div>
      </div>

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
              <div className="chat-header-title-row" onClick={startHeaderTitleEdit} title="Click to rename chat">
                <h1 className="chat-main-title">{activeSession?.title || "New Chat"}</h1>
                <Pencil size={13} className="chat-title-pencil" />
              </div>
            )}
          </div>
          <div className="chat-header-badges">
            <span className="chat-badge">{selectedAgent}</span>
            <span className="chat-badge">{selectedModel}</span>
          </div>
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
                      eventHandler={noopEventHandler}
                    />
                  ) : (
                    msg.content && (
                      <div className="chat-markdown-body">
                        <ReactMarkdown remarkPlugins={[remarkGfm]}>{msg.content}</ReactMarkdown>
                      </div>
                    )
                  )}
                </div>
              </div>
            ))
          ) : (
            <div className="chat-empty-state">
              <MessageSquare size={44} strokeWidth={1.5} />
              <h3 style={{ margin: 0, fontSize: "17px", color: "#fafafa" }}>Start a conversation</h3>
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
                  id="live-chat-agent-viewer"
                  jsonStream={streamingText}
                  stream={streamingStream}
                  subscribeToStream={subscribeToStream}
                  autoScroll={true}
                  showThinking={true}
                  showSystemEvents={false}
                  showStatusLabel={true}
                  eventHandler={noopEventHandler}
                />
              </div>
            </div>
          )}

          {/* Queued Messages */}
          {queuedMessages.map((q) => (
            <div key={q.id} className="chat-message-row user queued">
              <div className="chat-message-bubble queued">
                <div className="chat-queued-badge">
                  <Clock size={11} />
                  <span>Queued</span>
                </div>
                <div>{q.prompt}</div>
              </div>
            </div>
          ))}

          <div ref={messagesEndRef} />
        </div>

        {/* Footer & Resizable Input Toolbar */}
        <div className="chat-footer">
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
                      <span className="chat-shortcut-hint">↵</span>
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
                    <span className="chat-shortcut-hint">↵</span>
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
