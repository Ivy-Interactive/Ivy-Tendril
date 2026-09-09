import React, { useCallback, useEffect, useRef, useState } from "react";
import {
  ArrowRight,
  Check,
  CheckCheck,
  ChevronDown,
  ListPlus,
  LoaderCircle,
  Mic,
  Paperclip,
  Pencil,
  SendHorizontal,
  Sparkles,
  Square,
  Trash2,
  X,
  XCircle,
} from "lucide-react";
import { QuestionsDraftContext, QuestionsSubmitContext } from "../PlanMarkdown/questionsContext";
import type {
  QuestionSubmitCallback,
  QuestionsDraftState,
  QuestionsDraftStore,
} from "../PlanMarkdown/questionsContext";
import { BlockMarkdown } from "../BlockMarkdown";
import { AgentPicker } from "./AgentPicker";
import { AssistantTurn } from "./AssistantTurn";
import { ChatHeader } from "./ChatHeader";
import {
  ComposerAttachmentCard,
  MessageAttachmentChip,
  formatFileSize,
  parseUserMessageContent,
} from "./attachments";
import { formatSystemEvent } from "./systemEvents";
import { useAttachments } from "./useAttachments";
import { DEFAULT_TRANSCRIPTION_URL, useSpeechInput } from "./useSpeechInput";
import { useThreadScroll } from "./useThreadScroll";
import type { ChatMessageDto, ChatQueuedMessageDto, ChatWidgetProps } from "./types";
import "./chat-widget.css";

export type {
  AgentOptionDto,
  ChatAttachmentDto,
  ChatJobDto,
  ChatMessageDto,
  ChatQueuedMessageDto,
  ChatSessionDto,
  ChatWidgetProps,
  EffortOptionDto,
  ModelOptionDto,
} from "./types";
export { MAX_PAYLOAD_BYTES, formatFileSize } from "./attachments";

const newOptimisticMessage = (content: string, agentId: string, modelId: string): ChatMessageDto => ({
  id: `opt-${Date.now()}-${Math.random()}`,
  role: "user",
  content,
  timestamp: new Date().toLocaleTimeString([], { hour: "numeric", minute: "2-digit" }),
  agentId,
  modelId,
});

const SystemEventRow: React.FC<{ message: ChatMessageDto; onOpenPlan?: (planId: string) => void }> = ({
  message,
  onOpenPlan,
}) => {
  const view = formatSystemEvent(message.content);
  const Icon =
    view.kind === "completed"
      ? CheckCheck
      : view.kind === "failed"
        ? XCircle
        : view.kind === "started"
          ? LoaderCircle
          : Sparkles;
  const plan = view.plan;

  return (
    <div className="chat-system-event-row" data-kind={view.kind} title={message.timestamp}>
      <Icon size={16} className="chat-system-event-icon" />
      <span className="chat-system-event-text">
        {view.text}
        {plan && (
          <>
            {" "}
            {onOpenPlan ? (
              <button type="button" className="chat-system-event-plan" onClick={() => onOpenPlan(plan.id)}>
                {plan.label}
              </button>
            ) : (
              <span className="chat-system-event-plan">{plan.label}</span>
            )}
          </>
        )}
        {plan || view.kind !== "info" ? "." : ""}
        {view.detail && <span className="chat-system-event-detail">{view.detail}</span>}
      </span>
    </div>
  );
};

export function ChatWidget({
  id,
  activeSessionId,
  streamingSessionId: _streamingSessionId,
  uploadUrl,
  transcriptionUrl = DEFAULT_TRANSCRIPTION_URL,
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
  runningJobs = [],
  greeting,
  headline = "What Are We Producing Today?",
  events = [],
  eventHandler,
}: ChatWidgetProps) {
  const [promptText, setPromptText] = useState("");
  const [queuedMessages, setQueuedMessages] = useState<ChatQueuedMessageDto[]>(queuedMessagesProp || []);
  const [collapsedQueue, setCollapsedQueue] = useState(false);
  const [editingQueuedId, setEditingQueuedId] = useState<string | null>(null);
  const [editingQueuedText, setEditingQueuedText] = useState("");
  const [pendingRenames, setPendingRenames] = useState<Record<string, string>>({});
  const [optimisticMessages, setOptimisticMessages] = useState<Record<string, ChatMessageDto[]>>({});
  const [optimisticStreaming, setOptimisticStreaming] = useState<string | null>(null);
  const [activeLightboxImage, setActiveLightboxImage] = useState<{ url: string; title: string } | null>(null);
  const [isDragging, setIsDragging] = useState(false);

  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const {
    containerRef: messagesContainerRef,
    spacerRef,
    isAtBottomRef,
    checkIsAtBottom,
    scrollToBottom,
    pinMessage,
    retargetPin,
    clearPin,
  } = useThreadScroll();

  const {
    attachments,
    addFiles,
    removeAttachment,
    clearAttachments,
    totalSize: totalAttachmentSize,
    isPayloadOversized,
    isUploading,
    isAnyFailed,
    hasValidAttachments,
  } = useAttachments(uploadUrl);

  const emit = useCallback(
    (eventName: string, ...args: unknown[]) => {
      if (eventHandler && events.includes(eventName)) {
        eventHandler(eventName, id, args);
      }
    },
    [eventHandler, events, id],
  );

  const adjustTextareaHeight = useCallback(() => {
    const el = textareaRef.current;
    if (el) {
      el.style.height = "auto";
      el.style.height = `${Math.min(el.scrollHeight, 200)}px`;
    }
  }, []);

  const {
    voiceStatus,
    voiceError,
    dismissVoiceError,
    toggle: toggleVoiceRecording,
  } = useSpeechInput(promptText, setPromptText, adjustTextareaHeight, transcriptionUrl);

  const activeSession = sessions.find((s) => s.id === activeSessionId);

  useEffect(() => {
    if (!activeLightboxImage) return;
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") setActiveLightboxImage(null);
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [activeLightboxImage]);

  const sessionSpawnedJobs = activeSession?.spawnedJobs || [];
  const otherRunningJobs = (runningJobs || []).filter((rj) => !sessionSpawnedJobs.some((sj) => sj.id === rj.id));
  const headerJobs = [...sessionSpawnedJobs, ...otherRunningJobs];
  const currentOptimistic = (activeSessionId && optimisticMessages[activeSessionId]) || [];
  const displayMessages = [...(activeSession?.messages || []), ...currentOptimistic];
  const isSendDisabled =
    isPayloadOversized || isUploading || isAnyFailed || (!promptText.trim() && !hasValidAttachments);
  const effectiveIsStreaming =
    isStreaming ||
    (optimisticStreaming !== null && (optimisticStreaming === activeSessionId || optimisticStreaming === "__active__"));
  const sendTitle = isPayloadOversized
    ? "Attachments exceed the 50 MB limit"
    : isUploading
      ? "Files are uploading..."
      : isAnyFailed
        ? "Some attachments failed to upload"
        : effectiveIsStreaming
          ? "Queue message"
          : "Send message";
  const hasThreadContent = displayMessages.length > 0 || effectiveIsStreaming;

  useEffect(() => {
    if (queuedMessagesProp !== undefined) {
      setQueuedMessages((prev) => {
        const optimistic = prev.filter(
          (item) => item.id.startsWith("q-") && !queuedMessagesProp.some((p) => p.prompt === item.prompt),
        );
        return [...queuedMessagesProp, ...optimistic];
      });
    }
  }, [queuedMessagesProp]);

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
    if (changed) setPendingRenames(updatedPending);
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
    setQueuedMessages(queuedMessagesProp || []);
    clearPin();
    isAtBottomRef.current = true;
    scrollToBottom("auto");
    // Only a session switch resets this state.
    // eslint-disable-next-line react-hooks/exhaustive-deps
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
    if (!optimisticStreaming) return;
    const timer = setTimeout(() => setOptimisticStreaming(null), 60000);
    return () => clearTimeout(timer);
  }, [optimisticStreaming]);

  useEffect(() => {
    const container = messagesContainerRef.current;
    if (!container) return;
    const handleScroll = () => {
      isAtBottomRef.current = checkIsAtBottom(container);
    };
    container.addEventListener("scroll", handleScroll, { passive: true });
    return () => container.removeEventListener("scroll", handleScroll);
  }, [checkIsAtBottom, isAtBottomRef, messagesContainerRef]);

  useEffect(() => {
    const container = messagesContainerRef.current;
    if (!container || typeof ResizeObserver === "undefined") return;

    const resizeObserver = new ResizeObserver(() => {
      if (isAtBottomRef.current) scrollToBottom("auto");
    });
    resizeObserver.observe(container);

    const observed = new WeakSet<Element>();
    const observeChildren = (root: Element) => {
      for (const child of Array.from(root.children)) {
        if (!observed.has(child)) {
          resizeObserver.observe(child);
          observed.add(child);
        }
        observeChildren(child);
      }
    };
    observeChildren(container);

    let mutationObserver: MutationObserver | null = null;
    if (typeof MutationObserver !== "undefined") {
      mutationObserver = new MutationObserver((mutations) => {
        for (const m of mutations) {
          m.addedNodes.forEach((n) => {
            if (n.nodeType === Node.ELEMENT_NODE) observeChildren(n as Element);
          });
        }
        if (isAtBottomRef.current) scrollToBottom("auto");
      });
      mutationObserver.observe(container, { childList: true, subtree: true });
    }

    return () => {
      resizeObserver.disconnect();
      mutationObserver?.disconnect();
    };
  }, [scrollToBottom, isAtBottomRef, messagesContainerRef]);

  useEffect(() => {
    if (isAtBottomRef.current) scrollToBottom("auto");
  }, [displayMessages.length, effectiveIsStreaming, streamingText, queuedMessages, scrollToBottom, isAtBottomRef]);

  // An optimistic user message is retired once its server-side copy arrives; a pin follows it over.
  useEffect(() => {
    if (!activeSessionId) return;
    const current = optimisticMessages[activeSessionId];
    if (!current || current.length === 0) return;
    const serverMessages = sessions.find((s) => s.id === activeSessionId)?.messages;
    if (!serverMessages || serverMessages.length === 0) return;

    const remaining = current.filter((opt) => {
      const match = serverMessages.find((m) => m.role === "user" && m.content.startsWith(opt.content));
      if (match) retargetPin(opt.id, match.id);
      return !match;
    });
    if (remaining.length !== current.length) {
      setOptimisticMessages((prev) => ({ ...prev, [activeSessionId]: remaining }));
    }
  }, [sessions, activeSessionId, optimisticMessages, retargetPin]);

  const handleQuestionSubmit = (messageId: string, answers: Record<string, string[]>, responseText: string) => {
    if (!activeSession) return;
    emit("OnAnswerQuestion", { sessionId: activeSession.id, messageId, answers, responseText });
  };

  // `handleQuestionSubmit` closes over `activeSession` and `emit`, both rebuilt every render. A ref
  // to the latest closure plus a per-message-id cached callback keeps the context value identity
  // stable across renders, so a version bump doesn't re-render every question block's consumers.
  const handleQuestionSubmitRef = useRef(handleQuestionSubmit);
  useEffect(() => {
    handleQuestionSubmitRef.current = handleQuestionSubmit;
  });
  const submitHandlersRef = useRef(new Map<string, QuestionSubmitCallback>());
  const submitHandlerFor = (messageId: string): QuestionSubmitCallback => {
    let handler = submitHandlersRef.current.get(messageId);
    if (!handler) {
      handler = (answers, summaryText) => handleQuestionSubmitRef.current(messageId, answers, summaryText);
      submitHandlersRef.current.set(messageId, handler);
    }
    return handler;
  };

  // Holds every message's in-progress question-block drafts, keyed by `${messageId}::${blockKey}`.
  // A ref survives both re-render and remount of the message rows, so a drafted but unsubmitted
  // answer survives session switches for as long as the widget stays mounted.
  const questionDraftsRef = useRef(new Map<string, QuestionsDraftState>());
  const draftStoresRef = useRef(new Map<string, QuestionsDraftStore>());
  const draftStoreFor = (messageId: string): QuestionsDraftStore => {
    let store = draftStoresRef.current.get(messageId);
    if (!store) {
      store = {
        read: (blockKey) => questionDraftsRef.current.get(`${messageId}::${blockKey}`),
        write: (blockKey, state) => {
          questionDraftsRef.current.set(`${messageId}::${blockKey}`, state);
        },
        clear: (blockKey) => {
          questionDraftsRef.current.delete(`${messageId}::${blockKey}`);
        },
      };
      draftStoresRef.current.set(messageId, store);
    }
    return store;
  };

  const handleSendMessage = () => {
    const trimmed = promptText.trim();
    if (!trimmed && attachments.length === 0) return;
    if (isPayloadOversized || isUploading) return;

    const payloadAttachments = attachments
      .filter((att) => att.uploadStatus !== "failed")
      .map((att) => ({
        name: att.name,
        contentType: att.contentType,
        size: att.size,
        localPath: att.localPath,
        fileId: att.fileId,
        base64Data: uploadUrl && att.uploadStatus === "finished" ? undefined : att.base64Data || undefined,
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
        const optMsg = newOptimisticMessage(trimmed, selectedAgent, selectedModel);
        setOptimisticMessages((prev) => ({
          ...prev,
          [activeSessionId]: [...(prev[activeSessionId] || []), optMsg],
        }));
        pinMessage(optMsg.id);
      }
    }

    emit("OnSendMessage", payload);
    setPromptText("");
    clearAttachments();
    if (textareaRef.current) textareaRef.current.style.height = "auto";
  };

  const handleCancelStream = () => {
    setOptimisticStreaming(null);
    setQueuedMessages([]);
    emit("OnCancelStream");
  };

  const handleSendQueuedNow = (queueId: string) => {
    const item = queuedMessages.find((q) => q.id === queueId);
    if (!item) return;

    if (activeSessionId && item.prompt) {
      const optMsg = newOptimisticMessage(item.prompt, selectedAgent, selectedModel);
      setOptimisticMessages((prev) => ({
        ...prev,
        [activeSessionId]: [...(prev[activeSessionId] || []), optMsg],
      }));
      pinMessage(optMsg.id);
    }
    setOptimisticStreaming(activeSessionId || "__active__");

    if (events.includes("OnSendQueuedNow")) {
      emit("OnSendQueuedNow", queueId);
    } else {
      emit("OnSendMessage", {
        prompt: item.prompt,
        attachments: item.attachments,
        sessionId: activeSessionId,
        forceSend: true,
      });
    }
    setQueuedMessages((prev) => prev.filter((q) => q.id !== queueId));
  };

  const handleStartEditQueued = (item: ChatQueuedMessageDto) => {
    setEditingQueuedId(item.id);
    setEditingQueuedText(item.prompt);
  };

  const handleDeleteQueued = (queueId: string) => {
    emit("OnDeleteQueuedMessage", queueId);
    setQueuedMessages((prev) => prev.filter((q) => q.id !== queueId));
    if (editingQueuedId === queueId) {
      setEditingQueuedId(null);
      setEditingQueuedText("");
    }
  };

  const handleSaveEditQueued = (queueId: string) => {
    const trimmed = editingQueuedText.trim();
    if (!trimmed) {
      handleDeleteQueued(queueId);
    } else {
      emit("OnUpdateQueuedMessage", [queueId, trimmed]);
      setQueuedMessages((prev) => prev.map((q) => (q.id === queueId ? { ...q, prompt: trimmed } : q)));
    }
    setEditingQueuedId(null);
    setEditingQueuedText("");
  };

  const handleCancelEditQueued = () => {
    setEditingQueuedId(null);
    setEditingQueuedText("");
  };

  const handleFileSelect = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = e.target.files;
    if (!files || files.length === 0) return;
    await addFiles(files);
    e.target.value = "";
  };

  const handlePaste = async (e: React.ClipboardEvent<HTMLTextAreaElement>) => {
    const items = e.clipboardData?.items;
    const files = e.clipboardData?.files;
    const pastedFiles: File[] = [];

    if (files && files.length > 0) {
      for (let i = 0; i < files.length; i++) pastedFiles.push(files[i]);
    } else if (items && items.length > 0) {
      for (let i = 0; i < items.length; i++) {
        if (items[i].kind === "file") {
          const file = items[i].getAsFile();
          if (file) pastedFiles.push(file);
        }
      }
    }

    if (pastedFiles.length > 0) {
      e.preventDefault();
      await addFiles(pastedFiles);
    }
  };

  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    if (e.dataTransfer) e.dataTransfer.dropEffect = "copy";
    setIsDragging(true);
  };

  const handleDragLeave = (e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragging(false);
  };

  const handleDrop = async (e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragging(false);
    if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
      await addFiles(e.dataTransfer.files);
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

  const openPlan = events.includes("OnOpenPlan") ? (planId: string) => emit("OnOpenPlan", planId) : undefined;
  const title = (activeSession && pendingRenames[activeSession.id]) || activeSession?.title || "New Chat";

  return (
    <div className="chat-widget-root">
      <input ref={fileInputRef} type="file" multiple style={{ display: "none" }} onChange={handleFileSelect} />

      <ChatHeader
        key={activeSessionId ?? "none"}
        title={title}
        editable={!!activeSession}
        jobs={headerJobs}
        spawned={sessionSpawnedJobs.length > 0}
        onRename={(t) => {
          if (!activeSession) return;
          setPendingRenames((prev) => ({ ...prev, [activeSession.id]: t }));
          // One array argument: the host maps a single argument onto the event's string[] value.
          emit("OnRenameSession", [activeSession.id, t]);
        }}
        onDelete={() => activeSession && emit("OnDeleteSession", activeSession.id)}
        onNewChat={() => emit("OnCreateSession")}
        onReviewJobs={() =>
          activeSession &&
          emit("OnSendMessage", {
            prompt: "All spawned jobs have completed. Please review their outcomes with me and suggest next steps.",
            attachments: [],
            sessionId: activeSession.id,
          })
        }
      />

      <div ref={messagesContainerRef} className="chat-messages-container">
        <div className="chat-thread" data-empty={!hasThreadContent}>
          {hasThreadContent ? (
            <>
              {displayMessages.map((msg) => {
                if (msg.role === "system") {
                  return <SystemEventRow key={msg.id} message={msg} onOpenPlan={openPlan} />;
                }

                if (msg.role === "user") {
                  const { prompt, attachedPaths } = parseUserMessageContent(msg.content);
                  return (
                    <div key={msg.id} className="chat-message-row user" data-message-id={msg.id}>
                      <div className="chat-user-bubble" title={msg.timestamp}>
                        {prompt && <div className="chat-user-prompt-text">{prompt}</div>}
                        {attachedPaths.length > 0 && (
                          <div className="chat-user-message-attachments">
                            {attachedPaths.map((filePath, idx) => (
                              <MessageAttachmentChip
                                key={idx}
                                filePath={filePath}
                                onOpenImage={(url, name) => setActiveLightboxImage({ url, title: name })}
                              />
                            ))}
                          </div>
                        )}
                      </div>
                    </div>
                  );
                }

                return (
                  <div key={msg.id} className="chat-message-row assistant" data-message-id={msg.id}>
                    <QuestionsDraftContext.Provider value={draftStoreFor(msg.id)}>
                      <QuestionsSubmitContext.Provider value={submitHandlerFor(msg.id)}>
                        {msg.rawStream ? (
                          <AssistantTurn stream={msg.rawStream} />
                        ) : msg.content ? (
                          <div className="chat-markdown-body">
                            <BlockMarkdown content={msg.content} />
                          </div>
                        ) : null}
                      </QuestionsSubmitContext.Provider>
                    </QuestionsDraftContext.Provider>
                  </div>
                );
              })}

              {effectiveIsStreaming && (
                <div className="chat-message-row assistant chat-message-row--live">
                  <AssistantTurn stream={streamingText} live />
                </div>
              )}
            </>
          ) : (
            <div className="chat-empty-state">
              {greeting && <div className="chat-empty-greeting">{greeting}</div>}
              <div className="chat-empty-headline">{headline}</div>
            </div>
          )}
          <div ref={spacerRef} className="chat-scroll-spacer" aria-hidden="true" />
        </div>
      </div>

      <div className="chat-footer">
        <div className="chat-footer-inner">
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
                            {q.prompt ||
                              (q.attachments && q.attachments.length > 0
                                ? `${q.attachments.length} attachment${q.attachments.length > 1 ? "s" : ""}`
                                : "")}
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
            onDragEnter={handleDragOver}
            onDragLeave={handleDragLeave}
            onDragOver={handleDragOver}
            onDrop={handleDrop}
          >
            {isPayloadOversized && (
              <div className="chat-payload-warning" role="alert">
                <span>
                  Attachments exceed the 50 MB limit ({formatFileSize(totalAttachmentSize)} / 50 MB). Please remove or
                  downsize files before sending.
                </span>
              </div>
            )}

            {voiceError && (
              <div className="chat-voice-error" role="alert">
                <span>{voiceError}</span>
                <button
                  type="button"
                  className="chat-voice-error-close"
                  title="Dismiss"
                  aria-label="Dismiss voice input error"
                  onClick={dismissVoiceError}
                >
                  <X size={14} />
                </button>
              </div>
            )}

            {attachments.length > 0 && (
              <div className="chat-attachments-row">
                {attachments.map((att, idx) => (
                  <ComposerAttachmentCard
                    key={att.fileId || idx}
                    attachment={att}
                    onRemove={() => removeAttachment(idx)}
                  />
                ))}
              </div>
            )}

            <div className="chat-input-row">
              <button
                type="button"
                className="chat-icon-btn chat-attach-btn"
                title="Attach file"
                aria-label="Attach file"
                onClick={() => fileInputRef.current?.click()}
              >
                <Paperclip size={20} />
              </button>

              <textarea
                ref={textareaRef}
                className="chat-textarea"
                placeholder="Ask Tendril anything..."
                rows={1}
                value={promptText}
                onChange={handleTextChange}
                onKeyDown={handleKeyDown}
                onPaste={handlePaste}
              />

              <div className="chat-input-tools">
                <AgentPicker
                  agents={agents}
                  selectedAgent={selectedAgent}
                  selectedModel={selectedModel}
                  selectedEffort={selectedEffort}
                  models={models}
                  efforts={efforts}
                  supportsEffort={supportsEffort}
                  onAgentChange={(agentId) => emit("OnAgentChanged", agentId)}
                  onModelChange={(modelId) => emit("OnModelChanged", modelId)}
                  onEffortChange={(effortId) => emit("OnEffortChanged", effortId)}
                />

                <button
                  type="button"
                  className={`chat-icon-btn chat-voice-btn chat-voice-${voiceStatus}`}
                  title="Voice input"
                  aria-label="Voice input"
                  onClick={toggleVoiceRecording}
                >
                  {voiceStatus === "connecting" || voiceStatus === "processing" ? (
                    <LoaderCircle size={20} className="spin" />
                  ) : voiceStatus === "recording" ? (
                    <Square size={20} />
                  ) : (
                    <Mic size={20} />
                  )}
                </button>

                {effectiveIsStreaming ? (
                  <>
                    <button
                      type="button"
                      className="chat-stop-btn"
                      onClick={handleCancelStream}
                      title="Stop agent"
                      aria-label="Stop agent"
                    >
                      <Square size={12} fill="currentColor" />
                    </button>
                    <button
                      type="button"
                      className="chat-send-btn"
                      disabled={isSendDisabled}
                      onClick={handleSendMessage}
                      title={sendTitle}
                      aria-label="Queue message"
                    >
                      <ListPlus size={16} />
                    </button>
                  </>
                ) : (
                  <button
                    type="button"
                    className="chat-send-btn"
                    disabled={isSendDisabled}
                    onClick={handleSendMessage}
                    title={sendTitle}
                    aria-label="Send message"
                  >
                    <SendHorizontal size={16} />
                  </button>
                )}
              </div>
            </div>
          </div>
        </div>
      </div>

      {activeLightboxImage && (
        <div
          className="chat-image-lightbox-overlay"
          role="dialog"
          aria-modal="true"
          aria-label={activeLightboxImage.title || "Image preview"}
        >
          <div className="chat-image-lightbox-backdrop" onClick={() => setActiveLightboxImage(null)} />
          <div className="chat-image-lightbox-container">
            <button
              type="button"
              className="chat-image-lightbox-close"
              aria-label="Close preview"
              onClick={() => setActiveLightboxImage(null)}
            >
              <X size={18} />
            </button>
            <img
              src={activeLightboxImage.url}
              alt={activeLightboxImage.title || "Preview"}
              className="chat-image-lightbox-img"
              onClick={(e) => e.stopPropagation()}
            />
            {activeLightboxImage.title && <div className="chat-image-lightbox-caption">{activeLightboxImage.title}</div>}
          </div>
        </div>
      )}
    </div>
  );
}
