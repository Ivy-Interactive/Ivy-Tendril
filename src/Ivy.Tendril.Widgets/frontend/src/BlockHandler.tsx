import React, { lazy, Suspense, useContext } from "react";
import { CodeBlock } from "./CodeBlock";
import { QuestionsCallout } from "./PlanMarkdown/QuestionsCallout";
import { QuestionsAnswerContext } from "./PlanMarkdown/questionsContext";

/** `questions`, or `questions_<n>` once `tagQuestionBlocks` has stamped the block's index on it. */
const QUESTIONS_LANG = /^questions(?:_(\d+))?$/;

const MermaidRenderer = lazy(() => import("./PlanMarkdown/MermaidRenderer").then((m) => ({ default: m.MermaidRenderer })));
const GraphvizRenderer = lazy(() => import("./PlanMarkdown/GraphvizRenderer").then((m) => ({ default: m.GraphvizRenderer })));

export const BlockHandler: React.FC<React.HTMLAttributes<HTMLElement>> = ({ className, children, style: _style, ...rest }) => {
  const match = /language-(\w+)/.exec(String(className || ""));
  const content = String(children).replace(/\n$/, "");
  const onAnswer = useContext(QuestionsAnswerContext);

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

    const questions = QUESTIONS_LANG.exec(lang);
    if (questions) {
      return (
        <QuestionsCallout
          content={content}
          blockIndex={questions[1] ? Number(questions[1]) : 0}
          onAnswer={onAnswer}
        />
      );
    }

    return <CodeBlock content={content} language={lang} />;
  }

  // No language match - check if block-level (multi-line) or inline
  const isBlock = String(children).includes("\n");
  if (isBlock) {
    return <CodeBlock content={content} />;
  }

  return (
    <code className={className} {...rest}>
      {children}
    </code>
  );
};
