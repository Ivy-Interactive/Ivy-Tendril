import React, { useCallback } from "react";
import Markdown from "react-markdown";
import rehypeHighlight from "rehype-highlight";
import remarkGfm from "remark-gfm";
import "./draft-markdown.css";
import { getHeight, getWidth } from "../styles";

type IvyEventHandler = (eventName: string, widgetId: string, args: unknown[]) => void;

interface DraftMarkdownProps {
  id: string;
  width?: string;
  height?: string;
  content?: string;
  article?: boolean;
  dangerouslyAllowLocalFiles?: boolean;
  events?: string[];
  onIvyEvent?: IvyEventHandler;
  /** Slot content injected by the backend. `FixedContent` is pinned and does not scroll. */
  slots?: {
    FixedContent?: React.ReactNode[];
  };
}

const EMPTY_EVENTS: string[] = [];

export const DraftMarkdown: React.FC<DraftMarkdownProps> = ({
  id,
  width,
  height,
  content = "",
  article = false,
  dangerouslyAllowLocalFiles = false,
  events = EMPTY_EVENTS,
  onIvyEvent,
  slots,
}) => {
  const handleLinkClick = useCallback(
    (href: string) => {
      if (events.includes("OnLinkClick") && onIvyEvent) {
        onIvyEvent("OnLinkClick", id, [href]);
      }
    },
    [events, onIvyEvent, id],
  );

  // Intercept link clicks so the backend OnLinkClick handler runs (file sheets,
  // cross-plan navigation, etc.) instead of a hard browser navigation.
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

  const fixed = slots?.FixedContent;
  const hasFixed = !!fixed && React.Children.count(fixed) > 0;

  const shellStyle: React.CSSProperties = {
    ...getWidth(width),
    ...getHeight(height),
  };

  return (
    <div className="pmv-shell" style={shellStyle}>
      {/* Scrollable markdown body — owns the vertical scroll. */}
      <div className="pmv-body">
        <div className={article ? "pmv-markdown pmv-article" : "pmv-markdown"}>
          <Markdown
            remarkPlugins={[remarkGfm]}
            rehypePlugins={[rehypeHighlight]}
            components={{ a: anchor }}
          >
            {content}
          </Markdown>
        </div>
      </div>

      {/* Fixed (pinned) slot — sibling of the scroll body, so it stays in place. */}
      {hasFixed && <div className="pmv-fixed">{fixed}</div>}
    </div>
  );
};
