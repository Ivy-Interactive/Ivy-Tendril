import React from "react";
import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { CodeBlock } from "../CodeBlock";

/**
 * Question and option descriptions are full block markdown. Code fences are routed to
 * `CodeBlock` directly and never to `BlockHandler`: a `questions` fence written inside a
 * description is an example, not another picker, and must stay a code block.
 */
const descriptionComponents = {
  code: ({ className, children }: React.HTMLAttributes<HTMLElement>) => {
    const language = /language-(\w+)/.exec(String(className || ""))?.[1];
    const text = String(children);

    if (!language && !text.includes("\n")) return <code>{children}</code>;

    return <CodeBlock content={text.replace(/\n$/, "")} language={language} />;
  },
  pre: ({ children }: React.HTMLAttributes<HTMLPreElement>) => <>{children}</>,
};

const remarkPlugins = [remarkGfm];

export const DescriptionMarkdown: React.FC<{ text: string }> = ({ text }) => (
  <Markdown remarkPlugins={remarkPlugins} components={descriptionComponents}>
    {text}
  </Markdown>
);
