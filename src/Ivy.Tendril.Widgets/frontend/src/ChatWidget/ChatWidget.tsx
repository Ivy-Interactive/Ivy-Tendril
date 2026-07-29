import React, { useState, useRef, useEffect, useLayoutEffect } from "react";
import { createPortal } from "react-dom";
import { Plus, Trash2, Mic, Bot, Cpu, Search, MessageSquare, ChevronDown, Check } from "lucide-react";
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
}

export interface AgentOptionDto {
  id: string;
  label: string;
}

export interface ModelOptionDto {
  id: string;
  displayName: string;
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
  events = [],
  eventHandler,
}: ChatWidgetProps) {
  const [searchTerm, setSearchTerm] = useState("");
  const [promptText, setPromptText] = useState("");
  const [isRecording, setIsRecording] = useState(false);
  const messagesEndRef = useRef<HTMLDivElement>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  const activeSession = sessions.find((s) => s.id === activeSessionId);

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [activeSession?.messages, isStreaming, streamingText]);

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

  const handleSendMessage = () => {
    if (!promptText.trim() || isStreaming) return;
    emit("OnSendMessage", promptText.trim());
    setPromptText("");
    if (textareaRef.current) {
      textareaRef.current.style.height = "auto";
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
                    <span className="chat-session-title">{sess.title || "Untitled Chat"}</span>
                    <div className="chat-session-meta">
                      <span>{dateStr}</span>
                      <span>• {sess.agentId}</span>
                    </div>
                  </div>
                  <button
                    type="button"
                    className="chat-delete-btn"
                    title="Delete chat"
                    onClick={(e) => handleDeleteSession(sess.id, e)}
                  >
                    <Trash2 size={13} />
                  </button>
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
            <h1 className="chat-main-title">{activeSession?.title || "New Chat"}</h1>
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
                    <ReactMarkdown remarkPlugins={[remarkGfm]}>{msg.content}</ReactMarkdown>
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
                  autoScroll={true}
                  showThinking={true}
                  showSystemEvents={false}
                  showStatusLabel={true}
                  eventHandler={noopEventHandler}
                />
              </div>
            </div>
          )}
          <div ref={messagesEndRef} />
        </div>

        {/* Footer & Resizable Input Toolbar */}
        <div className="chat-footer">
          <div className="chat-input-box">
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
                  className={`chat-voice-btn ${isRecording ? "recording" : ""}`}
                  title="Voice input"
                  onClick={toggleVoiceRecording}
                >
                  <Mic size={15} />
                </button>
              </div>

              <div className="chat-input-actions-right">
                <button
                  type="button"
                  className="chat-send-btn"
                  disabled={!promptText.trim() || isStreaming}
                  onClick={handleSendMessage}
                >
                  <span>Send</span>
                  <span className="chat-shortcut-hint">↵</span>
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
