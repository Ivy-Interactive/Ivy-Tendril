import React, { useMemo, useState } from "react";
import { User, Sparkles, Send, Square } from "lucide-react";
import { useAutoScroll } from "../AgentViewer/use-auto-scroll";
import { AgentViewer } from "../AgentViewer/AgentViewer";
import { getHeight, getWidth } from "../styles";
import "./agent-chat.css";

type IvyEventHandler = (eventName: string, widgetId: string, args: unknown[]) => void;

interface AgentChatMessage {
  sender: "User" | "Assistant";
  content: string;
}

interface AgentChatProps {
  id: string;
  width?: string;
  height?: string;
  eventHandler: IvyEventHandler;
  events?: string[];
  messages?: AgentChatMessage[];
  isStreaming?: boolean;
  stream?: { id: string };
  subscribeToStream?: (streamId: string, onData: (data: unknown) => void) => () => void;
  placeholder?: string;
}

export const AgentChat: React.FC<AgentChatProps> = ({
  id,
  width = "Full",
  height = "Full",
  eventHandler,
  events = [],
  messages = [],
  isStreaming = false,
  stream,
  subscribeToStream,
  placeholder = "Ask the agent anything...",
}) => {
  const [input, setInput] = useState("");

  // Set up auto scroll based on messages and streaming
  const scrollContent = useMemo(() => {
    return [messages, isStreaming];
  }, [messages, isStreaming]);

  const { scrollRef, disableAutoScroll } = useAutoScroll({
    content: scrollContent,
    enabled: true,
    smooth: true,
  });

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!input.trim() || isStreaming) return;
    const textToSend = input.trim();
    setInput("");
    if (events.includes("OnSend") && eventHandler) {
      eventHandler("OnSend", id, [textToSend]);
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      handleSubmit(e);
    }
  };

  const handleCancel = () => {
    if (events.includes("OnCancel") && eventHandler) {
      eventHandler("OnCancel", id, []);
    }
  };

  const shellStyle: React.CSSProperties = {
    ...getWidth(width),
    ...getHeight(height),
  };

  return (
    <div style={shellStyle} className="ac-shell">
      <div
        ref={scrollRef}
        className="ac-body"
        onWheel={disableAutoScroll}
        onTouchMove={disableAutoScroll}
      >
        {messages.map((msg, idx) => (
          <div
            key={idx}
            className={`ac-bubble-row ${msg.sender === "User" ? "ac-bubble-row-user" : "ac-bubble-row-agent"}`}
          >
            <div className="ac-avatar">
              {msg.sender === "User" ? <User size={16} /> : <Sparkles size={16} />}
            </div>
            <div className="ac-bubble">
              <div className="ac-sender-label">
                {msg.sender === "User" ? "You" : "Assistant"}
              </div>
              <div className="ac-content">
                {msg.sender === "User" ? (
                  <p className="whitespace-pre-wrap">{msg.content}</p>
                ) : (
                  <AgentViewer
                    id={`${id}-msg-${idx}`}
                    eventHandler={() => {}}
                    jsonStream={msg.content}
                    showThinking={true}
                    showStatusLabel={false}
                    autoScroll={false}
                  />
                )}
              </div>
            </div>
          </div>
        ))}

        {isStreaming && (
          <div className="ac-bubble-row ac-bubble-row-agent ac-bubble-row-streaming">
            <div className="ac-avatar">
              <Sparkles size={16} />
            </div>
            <div className="ac-bubble ac-bubble-streaming">
              <div className="ac-sender-label">Assistant</div>
              <div className="ac-content">
                <AgentViewer
                  id={`${id}-streaming`}
                  eventHandler={() => {}}
                  stream={stream}
                  subscribeToStream={subscribeToStream}
                  showThinking={true}
                  showStatusLabel={true}
                  autoScroll={true}
                />
              </div>
            </div>
          </div>
        )}
      </div>

      <div className="ac-input-container">
        <form onSubmit={handleSubmit} className="ac-form">
          <textarea
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder={placeholder}
            rows={1}
            disabled={isStreaming}
            className="ac-textarea"
          />
          <div className="ac-actions">
            {isStreaming ? (
              <button type="button" onClick={handleCancel} className="ac-button ac-button-cancel">
                <Square size={14} className="fill-current" />
                Cancel Request
              </button>
            ) : (
              <button
                type="submit"
                disabled={!input.trim()}
                className="ac-button ac-button-send"
              >
                <Send size={14} />
                Send Message
              </button>
            )}
          </div>
        </form>
      </div>
    </div>
  );
};
