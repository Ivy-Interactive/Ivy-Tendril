import React, { useMemo, useState, useEffect, useRef, useCallback } from "react";
import { parseDiff, Diff, Hunk, getChangeKey, tokenize, type ChangeData, type HunkData } from "react-diff-view";
import "react-diff-view/style/index.css";
import "./plan-diff.css";
import Markdown from "react-markdown";
type IvyEventHandler = (eventName: string, widgetId: string, args: any[]) => void;
import { getWidth, getHeight } from "../styles";
import { getMarkdownPlugins } from "../math";
import { MessageSquare } from "lucide-react";
import { refractor } from "refractor/all";
import { prismTheme } from "../prismTheme";
import { getInitials } from "../DraftMarkdown/annotationUtils";

const refractorAdapter = {
  ...refractor,
  highlight: (code: string, language: string) => {
    const res = refractor.highlight(code, language);
    return Array.isArray(res) ? res : res && (res as any).children ? (res as any).children : [];
  },
};

const getStyleForTokenClass = (classNameStr: string): React.CSSProperties | undefined => {
  if (!classNameStr) return undefined;
  const classes = classNameStr.split(" ");
  for (const cls of classes) {
    if (cls === "token") continue;
    const camelCls = cls.replace(/-([a-z])/g, (_, letter) => letter.toUpperCase());
    if (prismTheme[camelCls]) return prismTheme[camelCls];
    if (prismTheme[cls]) return prismTheme[cls];
  }
  return undefined;
};

const customRenderToken = (token: any, renderDefault: any, index: number): React.ReactNode => {
  if (!token || token.type === "text") {
    return renderDefault(token, index);
  }

  const className =
    token.className ||
    (token.properties && Array.isArray(token.properties.className)
      ? token.properties.className.join(" ")
      : "");
  const style = getStyleForTokenClass(className);

  if (style) {
    return (
      <span key={index} className={className} style={style}>
        {token.children && token.children.length > 0
          ? token.children.map((child: any, i: number) => customRenderToken(child, renderDefault, i))
          : token.value}
      </span>
    );
  }

  return renderDefault(token, index);
};



export function getLanguageFromFilePath(filePath: string): string {
  if (!filePath) return "text";
  const ext = filePath.split(".").pop()?.toLowerCase() || "";
  switch (ext) {
    case "cs":
      return "csharp";
    case "js":
    case "cjs":
    case "mjs":
      return "javascript";
    case "jsx":
      return "jsx";
    case "ts":
    case "mts":
    case "cts":
      return "typescript";
    case "tsx":
      return "tsx";
    case "py":
      return "python";
    case "html":
    case "htm":
      return "markup";
    case "css":
      return "css";
    case "json":
      return "json";
    case "xml":
    case "csproj":
    case "props":
    case "targets":
    case "svg":
      return "markup";
    case "md":
    case "markdown":
      return "markdown";
    case "sh":
    case "bash":
    case "zsh":
      return "bash";
    case "yaml":
    case "yml":
      return "yaml";
    case "sql":
      return "sql";
    case "c":
    case "h":
    case "cpp":
    case "hpp":
    case "cc":
    case "cxx":
      return "cpp";
    case "java":
      return "java";
    case "go":
      return "go";
    case "rs":
      return "rust";
    default:
      return ext || "text";
  }
}

/** Container width (px) below which the diff is too cramped for a side-by-side (split) view. */
export const NARROW_BREAKPOINT = 768;

interface DraftComment {
  filePath: string;
  changeKey: string;
  content: string;
  lineNumber: number;
  author?: string;
}

interface PlanDiffViewProps {
  id: string;
  width?: string;
  height?: string;
  eventHandler?: IvyEventHandler;
  /** Legacy/test alias for eventHandler. */
  onIvyEvent?: IvyEventHandler;
  events?: string[];
  diff?: string;
  viewType?: "Unified" | "Split";
  language?: string;
  oldRevision?: string;
  newRevision?: string;
  wordWrap?: boolean;
  collapsible?: boolean;
  defaultCollapsed?: boolean;
  comments?: DraftComment[];
  filePath?: string;
  currentAuthor?: string;
}

function getLineNumber(change: ChangeData | null): number {
  if (!change) return 0;
  if (change.type === "normal") return change.newLineNumber;
  return change.lineNumber;
}

function getBasename(path: string): string {
  const parts = path.split("/");
  return parts[parts.length - 1] || path;
}

/**
 * Tracks whether a container is narrower than {@link NARROW_BREAKPOINT}, measured
 * against the element's own width (via ResizeObserver) rather than the viewport.
 */
export function useIsNarrow(): [React.RefObject<HTMLDivElement | null>, boolean] {
  const ref = useRef<HTMLDivElement | null>(null);
  const [isNarrow, setIsNarrow] = useState(false);

  useEffect(() => {
    const element = ref.current;
    if (!element || typeof ResizeObserver === "undefined") return;

    let animFrameId: number | null = null;

    const update = (width: number) => {
      const next = width > 0 && width < NARROW_BREAKPOINT;
      setIsNarrow((prev) => (prev === next ? prev : next));
    };

    update(element.clientWidth || element.getBoundingClientRect?.().width || 0);

    const observer = new ResizeObserver((entries) => {
      if (entries.length === 0) return;
      const width = entries[0].contentRect.width;
      if (animFrameId !== null) {
        cancelAnimationFrame(animFrameId);
      }
      animFrameId = requestAnimationFrame(() => {
        update(width);
      });
    });

    observer.observe(element);
    return () => {
      if (animFrameId !== null) {
        cancelAnimationFrame(animFrameId);
      }
      observer.disconnect();
    };
  }, []);

  return [ref, isNarrow];
}

interface CommentWidgetContainerProps {
  changeKey: string;
  comments: DraftComment[];
  isEditing?: boolean;
  editingText?: string;
  originalLineText: string;
  onAddComment: (text: string) => void;
  onUpdateComment: (text: string) => void;
  onDeleteComment: () => void;
  onCancelForm: () => void;
  onStartEdit: (text: string) => void;
  onCancelEdit: () => void;
}

const CommentWidgetContainer: React.FC<CommentWidgetContainerProps> = ({
  comments,
  isEditing,
  editingText,
  onAddComment,
  onUpdateComment,
  onDeleteComment,
  onCancelForm,
  onStartEdit,
  onCancelEdit,
}) => {
  const [inputText, setInputText] = useState("");
  const hasComment = comments.length > 0;

  return (
    <div className="diff-comment-widget p-3 bg-[var(--background)] border border-[var(--border)] rounded-md m-2 shadow-sm max-w-[600px] text-xs font-sans">
      {hasComment && !isEditing ? (
        <div className="flex flex-col gap-2">
          {comments.map((comment, idx) => (
            <div key={idx} className="flex flex-col gap-1 border-b border-[var(--border)] pb-2 last:border-0 last:pb-0">
              <div className="flex items-center justify-between text-xs text-[var(--muted-foreground)]">
                <div className="flex items-center gap-1.5 min-w-0">
                  {comment.author?.trim() && (
                    <div
                      className="pmv-comment-avatar"
                      title={comment.author.trim()}
                    >
                      {getInitials(comment.author.trim())}
                    </div>
                  )}
                  <span className="font-medium text-[var(--foreground)] truncate">
                    {comment.author?.trim() ? comment.author.trim() : "Agent Instruction (Draft)"}
                  </span>
                </div>
                <div className="flex items-center gap-1 shrink-0">
                  <button
                    type="button"
                    className="hover:underline hover:text-[var(--foreground)] cursor-pointer"
                    onClick={() => onStartEdit(comment.content)}
                  >
                    Edit
                  </button>
                  <span>&bull;</span>
                  <button
                    type="button"
                    className="hover:underline hover:text-[var(--foreground)] cursor-pointer"
                    onClick={onDeleteComment}
                    title="Mark comment as resolved"
                  >
                    Resolve
                  </button>
                </div>
              </div>
              <div className="diff-comment-markdown leading-relaxed text-sm text-[var(--foreground)] mt-1">
                <Markdown {...getMarkdownPlugins(comment.content)}>{comment.content}</Markdown>
              </div>
            </div>
          ))}
        </div>
      ) : (
        <div className="flex flex-col gap-2">
          <div className="flex items-center justify-between pb-1">
            <span className="text-xs font-medium text-[var(--foreground)]">
              {isEditing ? "Edit Agent Instruction" : "Agent Instruction"}
            </span>
          </div>

          <textarea
            className="w-full min-h-[80px] p-2 text-sm font-sans bg-[var(--background)] border border-[var(--border)] rounded focus:outline-none focus:ring-1 focus:ring-[var(--primary)] resize-y"
            placeholder="Enter instruction for the agent at this line..."
            value={isEditing ? (editingText ?? "") : inputText}
            onChange={(e) => {
              if (isEditing) {
                onStartEdit(e.target.value);
              } else {
                setInputText(e.target.value);
              }
            }}
            autoFocus
          />

          <div className="flex items-center justify-end gap-2 mt-1">
            <button
              type="button"
              className="px-3 py-1 text-xs font-medium border border-[var(--border)] rounded hover:bg-[var(--muted)] transition-colors cursor-pointer"
              onClick={() => {
                if (isEditing) {
                  onCancelEdit();
                } else {
                  onCancelForm();
                }
              }}
            >
              Cancel
            </button>
            <button
              type="button"
              className="px-3 py-1 text-xs font-medium bg-[var(--primary)] text-[var(--primary-foreground)] rounded hover:opacity-90 transition-colors disabled:opacity-50 cursor-pointer"
              disabled={isEditing ? !editingText?.trim() : !inputText.trim()}
              onClick={() => {
                if (isEditing) {
                  onUpdateComment(editingText ?? "");
                } else {
                  onAddComment(inputText);
                  setInputText("");
                }
              }}
            >
              {isEditing ? "Update Comment" : "Add Comment"}
            </button>
          </div>
        </div>
      )}
    </div>
  );
};

export const PlanDiffView: React.FC<PlanDiffViewProps> = ({
  id,
  width,
  height,
  eventHandler,
  onIvyEvent,
  diff,
  viewType = "Unified",
  language,
  oldRevision,
  newRevision,
  wordWrap,
  collapsible = false,
  defaultCollapsed = false,
  comments = [],
  filePath = "",
  currentAuthor,
}) => {
  const dispatchEvent = eventHandler ?? onIvyEvent;
  const files = useMemo(() => {
    if (!diff) return [];
    try {
      const parsed = parseDiff(diff);
      if (parsed && parsed.length > 0) return parsed;
      const p = filePath || "file";
      const syntheticDiff = `diff --git a/${p} b/${p}\n--- a/${p}\n+++ b/${p}\n${diff}`;
      return parseDiff(syntheticDiff);
    } catch {
      return [];
    }
  }, [diff, filePath]);

  const [viewedState, setViewedState] = useState<Record<string, boolean>>({});
  const [activeFormKeys, setActiveFormKeys] = useState<Record<string, boolean>>({});
  const [editingCommentKeys, setEditingCommentKeys] = useState<Record<string, string>>({});
  const [commentsHidden, setCommentsHidden] = useState<Record<string, boolean>>({});

  useEffect(() => {
    setViewedState({});
    setCommentsHidden({});
    setActiveFormKeys({});
    setEditingCommentKeys({});
  }, [id, diff, filePath]);

  const [containerRef, isNarrow] = useIsNarrow();
  const diffViewType = viewType === "Split" ? "split" : "unified";
  const effectiveViewType = isNarrow ? "unified" : diffViewType;
  const effectiveWordWrap = isNarrow || wordWrap;

  const commentsByChangeKey = useMemo(() => {
    const map: Record<string, DraftComment[]> = {};
    for (const c of comments) {
      if (!map[c.changeKey]) {
        map[c.changeKey] = [];
      }
      map[c.changeKey].push(c);
    }
    return map;
  }, [comments]);

  const handleAddComment = (changeKey: string, content: string, lineNumber: number) => {
    dispatchEvent?.("OnAddComment", id, [{
      filePath,
      changeKey,
      content,
      lineNumber,
      author: currentAuthor?.trim() || undefined,
    }]);
  };

  const handleUpdateComment = (changeKey: string, content: string, lineNumber: number) => {
    dispatchEvent?.("OnUpdateComment", id, [{
      filePath,
      changeKey,
      content,
      lineNumber
    }]);
  };

  const handleDeleteComment = (changeKey: string) => {
    const existing = commentsByChangeKey[changeKey]?.[0];
    if (existing) {
      dispatchEvent?.("OnDeleteComment", id, [existing]);
    }
  };

  const getWidgets = (hunks: HunkData[], hideComments = false) => {
    const allChanges = hunks.reduce<ChangeData[]>((result, hunk) => [...result, ...hunk.changes], []);
    const widgets: Record<string, React.ReactNode> = {};

    for (const change of allChanges) {
      const changeKey = getChangeKey(change);
      const lineComments = commentsByChangeKey[changeKey] || [];
      const showForm = activeFormKeys[changeKey];
      const isEditing = editingCommentKeys[changeKey] !== undefined;

      const rawLine = change?.content || "";
      const originalLineText = rawLine.startsWith("+") || rawLine.startsWith("-") || rawLine.startsWith(" ")
        ? rawLine.slice(1)
        : rawLine;

      const visibleComments = hideComments ? [] : lineComments;
      if (visibleComments.length > 0 || showForm || isEditing) {
        widgets[changeKey] = (
          <CommentWidgetContainer
            changeKey={changeKey}
            comments={visibleComments}
            isEditing={isEditing}
            editingText={editingCommentKeys[changeKey]}
            originalLineText={originalLineText}
            onAddComment={(text) => {
              handleAddComment(changeKey, text, getLineNumber(change));
              setActiveFormKeys(prev => ({ ...prev, [changeKey]: false }));
            }}
            onUpdateComment={(text) => {
              handleUpdateComment(changeKey, text, getLineNumber(change));
              setEditingCommentKeys(prev => {
                const copy = { ...prev };
                delete copy[changeKey];
                return copy;
              });
            }}
            onDeleteComment={() => {
              handleDeleteComment(changeKey);
              setActiveFormKeys(prev => ({ ...prev, [changeKey]: false }));
            }}
            onCancelForm={() => {
              setActiveFormKeys(prev => ({ ...prev, [changeKey]: false }));
            }}
            onStartEdit={(text) => {
              setEditingCommentKeys(prev => ({ ...prev, [changeKey]: text }));
            }}
            onCancelEdit={() => {
              setEditingCommentKeys(prev => {
                const copy = { ...prev };
                delete copy[changeKey];
                return copy;
              });
            }}
          />
        );
      }
    }
    return widgets;
  };

  // Per-file display metadata
  const fileMeta = useMemo(() => {
    return files.map((file, fileIndex) => {
      const rawOld = oldRevision || file.oldPath || "";
      const rawNew = newRevision || file.newPath || "";
      const oldName = rawOld === "/dev/null" ? "" : rawOld;
      const newName = rawNew === "/dev/null" ? "" : rawNew;
      const isRename = oldName !== newName && oldName !== "" && newName !== "";
      const hasHeader = Boolean(oldName || newName || filePath || collapsible);
      const elementId = filePath || `${id}-${file.newPath || file.oldPath || `diff-${fileIndex}`}`;
      const label = isRename
        ? `${getBasename(oldName)} → ${getBasename(newName)}`
        : getBasename(newName || oldName || filePath) || `Diff ${fileIndex + 1}`;

      return { oldName, newName, isRename, hasHeader, elementId, label };
    });
  }, [files, id, oldRevision, newRevision, filePath, collapsible]);

  const scrollToFile = useCallback((elementId: string) => {
    if (typeof document === "undefined") return;
    document
      .getElementById(elementId)
      ?.scrollIntoView({ behavior: "smooth", block: "start" });
  }, []);

  // Pre-tokenize all files and hunks once when files/language change, instead of synchronously tokenizing on every render
  const tokensByFile = useMemo(() => {
    return files.map((file, fileIndex) => {
      const meta = fileMeta[fileIndex];
      const effectiveFilePath = filePath || meta?.newName || meta?.oldName || "";
      const fileLang = language || getLanguageFromFilePath(effectiveFilePath);
      if (file.hunks && file.hunks.length > 0) {
        try {
          if (fileLang && refractor.registered(fileLang)) {
            return tokenize(file.hunks, {
              highlight: true,
              refractor: refractorAdapter,
              language: fileLang,
            });
          }
        } catch {
          // Fallback if language tokenization fails
        }
      }
      return undefined;
    });
  }, [files, fileMeta, filePath, language]);

  const style: React.CSSProperties = {
    ...getWidth(width),
    ...getHeight(height),
    ...(height ? { overflow: "auto" } : {}),
  };

  if (!diff || files.length === 0) {
    return (
      <div ref={containerRef} style={style} className="text-[var(--muted-foreground)] p-4 text-sm">
        No diff to display
      </div>
    );
  }

  const showFileDropdown = isNarrow && fileMeta.length > 1;

  function renderFilePath(path: string) {
    const normalized = path.replace(/\\/g, "/");
    const parts = normalized.split("/");
    if (parts.length === 1) {
      return <span className="font-semibold text-[var(--foreground)]">{path}</span>;
    }
    const dir = parts.slice(0, -1).join("/") + "/";
    const file = parts[parts.length - 1];
    return (
      <span className="font-medium text-[var(--muted-foreground)]">
        {dir}
        <span className="font-semibold text-[var(--foreground)]">{file}</span>
      </span>
    );
  }

  return (
    <div ref={containerRef} style={style} className={`ivy-diff-view text-sm${effectiveWordWrap ? " diff-wrap" : ""}`}>
      {showFileDropdown && (
        <div
          className="sticky top-0 z-20 flex items-center gap-2 px-3 py-1.5 bg-[var(--muted)] border-b border-[var(--border)] font-sans"
        >
          <span className="text-xs text-[var(--muted-foreground)] shrink-0">
            {fileMeta.length} files
          </span>
          <select
            aria-label="Jump to file"
            className="flex-1 min-w-0 text-xs px-2 py-1 rounded bg-[var(--background)] text-[var(--foreground)] border border-[var(--border)] font-sans"
            defaultValue=""
            onChange={(e) => {
              if (e.target.value) scrollToFile(e.target.value);
            }}
          >
            <option value="" disabled>
              Jump to file…
            </option>
            {fileMeta.map((meta, fileIndex) => (
              <option key={fileIndex} value={meta.elementId}>
                {meta.label}
              </option>
            ))}
          </select>
        </div>
      )}
      {files.map((file, fileIndex) => {
        const { oldName, newName, isRename, hasHeader, elementId } = fileMeta[fileIndex];
        const effectiveFilePath = filePath || newName || oldName;
        const fileKey = effectiveFilePath || file.newPath || file.oldPath || `diff-${fileIndex}`;

        const isViewed = viewedState[fileKey] ?? false;
        const isCollapsed = collapsible ? isViewed : defaultCollapsed;

        const toggleViewed = () => {
          if (!collapsible) return;
          setViewedState((prev) => ({
            ...prev,
            [fileKey]: !isViewed,
          }));
        };

        const { additions, deletions } = file.hunks
          ? file.hunks.reduce(
              (acc, hunk) => {
                if (hunk.changes) {
                  for (const change of hunk.changes) {
                    if (change.type === "insert") acc.additions++;
                    else if (change.type === "delete") acc.deletions++;
                  }
                }
                return acc;
              },
              { additions: 0, deletions: 0 }
            )
          : { additions: 0, deletions: 0 };

        const renderDiffSquares = (add: number, del: number) => {
          const total = add + del;
          const squares = [];
          for (let i = 0; i < 5; i++) {
            let colorClass = "bg-[var(--border)]"; // neutral grey
            if (total > 0) {
              const ratio = add / total;
              const threshold = (i + 0.5) / 5;
              if (ratio >= threshold) {
                colorClass = "bg-[var(--success)]";
              } else if (del / total >= (5 - i - 0.5) / 5) {
                colorClass = "bg-[var(--destructive)]";
              }
            }
            squares.push(
              <span key={i} className={`w-1.5 h-1.5 rounded-sm ${colorClass}`} />
            );
          }
          return <span className="flex gap-0.5 ml-1.5 items-center">{squares}</span>;
        };

        return (
          <div
            key={fileIndex}
            id={elementId}
            className={`ivy-diff-file border border-[var(--border)] rounded-md ${isCollapsed ? "mb-0" : "mb-1.5"} bg-[var(--background)] overflow-clip`}
            style={{ scrollMarginTop: showFileDropdown ? "2rem" : 0 }}
          >
            {hasHeader && (
              <div
                className="flex items-center justify-between px-3 py-1 text-xs bg-[var(--muted)] text-[var(--muted-foreground)] border-b border-[var(--border)] sticky z-10 font-sans"
                style={{
                  top: showFileDropdown ? "2rem" : "-1px",
                }}
              >
                <div
                  className="flex items-center gap-2 cursor-pointer select-none grow min-w-0"
                  onClick={collapsible ? toggleViewed : undefined}
                >
                  {collapsible && (
                    <svg
                      width="12"
                      height="12"
                      viewBox="0 0 12 12"
                      className="shrink-0 transition-transform duration-150 text-[var(--muted-foreground)]"
                      style={{ transform: isCollapsed ? "rotate(-90deg)" : "rotate(0deg)" }}
                    >
                      <path d="M3 4.5L6 7.5L9 4.5" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
                    </svg>
                  )}
                  {isRename ? (
                    <div className="flex items-center gap-1.5 min-w-0 truncate">
                      {renderFilePath(oldName)}
                      <span className="opacity-40">&rarr;</span>
                      {renderFilePath(newName)}
                    </div>
                  ) : (
                    <div className="truncate">
                      {renderFilePath(newName || oldName || filePath || "Diff")}
                    </div>
                  )}
                </div>

                <div className="flex items-center gap-4 shrink-0 pl-2">
                  {/* Additions / Deletions count */}
                  <span className="flex items-center gap-1 font-mono text-xs tabular-nums">
                    <span className="flex items-center justify-end gap-1 min-w-[4.5rem]">
                      {additions > 0 && <span className="text-[var(--success)]">+{additions}</span>}
                      {deletions > 0 && <span className="text-[var(--destructive)]">-{deletions}</span>}
                    </span>
                    {renderDiffSquares(additions, deletions)}
                  </span>

                  {/* Viewed / Collapsed Checkbox */}
                  <button
                    type="button"
                    role="checkbox"
                    aria-checked={isViewed}
                    onClick={(e) => {
                      e.stopPropagation();
                      toggleViewed();
                    }}
                    className="flex items-center gap-1.5 text-xs text-[var(--muted-foreground)] hover:text-[var(--foreground)] cursor-pointer select-none font-medium focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-[var(--ring)] rounded px-1 py-0.5"
                  >
                    <span
                      className={`size-3.5 shrink-0 rounded-sm border transition-colors flex items-center justify-center ${
                        isViewed
                          ? "bg-[var(--primary)] text-[var(--primary-foreground)] border-[var(--primary)]"
                          : "border-[var(--border)] bg-[var(--background)] hover:bg-[var(--accent)]"
                      }`}
                    >
                      {isViewed && (
                        <svg width="9" height="9" viewBox="0 0 12 12" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
                          <polyline points="2 6 5 9 10 3" />
                        </svg>
                      )}
                    </span>
                    Viewed
                  </button>

                  {/* Comment count / visibility toggle */}
                  {(() => {
                    const fileCommentCount = comments.length;
                    const hidden = commentsHidden[fileKey] ?? false;
                    return (
                      <button
                        type="button"
                        aria-label={hidden ? "Show comments" : "Hide comments"}
                        aria-pressed={!hidden}
                        disabled={fileCommentCount === 0}
                        title={
                          fileCommentCount === 0
                            ? "No comments on this file"
                            : hidden
                              ? `Show ${fileCommentCount} comment(s)`
                              : `Hide ${fileCommentCount} comment(s)`
                        }
                        className="flex items-center gap-1 p-1 rounded hover:bg-[var(--muted)] text-[var(--muted-foreground)] hover:text-[var(--foreground)] transition-colors cursor-pointer disabled:cursor-default disabled:opacity-40 disabled:hover:bg-transparent disabled:hover:text-[var(--muted-foreground)]"
                        onClick={(e) => {
                          e.stopPropagation();
                          if (fileCommentCount === 0) return;
                          setCommentsHidden((prev) => ({ ...prev, [fileKey]: !hidden }));
                        }}
                      >
                        <MessageSquare className="w-3.5 h-3.5" />
                        <span className="font-mono text-xs tabular-nums min-w-4 text-left">
                          {fileCommentCount > 0 ? fileCommentCount : ""}
                        </span>
                      </button>
                    );
                  })()}
                </div>
              </div>
            )}
            {!isCollapsed && (
              <div className="overflow-x-auto">
                <Diff
                  className={`${effectiveViewType === "unified" ? "diff-unified-view" : "diff-split-view"} ${deletions === 0 && additions > 0 ? "diff-no-deletions" : ""} ${additions === 0 && deletions > 0 ? "diff-no-additions" : ""}`}
                  viewType={effectiveViewType}
                  diffType={file.type}
                  hunks={file.hunks}
                  tokens={tokensByFile[fileIndex]}
                  renderToken={customRenderToken}
                  widgets={getWidgets(file.hunks, commentsHidden[fileKey] ?? false)}
                  gutterEvents={{
                    onClick: ({ change }) => {
                      if (change) {
                        const changeKey = getChangeKey(change);
                        setActiveFormKeys((prev) => ({ ...prev, [changeKey]: !prev[changeKey] }));
                      }
                    },
                  }}
                >
                  {(hunks) =>
                    hunks.map((hunk) => (
                      <Hunk key={hunk.content} hunk={hunk} />
                    ))
                  }
                </Diff>
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
};

