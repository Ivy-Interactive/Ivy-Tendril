import React, { useMemo, useState, useEffect, useRef, useCallback } from "react";
import { parseDiff, Diff, Hunk, getChangeKey, type ChangeData, type HunkData } from "react-diff-view";
import "react-diff-view/style/index.css";
import "./plan-diff.css";
type IvyEventHandler = (eventName: string, widgetId: string, args: any[]) => void;
import { getWidth, getHeight } from "../styles";

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

    const update = (width: number) => {
      setIsNarrow((prev) => {
        const next = width > 0 && width < NARROW_BREAKPOINT;
        return prev === next ? prev : next;
      });
    };

    update(element.getBoundingClientRect().width);

    const observer = new ResizeObserver((entries) => {
      for (const entry of entries) {
        update(entry.contentRect.width);
      }
    });
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  return [ref, isNarrow];
}

interface CommentWidgetContainerProps {
  changeKey: string;
  comments: DraftComment[];
  isEditing?: boolean;
  editingText?: string;
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
                    className="hover:underline hover:text-[var(--foreground)]"
                    onClick={() => onStartEdit(comment.content)}
                  >
                    Edit
                  </button>
                  <span>&bull;</span>
                  <button
                    type="button"
                    className="hover:underline hover:text-[var(--destructive)]"
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
        <div className="flex flex-col gap-2">
          <div className="flex items-center justify-between border-b border-[var(--border)] pb-1">
            <div className="flex gap-2">
              <button
                type="button"
                className={`pb-1 px-1 border-b-2 text-[11px] font-medium transition-all ${
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
                className={`pb-1 px-1 border-b-2 text-[11px] font-medium transition-all ${
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
              className="px-3 py-1 text-[10px] font-medium border border-[var(--border)] rounded hover:bg-[var(--muted)] transition-colors"
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
              className="px-3 py-1 text-[10px] font-medium bg-[var(--primary)] text-[var(--primary-foreground)] rounded hover:opacity-90 transition-colors disabled:opacity-50"
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

      if (lineComments.length > 0 || showForm || isEditing) {
        widgets[changeKey] = (
          <CommentWidgetContainer
            changeKey={changeKey}
            comments={lineComments}
            isEditing={isEditing}
            editingText={editingCommentKeys[changeKey]}
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
      const elementId = `${id}-${file.newPath || file.oldPath || `diff-${fileIndex}`}`;
      const label = isRename
        ? `${getBasename(oldName)} → ${getBasename(newName)}`
        : getBasename(newName || oldName) || `Diff ${fileIndex + 1}`;

      return { oldName, newName, isRename, hasHeader, elementId, label };
    });
  }, [files, id, oldRevision, newRevision]);

  const scrollToFile = useCallback((elementId: string) => {
    if (typeof document === "undefined") return;
    document
      .getElementById(elementId)
      ?.scrollIntoView({ behavior: "smooth", block: "start" });
  }, []);

  const style: React.CSSProperties = {
    ...getWidth(width),
    ...getHeight(height),
    overflow: "auto",
  };

  if (!diff || files.length === 0) {
    return (
      <div ref={containerRef} style={style} className="text-[var(--muted-foreground)] p-4 text-sm">
        No diff to display
      </div>
    );
  }

  const showFileDropdown = isNarrow && fileMeta.length > 1;

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

        const isCollapsed = collapsible
          ? (collapsedState[fileIndex] ?? defaultCollapsed)
          : false;

        const toggleCollapsed = () => {
          if (!collapsible) return;
          setCollapsedState((prev) => ({
            ...prev,
            [fileIndex]: !isCollapsed,
          }));
        };

        return (
          <div key={fileIndex} id={elementId} style={{ scrollMarginTop: showFileDropdown ? "2rem" : 0 }}>
            {hasHeader && (
              <div
                className={`flex items-center gap-2 px-3 py-1.5 text-[11px] bg-[var(--muted)] text-[var(--muted-foreground)] border-b border-[var(--border)] sticky z-10${collapsible ? " cursor-pointer select-none" : ""}`}
                style={{
                  fontFamily: 'var(--font-sans, sans-serif)',
                  top: showFileDropdown ? "2rem" : 0,
                }}
                onClick={collapsible ? toggleCollapsed : undefined}
              >
                {collapsible && (
                  <svg
                    width="12"
                    height="12"
                    viewBox="0 0 12 12"
                    className="shrink-0 transition-transform duration-150"
                    style={{ transform: isCollapsed ? "rotate(-90deg)" : "rotate(0deg)" }}
                  >
                    <path d="M3 4.5L6 7.5L9 4.5" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
                  </svg>
                )}
                {isRename ? (
                  <>
                    <span className="font-semibold">{getBasename(oldName)}</span>
                    <span className="opacity-40">&rarr;</span>
                    <span className="font-semibold">{getBasename(newName)}</span>
                  </>
                ) : (
                  <span className="font-semibold">{getBasename(newName || oldName)}</span>
                )}
              </div>
            )}
            {!isCollapsed && (
              <Diff
                viewType={effectiveViewType}
                diffType={file.type}
                hunks={file.hunks}
                widgets={getWidgets(file.hunks)}
                gutterEvents={{
                  onClick: ({ change }) => {
                    if (change) {
                      const changeKey = getChangeKey(change);
                      setActiveFormKeys(prev => ({ ...prev, [changeKey]: !prev[changeKey] }));
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
            )}
          </div>
        );
      })}
    </div>
  );
};
