import React, { useMemo } from "react";
import Markdown from "react-markdown";
import { getMarkdownPlugins } from "./math";
import { BlockHandler } from "./BlockHandler";
import { AlertBlockquote } from "./PlanMarkdown/AlertBlockquote";

/**
 * Unwraps the `pre` react-markdown wraps a fenced code block in. `BlockHandler` (routed through
 * `code` below) already supplies its own container, so the outer `pre` is redundant.
 *
 * Its identity must stay stable across renders: `QuestionsCallout` renders inside this `pre`, and a
 * new function identity here is a new React element type, which unmounts and remounts everything
 * inside it — including in-progress question answers.
 */
const UnwrapPre: React.FC<React.HTMLAttributes<HTMLPreElement>> = ({ children }) => <>{children}</>;

const blockMarkdownComponents = { code: BlockHandler, blockquote: AlertBlockquote, pre: UnwrapPre };

/**
 * The shared markdown renderer for chat and agent output. `components` is a module-level constant
 * so react-markdown never sees a new `pre` identity, and the whole render is memoized on `content`
 * so a version bump that doesn't change the text (a streaming chunk for another message, a job
 * status update) skips the remark/rehype pipeline entirely.
 */
export const BlockMarkdown: React.FC<{ content: string }> = React.memo(({ content }) => {
  const plugins = useMemo(() => getMarkdownPlugins(content), [content]);

  return (
    <Markdown {...plugins} components={blockMarkdownComponents}>
      {content}
    </Markdown>
  );
});
