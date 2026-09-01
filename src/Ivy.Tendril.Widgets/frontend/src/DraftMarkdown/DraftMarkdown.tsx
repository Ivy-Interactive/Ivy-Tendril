import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import Markdown, { defaultUrlTransform } from "react-markdown";
import "./draft-markdown.css";
import { getHeight, getWidth } from "../styles";
import { BlockHandler } from "../BlockHandler";
import type { MarkdownAnnotation } from "./annotationUtils";
import { applyAnnotationHighlights, getPlainTextOffset, QUESTIONS_SELECTOR, rangeTouchesQuestions } from "./annotationUtils";
import { AddAnnotationPopover, EditAnnotationPopover, SelectionToolbar } from "./AnnotationPopover";
import { AlertBlockquote } from "./AlertBlockquote";
import { ImageRenderer } from "./ImageRenderer";
import { isLocalFileUrl, transformLocalFileUrl } from "./localFiles";
import { getMarkdownPlugins } from "../math";
import { tagQuestionBlocks } from "./questionsSource";
import { QuestionsAnswerContext } from "./questionsContext";
import type { AnswerCallback } from "./questionsContext";
import { useAnchoredPosition } from "./useAnchoredPosition";
import { SearchOverlay } from "./SearchOverlay";
import { applySearchHighlights, clearSearchHighlights } from "./searchUtils";

type IvyEventHandler = (eventName: string, widgetId: string, args: unknown[]) => void;

interface DraftMarkdownProps {
  id: string;
  width?: string;
  height?: string;
  content?: string;
  article?: boolean;
  dangerouslyAllowLocalFiles?: boolean;
  annotations?: MarkdownAnnotation[];
  scrollTo?: { questionId: string; token: number } | null;
  currentAuthor?: string;
  events?: string[];
  eventHandler?: IvyEventHandler;
  slots?: {
    StickyContent?: React.ReactNode[];
  };
}

interface SelectionState {
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
  scrollTo,
  currentAuthor,
  events = EMPTY_EVENTS,
  eventHandler,
  slots,
}) => {
  const contentRef = useRef<HTMLDivElement>(null);
  const shellRef = useRef<HTMLDivElement>(null);
  const [selectionToolbar, setSelectionToolbar] = useState<SelectionState | null>(null);
  const [addPopover, setAddPopover] = useState<SelectionState | null>(null);
  const [editPopover, setEditPopover] = useState<MarkdownAnnotation | null>(null);

  // In-page search state
  const [isSearchOpen, setIsSearchOpen] = useState(false);
  const [searchQuery, setSearchQuery] = useState("");
  const [matches, setMatches] = useState<HTMLElement[]>([]);
  const [activeMatchIndex, setActiveMatchIndex] = useState(-1);

  // Re-measured on scroll/resize so the fixed-position toolbar/popovers stay
  // lined up with the text they anchor to, instead of the one-shot rect
  // captured at mouseup/click time.
  const selectionAnchor = useAnchoredPosition(contentRef, shellRef, selectionToolbar);
  const addAnchor = useAnchoredPosition(contentRef, shellRef, addPopover);
  const editAnchor = useAnchoredPosition(contentRef, shellRef, editPopover);

  const annotationsEnabled = events.includes("OnAnnotationsChange");
  const questionsEnabled = events.includes("OnAnswersChange");

  // Keyboard shortcut Ctrl+F / Cmd+F to open in-page search
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if ((e.key === "f" || e.key === "F" || e.code === "KeyF") && (e.metaKey || e.ctrlKey) && !e.altKey) {
        e.preventDefault();
        setIsSearchOpen(true);
      }
    };
    document.addEventListener("keydown", handleKeyDown);
    return () => document.removeEventListener("keydown", handleKeyDown);
  }, []);

  // Update search highlights when search query, open state, or markdown content changes
  useEffect(() => {
    if (!contentRef.current) return;
    if (isSearchOpen && searchQuery.trim()) {
      const foundMarks = applySearchHighlights(contentRef.current, searchQuery);
      setMatches(foundMarks);
      if (foundMarks.length > 0) {
        setActiveMatchIndex(0);
      } else {
        setActiveMatchIndex(-1);
      }
    } else {
      clearSearchHighlights(contentRef.current);
      setMatches([]);
      setActiveMatchIndex(-1);
    }
  }, [isSearchOpen, searchQuery, content]);

  // Update active highlight class and scroll into view when activeMatchIndex changes
  useEffect(() => {
    if (!isSearchOpen || matches.length === 0 || activeMatchIndex < 0) return;
    matches.forEach((mark, index) => {
      if (index === activeMatchIndex) {
        mark.classList.add("pmv-search-highlight--active");
        mark.scrollIntoView?.({ behavior: "smooth", block: "center" });
      } else {
        mark.classList.remove("pmv-search-highlight--active");
      }
    });
  }, [activeMatchIndex, matches, isSearchOpen]);

  const handleNextMatch = useCallback(() => {
    if (matches.length === 0) return;
    setActiveMatchIndex((prev) => (prev + 1) % matches.length);
  }, [matches.length]);

  const handlePreviousMatch = useCallback(() => {
    if (matches.length === 0) return;
    setActiveMatchIndex((prev) => (prev - 1 + matches.length) % matches.length);
  }, [matches.length]);

  const handleCloseSearch = useCallback(() => {
    setIsSearchOpen(false);
    setSearchQuery("");
  }, []);

  // Clean up search highlights on unmount
  useEffect(() => {
    return () => {
      if (contentRef.current) {
        clearSearchHighlights(contentRef.current);
      }
    };
  }, []);

  // Reports one changed question. `Answer` carries all three states without a sentinel: null
  // clears the question back to unanswered, an empty list is an explicit skip, and a non-empty
  // list is the answer itself. The document is never merged here — the host decides how and
  // whether to persist it.
  const handleAnswer = useCallback<AnswerCallback>(
    (questionId, answer) => {
      if (!eventHandler) return;

      const value =
        answer === undefined ? null : answer === null ? [] : Array.isArray(answer) ? answer : [answer];

      // camelCase on the wire: the server deserializes event args with a camelCase naming policy
      // and no case-insensitive fallback, so PascalCase keys would bind to nothing.
      eventHandler("OnAnswersChange", id, [{ questionId, answer: value }]);
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

  // Bring a question into view when the host asks. Keyed on the token as well as the id, so
  // asking for the same question twice scrolls twice.
  const scrollQuestionId = scrollTo?.questionId;
  const scrollToken = scrollTo?.token;
  useEffect(() => {
    if (!scrollQuestionId) return;

    const shell = shellRef.current;
    const container = contentRef.current;
    if (!shell || !container) return;

    // Compared rather than interpolated into a selector: an id is whatever the document said, and
    // this needs no escaping and no CSS.escape (which jsdom does not provide).
    const question = Array.from(container.querySelectorAll("[data-question-id]")).find(
      (el) => el.getAttribute("data-question-id") === scrollQuestionId,
    );
    if (!question) return;

    // Scroll the whole block, not just the question: a question is only answerable in the context
    // of the fence it sits in, and landing mid-block hides that.
    const block = question.closest(QUESTIONS_SELECTOR) ?? question;
    const margin = 16;
    const delta = block.getBoundingClientRect().top - shell.getBoundingClientRect().top - margin;

    // The widget owns its own scroll, so move that rather than calling scrollIntoView, which would
    // also drag every scrollable ancestor of the host page along with it.
    shell.scrollTo({ top: shell.scrollTop + delta, behavior: "smooth" });
  }, [scrollQuestionId, scrollToken]);

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

      setSelectionToolbar({ startOffset, endOffset, selectedText });
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
      const mark = (e.target as HTMLElement).closest(
        "mark[data-annotation-id]",
      ) as HTMLElement | null;
      if (!mark) return;

      const annotationId = mark.dataset.annotationId;
      const annotation = annotations.find((a) => a.id === annotationId);
      if (!annotation) return;

      setEditPopover(annotation);
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
        author: currentAuthor || undefined,
      };
      fireAnnotationsChange([...annotations, newAnnotation]);
      setAddPopover(null);
    },
    [addPopover, annotations, currentAuthor, fireAnnotationsChange],
  );

  const handleEditAnnotation = useCallback(
    (comment: string) => {
      if (!editPopover) return;
      const updated = annotations.map((a) => (a.id === editPopover.id ? { ...a, comment } : a));
      fireAnnotationsChange(updated);
      setEditPopover(null);
    },
    [editPopover, annotations, fireAnnotationsChange],
  );

  const handleToggleResolveAnnotation = useCallback(() => {
    if (!editPopover) return;
    const updated = annotations.map((a) =>
      a.id === editPopover.id ? { ...a, isResolved: !a.isResolved } : a
    );
    fireAnnotationsChange(updated);
    setEditPopover(null);
  }, [editPopover, annotations, fireAnnotationsChange]);

  const handleRemoveAnnotation = useCallback(() => {
    if (!editPopover) return;
    const filtered = annotations.filter((a) => a.id !== editPopover.id);
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
        !!href &&
        (href.startsWith("file:") || (!/^[a-z]+:\/\//i.test(href) && !href.startsWith("#")));
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

  // react-markdown wraps an overridden `code` element in a `pre`. BlockHandler already encapsulates
  // code blocks, diagram renderers, and questions callouts with their own appropriate containers.
  // Unwrapping the outer `pre` produces clean, valid block-level DOM elements directly under .pmv-markdown.
  const pre = useCallback((props: React.HTMLAttributes<HTMLPreElement>) => {
    const { children } = props;
    return <>{children}</>;
  }, []);

  // Math plugins are added only when the content actually contains math
  // delimiters, so plain plan markdown skips the extra KaTeX pass entirely.
  const plugins = useMemo(() => getMarkdownPlugins(content), [content]);

  // Each top-level `questions` fence gets its index stamped onto its info line, which is how the
  // renderer learns which block it is dispatching. The info line is never rendered, so annotation
  // offsets — computed over rendered text — are unaffected.
  const tagged = useMemo(() => tagQuestionBlocks(content), [content]);

  // Held by identity so React can skip the whole markdown subtree on renders that did not touch
  // it. Re-parsing and re-rendering a long plan to answer a scroll request — or to move a popover
  // — is work nobody asked for. Every dependency here is already stable (useCallback/useMemo), so
  // this only rebuilds when the document or the rendering of it actually changes.
  const markdownTree = useMemo(
    () => (
      <Markdown
        remarkPlugins={plugins.remarkPlugins}
        rehypePlugins={plugins.rehypePlugins}
        urlTransform={urlTransform}
        components={{
          a: anchor,
          code: BlockHandler,
          pre,
          blockquote: AlertBlockquote,
          img: ImageRenderer,
        }}
      >
        {tagged}
      </Markdown>
    ),
    [plugins, urlTransform, anchor, pre, tagged],
  );

  const fixed = slots?.StickyContent;
  const hasFixed = !!fixed && React.Children.count(fixed) > 0;

  const shellStyle: React.CSSProperties = {
    ...getWidth(width),
    ...getHeight(height),
  };

  return (
    <div className="pmv-root" style={shellStyle}>
      <div ref={shellRef} className="pmv-shell">
        <div className="pmv-body">
          <div ref={contentRef} className={article ? "pmv-markdown pmv-article" : "pmv-markdown"}>
            <QuestionsAnswerContext.Provider value={answerCallback}>{markdownTree}</QuestionsAnswerContext.Provider>
          </div>
        </div>
        {(hasFixed || isSearchOpen) && (
          <div className="pmv-sticky">
            {isSearchOpen && (
              <SearchOverlay
                query={searchQuery}
                onQueryChange={setSearchQuery}
                matchCount={matches.length}
                currentIndex={activeMatchIndex}
                onNext={handleNextMatch}
                onPrevious={handlePreviousMatch}
                onClose={handleCloseSearch}
              />
            )}
            {fixed}
          </div>
        )}

        {annotationsEnabled && selectionToolbar && selectionAnchor && (
          <SelectionToolbar
            position={selectionAnchor.position}
            visible={selectionAnchor.visible}
            onAddComment={handleAddComment}
          />
        )}
        {annotationsEnabled && addPopover && addAnchor && (
          <AddAnnotationPopover
            position={addAnchor.position}
            visible={addAnchor.visible}
            selectedText={addPopover.selectedText}
            onAdd={handleAddAnnotation}
            onCancel={() => setAddPopover(null)}
          />
        )}
        {annotationsEnabled && editPopover && editAnchor && (
          <EditAnnotationPopover
            position={editAnchor.position}
            visible={editAnchor.visible}
            annotation={editPopover}
            currentAuthor={currentAuthor}
            onSave={handleEditAnnotation}
            onToggleResolve={handleToggleResolveAnnotation}
            onRemove={handleRemoveAnnotation}
            onCancel={() => setEditPopover(null)}
          />
        )}
      </div>
    </div>
  );
};
