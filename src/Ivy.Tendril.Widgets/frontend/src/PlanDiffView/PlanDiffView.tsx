import React, { useMemo, useState, useEffect, useRef, useCallback } from "react";
import { parseDiff, Diff, Hunk, getChangeKey, type ChangeData, type HunkData } from "react-diff-view";
import "react-diff-view/style/index.css";
import "./plan-diff.css";
type IvyEventHandler = (eventName: string, widgetId: string, args: any[]) => void;
import { getWidth, getHeight } from "../styles";
import { Eye, Pencil, Trash2, MoreHorizontal, MessageSquare } from "lucide-react";

/** Container width (px) below which the diff is too cramped for a side-by-side (split) view. */
export const NARROW_BREAKPOINT = 768;

interface DraftComment {
  filePath: string;
  changeKey: string;
  content: string;
  lineNumber: number;
}

interface PlanDiffViewProps {
  id: string;
  width?: string;
  height?: string;
  onIvyEvent: IvyEventHandler;
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

    update(element.clientWidth);

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
  const [activeTab, setActiveTab] = useState<"write" | "preview">("write");

  const hasComment = comments.length > 0;

  return (
    <div className="diff-comment-widget p-3 bg-[var(--background)] border border-[var(--border)] rounded-md m-2 shadow-sm max-w-[600px] text-xs">
      {hasComment && !isEditing ? (
        <div className="flex flex-col gap-2">
          {comments.map((comment, idx) => (
            <div key={idx} className="flex flex-col gap-1 border-b border-[var(--border)] pb-2 last:border-0 last:pb-0">
              <div className="flex items-center justify-between text-[10px] text-[var(--muted-foreground)]">
                <span className="font-medium text-[var(--foreground)]">Agent Instruction (Draft)</span>
                <div className="flex items-center gap-1">
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
                    className="hover:underline hover:text-[var(--destructive)] cursor-pointer"
                    onClick={onDeleteComment}
                  >
                    Delete
                  </button>
                </div>
              </div>
              <div className="whitespace-pre-wrap leading-relaxed text-[11px] text-[var(--foreground)] mt-1">
                {comment.content}
              </div>
            </div>
          ))}
        </div>
      ) : (
        <div className="flex flex-col gap-3">
          <div className="flex flex-col gap-2">
            <div className="flex items-center justify-between border-b border-[var(--border)] pb-1">
              <div className="flex gap-2">
                <button
                  type="button"
                  className={`pb-1 px-1 border-b-2 text-[11px] font-medium transition-all cursor-pointer ${
                    activeTab === "write"
                      ? "border-[var(--primary)] text-[var(--foreground)]"
                      : "border-transparent text-[var(--muted-foreground)] hover:text-[var(--foreground)]"
                  }`}
                  onClick={() => setActiveTab("write")}
                >
                  Write
                </button>
                <button
                  type="button"
                  className={`pb-1 px-1 border-b-2 text-[11px] font-medium transition-all cursor-pointer ${
                    activeTab === "preview"
                      ? "border-[var(--primary)] text-[var(--foreground)]"
                      : "border-transparent text-[var(--muted-foreground)] hover:text-[var(--foreground)]"
                  }`}
                  onClick={() => setActiveTab("preview")}
                >
                  Preview
                </button>
              </div>
              <span className="text-[10px] text-[var(--muted-foreground)]">
                Markdown instruction for the agent
              </span>
            </div>

            {activeTab === "write" ? (
              <textarea
                className="w-full min-h-[80px] p-2 text-[11px] bg-[var(--background)] border border-[var(--border)] rounded focus:outline-none focus:ring-1 focus:ring-[var(--primary)] resize-y"
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
            ) : (
              <div className="w-full min-h-[80px] p-2 text-[11px] bg-[var(--muted)] border border-[var(--border)] rounded overflow-auto whitespace-pre-wrap">
                {(isEditing ? editingText : inputText) || <span className="text-[var(--muted-foreground)] italic">Nothing to preview</span>}
              </div>
            )}

            <div className="flex items-center justify-end gap-2 mt-1">
              <button
                type="button"
                className="px-3 py-1 text-[10px] font-medium border border-[var(--border)] rounded hover:bg-[var(--muted)] transition-colors cursor-pointer"
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
                className="px-3 py-1 text-[10px] font-medium bg-[var(--primary)] text-[var(--primary-foreground)] rounded hover:opacity-90 transition-colors disabled:opacity-50 cursor-pointer"
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
        </div>
      )}
    </div>
  );
};

export const PlanDiffView: React.FC<PlanDiffViewProps> = ({
  id,
  width,
  height,
  onIvyEvent,
  diff,
  viewType = "Unified",
  oldRevision,
  newRevision,
  wordWrap,
  collapsible = false,
  defaultCollapsed = false,
  comments = [],
  filePath = "",
}) => {
  const files = useMemo(() => {
    if (!diff) return [];
    try {
      return parseDiff(diff);
    } catch {
      return [];
    }
  }, [diff]);

  const [collapsedState, setCollapsedState] = useState<Record<number, boolean>>({});
  const [activeFormKeys, setActiveFormKeys] = useState<Record<string, boolean>>({});
  const [editingCommentKeys, setEditingCommentKeys] = useState<Record<string, string>>({});
  const [activeDropdownIndex, setActiveDropdownIndex] = useState<number | null>(null);

  useEffect(() => {
    if (activeDropdownIndex === null) return;
    const handleOutsideClick = (e: MouseEvent) => {
      const target = e.target as HTMLElement;
      if (!target.closest(".diff-more-actions-container")) {
        setActiveDropdownIndex(null);
      }
    };
    document.addEventListener("click", handleOutsideClick);
    return () => {
      document.removeEventListener("click", handleOutsideClick);
    };
  }, [activeDropdownIndex]);

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
    onIvyEvent("OnAddComment", id, [{
      filePath,
      changeKey,
      content,
      lineNumber
    }]);
  };

  const handleUpdateComment = (changeKey: string, content: string, lineNumber: number) => {
    onIvyEvent("OnUpdateComment", id, [{
      filePath,
      changeKey,
      content,
      lineNumber
    }]);
  };

  const handleDeleteComment = (changeKey: string) => {
    const existing = commentsByChangeKey[changeKey]?.[0];
    if (existing) {
      onIvyEvent("OnDeleteComment", id, [existing]);
    }
  };

  const getWidgets = (hunks: HunkData[]) => {
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

      if (lineComments.length > 0 || showForm || isEditing) {
        widgets[changeKey] = (
          <CommentWidgetContainer
            changeKey={changeKey}
            comments={lineComments}
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
      const hasHeader = Boolean(oldName || newName);
      const elementId = filePath || `${id}-${file.newPath || file.oldPath || `diff-${fileIndex}`}`;
      const label = isRename
        ? `${getBasename(oldName)} → ${getBasename(newName)}`
        : getBasename(newName || oldName) || `Diff ${fileIndex + 1}`;

      return { oldName, newName, isRename, hasHeader, elementId, label };
    });
  }, [files, id, oldRevision, newRevision, filePath]);

  const scrollToFile = useCallback((elementId: string) => {
    if (typeof document === "undefined") return;
    document
      .getElementById(elementId)
      ?.scrollIntoView({ behavior: "smooth", block: "start" });
  }, []);

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
    <div ref={containerRef} style={style} className={`ivy-diff-view text-xs${effectiveWordWrap ? " diff-wrap" : ""}`}>
      {showFileDropdown && (
        <div
          className="sticky top-0 z-20 flex items-center gap-2 px-3 py-1.5 bg-[var(--muted)] border-b border-[var(--border)]"
          style={{ fontFamily: 'var(--font-sans, sans-serif)' }}
        >
          <span className="text-[11px] text-[var(--muted-foreground)] shrink-0">
            {fileMeta.length} files
          </span>
          <select
            aria-label="Jump to file"
            className="flex-1 min-w-0 text-[11px] px-2 py-1 rounded bg-[var(--background)] text-[var(--foreground)] border border-[var(--border)]"
            style={{ fontFamily: 'var(--font-sans, sans-serif)' }}
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

        const isCollapsed = collapsedState[fileIndex] ?? defaultCollapsed;

        const toggleCollapsed = () => {
          if (!collapsible) return;
          setCollapsedState((prev) => ({
            ...prev,
            [fileIndex]: !isCollapsed,
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
            className={`border border-[var(--border)] rounded-md ${isCollapsed ? "mb-1" : "mb-2"} bg-[var(--background)]`}
            style={{ scrollMarginTop: showFileDropdown ? "2rem" : 0 }}
          >
            {hasHeader && (
              <div
                className="relative flex items-center justify-between px-3 py-1.5 text-[11px] bg-[var(--muted)] text-[var(--muted-foreground)] border-b border-[var(--border)] sticky top-0 z-10 font-sans rounded-t-md before:absolute before:-top-px before:inset-x-0 before:h-2 before:bg-[var(--muted)] before:rounded-t-md"
                style={{
                  top: showFileDropdown ? "2rem" : 0,
                }}
              >
                <div
                  className="flex items-center gap-2 cursor-pointer select-none grow min-w-0"
                  onClick={collapsible ? toggleCollapsed : undefined}
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
                      {renderFilePath(newName || oldName)}
                    </div>
                  )}
                </div>

                <div className="flex items-center gap-4 shrink-0 pl-2">
                  {/* Additions / Deletions count */}
                  <span className="flex items-center gap-1 font-mono text-[11px]">
                    {additions > 0 && <span className="text-[var(--success)]">+{additions}</span>}
                    {deletions > 0 && <span className="text-[var(--destructive)]">-{deletions}</span>}
                    {renderDiffSquares(additions, deletions)}
                  </span>

                  {/* Viewed / Collapsed Checkbox */}
                  <label className="flex items-center gap-1.5 text-[11px] text-[var(--muted-foreground)] cursor-pointer select-none font-medium">
                    <input
                      type="checkbox"
                      checked={isCollapsed}
                      onChange={(e) => {
                        setCollapsedState((prev) => ({
                          ...prev,
                          [fileIndex]: e.target.checked,
                        }));
                      }}
                      className="size-3.5 rounded-checkbox border border-border bg-background accent-primary text-primary shadow-xs transition-colors cursor-pointer hover:bg-accent/50 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
                    />
                    Viewed
                  </label>

                  {/* Message bubble icon */}
                  <MessageSquare className="w-3.5 h-3.5 text-[var(--muted-foreground)]" />

                  {/* More actions button & dropdown */}
                  <div className="diff-more-actions-container relative">
                    <button
                      type="button"
                      aria-label="More actions"
                      className="p-1 rounded hover:bg-[var(--muted)] text-[var(--muted-foreground)] hover:text-[var(--foreground)] transition-colors flex items-center justify-center cursor-pointer"
                      onClick={(e) => {
                        e.stopPropagation();
                        setActiveDropdownIndex(prev => prev === fileIndex ? null : fileIndex);
                      }}
                    >
                      <MoreHorizontal className="w-3.5 h-3.5" />
                    </button>
                    {activeDropdownIndex === fileIndex && (
                      <div className="absolute right-0 mt-1 z-50 w-36 bg-[var(--background)] border border-[var(--border)] rounded-md shadow-lg py-1 text-xs">
                        <button
                          type="button"
                          className="w-full flex items-center gap-2 px-3 py-1.5 hover:bg-[var(--muted)] text-[var(--foreground)] text-left cursor-pointer"
                          onClick={() => {
                            setActiveDropdownIndex(null);
                            onIvyEvent("OnViewFile", id, [filePath]);
                          }}
                        >
                          <Eye className="w-3.5 h-3.5" />
                          View file
                        </button>
                        <button
                          type="button"
                          className="w-full flex items-center gap-2 px-3 py-1.5 hover:bg-[var(--muted)] text-[var(--foreground)] text-left cursor-pointer"
                          onClick={() => {
                            setActiveDropdownIndex(null);
                            onIvyEvent("OnEditFile", id, [filePath]);
                          }}
                        >
                          <Pencil className="w-3.5 h-3.5" />
                          Edit file
                        </button>
                        <button
                          type="button"
                          className="w-full flex items-center gap-2 px-3 py-1.5 hover:bg-destructive/10 text-[var(--destructive)] text-left border-t border-[var(--border)] cursor-pointer"
                          onClick={() => {
                            setActiveDropdownIndex(null);
                            onIvyEvent("OnDeleteFile", id, [filePath]);
                          }}
                        >
                          <Trash2 className="w-3.5 h-3.5 text-[var(--destructive)]" />
                          Delete file
                        </button>
                      </div>
                    )}
                  </div>
                </div>
              </div>
            )}
            {!isCollapsed && (
              <div className="overflow-x-auto">
                <Diff
                  className={`${effectiveViewType === "unified" ? "diff-unified-view" : "diff-split-view"} ${deletions === 0 ? "diff-no-deletions" : ""} ${additions === 0 ? "diff-no-additions" : ""}`}
                  viewType={effectiveViewType}
                  diffType={file.type}
                  hunks={file.hunks}
                  widgets={getWidgets(file.hunks)}
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
