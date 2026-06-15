import React, { lazy, Suspense, useCallback, useState } from "react";
import { Prism as SyntaxHighlighter } from "react-syntax-highlighter";
import { prismTheme } from "./prismTheme";

const MermaidRenderer = lazy(() => import("./DraftMarkdown/MermaidRenderer").then((m) => ({ default: m.MermaidRenderer })));
const GraphvizRenderer = lazy(() => import("./DraftMarkdown/GraphvizRenderer").then((m) => ({ default: m.GraphvizRenderer })));

const CopyIcon = () => (
  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
    <rect width="14" height="14" x="8" y="8" rx="2" ry="2" />
    <path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2" />
  </svg>
);

const CheckIcon = () => (
  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
    <polyline points="20 6 9 17 4 12" />
  </svg>
);

const codeBlockPreStyle: React.CSSProperties = {
  margin: 0,
  borderRadius: 0,
  background: "transparent",
  padding: "1rem",
  paddingRight: "3rem",
  overflowX: "auto",
  wordBreak: "normal",
  overflowWrap: "break-word",
};

export const CodeBlock: React.FC<React.HTMLAttributes<HTMLElement>> = ({ className, children, style: _style, ...rest }) => {
  const match = /language-(\w+)/.exec(String(className || ""));
  const content = String(children).replace(/\n$/, "");
  const [copied, setCopied] = useState(false);

  const handleCopy = useCallback(() => {
    navigator.clipboard.writeText(content).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    });
  }, [content]);

  if (match) {
    const lang = match[1];

    if (lang === "mermaid") {
      return (
        <Suspense fallback={<div className="pmv-diagram-loading"><span>Loading diagram...</span></div>}>
          <MermaidRenderer content={content} />
        </Suspense>
      );
    }

    if (lang === "graphviz" || lang === "dot") {
      return (
        <Suspense fallback={<div className="pmv-diagram-loading"><span>Loading diagram...</span></div>}>
          <GraphvizRenderer content={content} />
        </Suspense>
      );
    }

    return (
      <div className="pmv-code-block">
        <button
          className={`pmv-code-copy${copied ? " pmv-code-copy--copied" : ""}`}
          onClick={handleCopy}
          aria-label="Copy to clipboard"
        >
          {copied ? <CheckIcon /> : <CopyIcon />}
        </button>
        <SyntaxHighlighter
          style={prismTheme as unknown as { [key: string]: React.CSSProperties }}
          language={lang}
          PreTag="pre"
          customStyle={codeBlockPreStyle}
          wrapLongLines={false}
        >
          {content}
        </SyntaxHighlighter>
      </div>
    );
  }
  return (
    <code className={className} {...rest}>
      {children}
    </code>
  );
};
