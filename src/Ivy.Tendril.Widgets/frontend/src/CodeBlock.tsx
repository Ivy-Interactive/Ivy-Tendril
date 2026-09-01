import React, { useCallback, useState } from "react";
import { Prism as SyntaxHighlighter } from "react-syntax-highlighter";
import { prismTheme } from "./prismTheme";

/**
 * A highlighted code block with a copy button — the plain-fence rendering, with no dispatch on the
 * language.
 *
 * It lives in its own module rather than inside `BlockHandler` because `QuestionsCallout` renders
 * code blocks too, and importing `BlockHandler` from there would close a cycle (`BlockHandler`
 * already imports `QuestionsCallout`). Same reasoning as `questionsContext.ts`.
 */

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

export const codeBlockPreStyle: React.CSSProperties = {
  margin: 0,
  minWidth: 0,
  maxWidth: "100%",
  borderRadius: 0,
  background: "transparent",
  padding: "1rem",
  paddingRight: "3rem",
  overflowX: "auto",
  wordBreak: "normal",
  overflowWrap: "break-word",
};

/** Prism's own name for the markup family, which several fence languages map onto. */
export const normalizeLanguage = (lang: string): string =>
  lang === "xml" || lang === "html" || lang === "svg" ? "markup" : lang;

const CopyButton: React.FC<{ content: string }> = ({ content }) => {
  const [copied, setCopied] = useState(false);

  const handleCopy = useCallback(() => {
    navigator.clipboard.writeText(content).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    });
  }, [content]);

  return (
    <button
      className={`pmv-code-copy${copied ? " pmv-code-copy--copied" : ""}`}
      onClick={handleCopy}
      aria-label="Copy to clipboard"
    >
      {copied ? <CheckIcon /> : <CopyIcon />}
    </button>
  );
};

interface CodeBlockProps {
  content: string;
  /** Fence language, or undefined for a fence that named none. */
  language?: string;
}

export const CodeBlock: React.FC<CodeBlockProps> = ({ content, language }) => (
  <div className="pmv-code-block">
    <CopyButton content={content} />
    {language ? (
      <SyntaxHighlighter
        style={prismTheme as unknown as { [key: string]: React.CSSProperties }}
        language={normalizeLanguage(language)}
        PreTag="pre"
        customStyle={codeBlockPreStyle}
        wrapLongLines={false}
      >
        {content}
      </SyntaxHighlighter>
    ) : (
      <pre style={codeBlockPreStyle}>
        <code>{content}</code>
      </pre>
    )}
  </div>
);
