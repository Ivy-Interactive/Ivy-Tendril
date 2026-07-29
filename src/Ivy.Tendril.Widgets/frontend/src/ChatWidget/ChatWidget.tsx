import React, { useState, useRef, useEffect } from "react";
import { Plus, Trash2, Mic, Bot, Cpu, MessageSquare } from "lucide-react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import "./chat-widget.css";

export interface ChatMessageDto {
  id: string;
  role: "user" | "assistant";
  content: string;
  timestamp: string;
  agentId?: string;
  modelId?: string;
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

  const activeSession = sessions.find((s) => s.id === activeSessionId);

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [activeSession?.messages, isStreaming, streamingText]);

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
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      handleSendMessage();
    }
  };

  const handleAgentChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
    emit("OnAgentChanged", e.target.value);
  };

  const handleModelChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
    emit("OnModelChanged", e.target.value);
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
      setPromptText((prev) => (prev ? prev + " " + transcript : transcript));
    };

    recognition.start();
  };

  const filteredSessions = sessions.filter(
    (s) =>
      !searchTerm ||
      s.title.toLowerCase().includes(searchTerm.toLowerCase()) ||
      s.messages.some((m) => m.content.toLowerCase().includes(searchTerm.toLowerCase()))
  );

  return (
    <div className="chat-widget-root">
      {/* Sidebar */}
      <div className="chat-sidebar">
        <div className="chat-sidebar-header">
          <div className="chat-sidebar-title-row">
            <h2 className="chat-sidebar-title">Chat History</h2>
          </div>
          <button type="button" className="chat-new-btn" onClick={handleCreateSession}>
            <Plus size={16} />
            <span>New Chat</span>
          </button>
          <div>
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
                    <Trash2 size={14} />
                  </button>
                </div>
              );
            })
          ) : (
            <div style={{ color: "#64748b", fontSize: "13px", textAlign: "center", padding: "16px" }}>
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
                <div className="chat-message-bubble">
                  {msg.role === "assistant" && (
                    <div className="chat-message-header">
                      <Bot size={14} className="chat-message-author" />
                      <span className="chat-message-author">{msg.agentId || selectedAgent}</span>
                      <span className="chat-message-time">{msg.timestamp}</span>
                    </div>
                  )}
                  {msg.role === "user" ? (
                    <div>{msg.content}</div>
                  ) : (
                    <ReactMarkdown remarkPlugins={[remarkGfm]}>{msg.content}</ReactMarkdown>
                  )}
                </div>
              </div>
            ))
          ) : (
            <div className="chat-empty-state">
              <MessageSquare size={48} strokeWidth={1.5} />
              <h3 style={{ margin: 0, fontSize: "18px", color: "#e2e8f0" }}>Start a conversation</h3>
              <p style={{ margin: 0, fontSize: "14px" }}>
                Choose an agent and model below to begin chatting.
              </p>
            </div>
          )}

          {isStreaming && (
            <div className="chat-message-row assistant">
              <div className="chat-message-bubble">
                <div className="chat-message-header">
                  <Bot size={14} className="chat-message-author" />
                  <span className="chat-message-author">{selectedAgent}</span>
                </div>
                <div>{streamingText || "Thinking..."}</div>
              </div>
            </div>
          )}
          <div ref={messagesEndRef} />
        </div>

        {/* Footer & Input Toolbar */}
        <div className="chat-footer">
          <div className="chat-input-box">
            <textarea
              className="chat-textarea"
              placeholder={`Ask ${selectedAgent}...`}
              value={promptText}
              onChange={(e) => setPromptText(e.target.value)}
              onKeyDown={handleKeyDown}
            />
            <div className="chat-input-actions">
              <div className="chat-input-actions-left">
                <div className="chat-inline-select-wrapper" title="Agentic CLI">
                  <Bot size={14} />
                  <select
                    className="chat-inline-select"
                    value={selectedAgent}
                    onChange={handleAgentChange}
                  >
                    {agents.map((a) => (
                      <option key={a.id} value={a.id}>
                        {a.label}
                      </option>
                    ))}
                  </select>
                </div>

                <div className="chat-inline-select-wrapper" title="Model">
                  <Cpu size={14} />
                  <select
                    className="chat-inline-select"
                    value={selectedModel}
                    onChange={handleModelChange}
                  >
                    {models.map((m) => (
                      <option key={m.id} value={m.id}>
                        {m.displayName}
                      </option>
                    ))}
                  </select>
                </div>

                <button
                  type="button"
                  className={`chat-voice-btn ${isRecording ? "recording" : ""}`}
                  title="Voice input"
                  onClick={toggleVoiceRecording}
                >
                  <Mic size={16} />
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
