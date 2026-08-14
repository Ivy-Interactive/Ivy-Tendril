import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import Markdown, { defaultUrlTransform } from "react-markdown";
import "./draft-markdown.css";
import { getHeight, getWidth } from "../styles";
import { BlockHandler } from "../BlockHandler";
import type { MarkdownAnnotation } from "./annotationUtils";
import { applyAnnotationHighlights, getPlainTextOffset, rangeTouchesQuestions } from "./annotationUtils";
import { AddAnnotationPopover, EditAnnotationPopover, SelectionToolbar } from "./AnnotationPopover";
import { AlertBlockquote } from "./AlertBlockquote";
import { ImageRenderer } from "./ImageRenderer";
import { isLocalFileUrl, transformLocalFileUrl } from "./localFiles";
import { getMarkdownPlugins } from "../math";
import { tagQuestionBlocks } from "./questionsSource";
import { QuestionsAnswerContext } from "./questionsContext";
import type { AnswerCallback } from "./questionsContext";

type IvyEventHandler = (eventName: string, widgetId: string, args: unknown[]) => void;

interface DraftMarkdownProps {
  id: string;
  width?: string;
  height?: string;
  content?: string;
  article?: boolean;
  dangerouslyAllowLocalFiles?: boolean;
  annotations?: MarkdownAnnotation[];
  events?: string[];
  eventHandler?: IvyEventHandler;
  slots?: {
    StickyContent?: React.ReactNode[];
  };
}

interface Position {
  top: number;
  left: number;
}

interface SelectionState {
  position: Position;
  startOffset: number;
  endOffset: number;
  selectedText: string;
}

const EMPTY_EVENTS: string[] = [];
const EMPTY_ANNOTATIONS: MarkdownAnnotation[] = [];

export const DraftMarkdown: React.FC<DraftMarkdownProps> = ({
  id,
  width,
  height,
  content = "",
  article = false,
  dangerouslyAllowLocalFiles = false,
  annotations = EMPTY_ANNOTATIONS,
  events = EMPTY_EVENTS,
  eventHandler,
  slots,
}) => {
  const contentRef = useRef<HTMLDivElement>(null);
  const [selectionToolbar, setSelectionToolbar] = useState<SelectionState | null>(null);
  const [addPopover, setAddPopover] = useState<SelectionState | null>(null);
  const [editPopover, setEditPopover] = useState<{ position: Position; annotation: MarkdownAnnotation } | null>(null);

  const annotationsEnabled = events.includes("OnAnnotationsChange");
  const questionsEnabled = events.includes("OnAnswersChange");

  // Reports one changed question. `Answer` carries all three states without a sentinel: null
  // clears the question back to unanswered, an empty list is an explicit skip, and a non-empty
  // list is the answer itself. The document is never merged here — the host decides how and
  // whether to persist it.
  const handleAnswer = useCallback<AnswerCallback>(
    (questionId, answer) => {
      if (!eventHandler) return;

      const value =
        answer === undefined ? null : answer === null ? [] : Array.isArray(answer) ? answer : [answer];

      eventHandler("OnAnswersChange", id, [{ QuestionId: questionId, Answer: value }]);
    },
    [eventHandler, id],
  );

  // undefined puts every callout in read-only mode, mirroring how annotations gate on their event.
  const answerCallback = questionsEnabled ? handleAnswer : undefined;

  const fireAnnotationsChange = useCallback(
    (newAnnotations: MarkdownAnnotation[]) => {
      if (eventHandler) {
        eventHandler("OnAnnotationsChange", id, [newAnnotations]);
      }
    },
    [eventHandler, id],
  );

  // Apply highlights after render
  useEffect(() => {
    if (contentRef.current && annotationsEnabled) {
      applyAnnotationHighlights(contentRef.current, annotations);
    }
  }, [annotations, content, annotationsEnabled]);

  // Detect text selection
  useEffect(() => {
    if (!annotationsEnabled) return;
    const container = contentRef.current;
    if (!container) return;

    const handleMouseUp = () => {
      const selection = window.getSelection();
      if (!selection || selection.isCollapsed || !selection.rangeCount) {
        return;
      }

      const range = selection.getRangeAt(0);
      if (!container.contains(range.commonAncestorContainer)) {
        return;
      }

      if (rangeTouchesQuestions(container, range)) {
        return;
      }

      const selectedText = selection.toString().trim();
      if (!selectedText) return;

      const startOffset = getPlainTextOffset(container, range.startContainer, range.startOffset);
      const endOffset = getPlainTextOffset(container, range.endContainer, range.endOffset);

      const rect = range.getBoundingClientRect();
      setSelectionToolbar({
        position: { top: rect.bottom + 4, left: rect.left },
        startOffset,
        endOffset,
        selectedText,
      });
    };

    container.addEventListener("mouseup", handleMouseUp);
    return () => container.removeEventListener("mouseup", handleMouseUp);
  }, [annotationsEnabled]);

  // Dismiss selection toolbar on outside mousedown
  useEffect(() => {
    if (!selectionToolbar) return;
    const handleMouseDown = (e: MouseEvent) => {
      const target = e.target as HTMLElement;
      if (target.closest(".pmv-selection-toolbar")) return;
      setSelectionToolbar(null);
    };
    document.addEventListener("mousedown", handleMouseDown);
    return () => document.removeEventListener("mousedown", handleMouseDown);
  }, [selectionToolbar]);

  // Click on existing annotation marks
  useEffect(() => {
    if (!annotationsEnabled) return;
    const container = contentRef.current;
    if (!container) return;

    const handleClick = (e: MouseEvent) => {
      const mark = (e.target as HTMLElement).closest("mark[data-annotation-id]") as HTMLElement | null;
      if (!mark) return;

      const annotationId = mark.dataset.annotationId;
      const annotation = annotations.find((a) => a.id === annotationId);
      if (!annotation) return;

      const rect = mark.getBoundingClientRect();
      setEditPopover({
        position: { top: rect.bottom + 4, left: rect.left },
        annotation,
      });
    };

    container.addEventListener("click", handleClick);
    return () => container.removeEventListener("click", handleClick);
  }, [annotationsEnabled, annotations]);

  const handleAddComment = useCallback(() => {
    if (!selectionToolbar) return;
    setAddPopover(selectionToolbar);
    setSelectionToolbar(null);
    window.getSelection()?.removeAllRanges();
  }, [selectionToolbar]);

  // Keyboard shortcuts while a selection toolbar is showing:
  // Cmd/Ctrl+Alt+M opens the comment dialog, Escape dismisses the toolbar.
  useEffect(() => {
    if (!selectionToolbar) return;
    const handleKeyDown = (e: KeyboardEvent) => {
      // Match the physical key (e.code) so Windows AltGr (Ctrl+Alt) and non-US
      // layouts, which can remap e.key, still trigger the shortcut.
      if (e.code === "KeyM" && (e.metaKey || e.ctrlKey) && e.altKey) {
        e.preventDefault();
        handleAddComment();
      } else if (e.key === "Escape") {
        setSelectionToolbar(null);
      }
    };
    document.addEventListener("keydown", handleKeyDown);
    return () => document.removeEventListener("keydown", handleKeyDown);
  }, [selectionToolbar, handleAddComment]);

  const handleAddAnnotation = useCallback(
    (comment: string) => {
      if (!addPopover) return;
      const newAnnotation: MarkdownAnnotation = {
        id: Math.random().toString(36).slice(2, 10),
        startOffset: addPopover.startOffset,
        endOffset: addPopover.endOffset,
        selectedText: addPopover.selectedText,
        comment,
      };
      fireAnnotationsChange([...annotations, newAnnotation]);
      setAddPopover(null);
    },
    [addPopover, annotations, fireAnnotationsChange],
  );

  const handleEditAnnotation = useCallback(
    (comment: string) => {
      if (!editPopover) return;
      const updated = annotations.map((a) => (a.id === editPopover.annotation.id ? { ...a, comment } : a));
      fireAnnotationsChange(updated);
      setEditPopover(null);
    },
    [editPopover, annotations, fireAnnotationsChange],
  );

  const handleRemoveAnnotation = useCallback(() => {
    if (!editPopover) return;
    const filtered = annotations.filter((a) => a.id !== editPopover.annotation.id);
    fireAnnotationsChange(filtered);
    setEditPopover(null);
  }, [editPopover, annotations, fireAnnotationsChange]);

  const handleLinkClick = useCallback(
    (href: string) => {
      if (events.includes("OnLinkClick") && eventHandler) {
        eventHandler("OnLinkClick", id, [href]);
      }
    },
    [events, eventHandler, id],
  );

  const anchor = useCallback(
    (props: React.AnchorHTMLAttributes<HTMLAnchorElement>) => {
      const { href, children, ...rest } = props;
      const isLocalFile =
        !!href && (href.startsWith("file:") || (!/^[a-z]+:\/\//i.test(href) && !href.startsWith("#")));
      if (isLocalFile && !dangerouslyAllowLocalFiles) {
        return <span {...rest}>{children}</span>;
      }
      return (
        <a
          href={href}
          {...rest}
          onClick={(e) => {
            if (events.includes("OnLinkClick") && href) {
              e.preventDefault();
              handleLinkClick(href);
            }
          }}
        >
          {children}
        </a>
      );
    },
    [events, dangerouslyAllowLocalFiles, handleLinkClick],
  );

  // react-markdown's default transform strips file:// URLs. When local files
  // are allowed, route image sources through the host's /ivy/local-file proxy
  // (the browser cannot load file:// from a served page) and preserve file://
  // URLs on links so the anchor renderer / OnLinkClick can handle them.
  const urlTransform = useCallback(
    (url: string, key: string) => {
      if (dangerouslyAllowLocalFiles && isLocalFileUrl(url)) {
        return transformLocalFileUrl(url, key);
      }
      return defaultUrlTransform(url);
    },
    [dangerouslyAllowLocalFiles],
  );

  // react-markdown wraps an overridden `code` element in a `pre`. A question form is flow content
  // and does not belong inside one — inputs there inherit the monospace stack — so the wrapper is
  // dropped for questions blocks only.
  const pre = useCallback((props: React.HTMLAttributes<HTMLPreElement>) => {
    const { children, ...rest } = props;
    const only = React.Children.count(children) === 1 ? React.Children.toArray(children)[0] : null;

    if (React.isValidElement<{ className?: string }>(only)) {
      if (/(^|\s)language-questions(_\d+)?(\s|$)/.test(String(only.props.className ?? ""))) {
        return <>{children}</>;
      }
    }

    return <pre {...rest}>{children}</pre>;
  }, []);

  // Math plugins are added only when the content actually contains math
  // delimiters, so plain plan markdown skips the extra KaTeX pass entirely.
  const plugins = useMemo(() => getMarkdownPlugins(content), [content]);

  // Each top-level `questions` fence gets its index stamped onto its info line, which is how the
  // renderer learns which block it is dispatching. The info line is never rendered, so annotation
  // offsets — computed over rendered text — are unaffected.
  const tagged = useMemo(() => tagQuestionBlocks(content), [content]);

  const fixed = slots?.StickyContent;
  const hasFixed = !!fixed && React.Children.count(fixed) > 0;

  const shellStyle: React.CSSProperties = {
    ...getWidth(width),
    ...getHeight(height),
  };

  return (
    <div className="pmv-shell" style={shellStyle}>
      <div className="pmv-body">
        <div ref={contentRef} className={article ? "pmv-markdown pmv-article" : "pmv-markdown"}>
          <QuestionsAnswerContext.Provider value={answerCallback}>
            <Markdown
              remarkPlugins={plugins.remarkPlugins}
              rehypePlugins={plugins.rehypePlugins}
              urlTransform={urlTransform}
              components={{ a: anchor, code: BlockHandler, pre, blockquote: AlertBlockquote, img: ImageRenderer }}
            >
              {tagged}
            </Markdown>
          </QuestionsAnswerContext.Provider>
        </div>
      </div>
      {hasFixed && <div className="pmv-sticky">{fixed}</div>}

      {annotationsEnabled && selectionToolbar && (
        <SelectionToolbar position={selectionToolbar.position} onAddComment={handleAddComment} />
      )}
      {annotationsEnabled && addPopover && (
        <AddAnnotationPopover
          position={addPopover.position}
          selectedText={addPopover.selectedText}
          onAdd={handleAddAnnotation}
          onCancel={() => setAddPopover(null)}
        />
      )}
      {annotationsEnabled && editPopover && (
        <EditAnnotationPopover
          position={editPopover.position}
          annotation={editPopover.annotation}
          onSave={handleEditAnnotation}
          onRemove={handleRemoveAnnotation}
          onCancel={() => setEditPopover(null)}
        />
      )}
    </div>
  );
};
