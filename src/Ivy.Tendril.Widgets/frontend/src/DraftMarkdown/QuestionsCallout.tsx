import React, { useMemo, useState } from "react";
import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { CodeBlock } from "../CodeBlock";
import { answerEntries, isSkipped, otherEntry, parseQuestions } from "./questionsSchema";
import type { PlanQuestion, QuestionOption } from "./questionsSchema";
import type { AnswerCallback } from "./questionsContext";

/**
 * Question and option descriptions are full block markdown — paragraphs, lists, tables and fenced
 * code blocks all render. An option often needs to show the shape of the thing it is proposing, and
 * a snippet says that better than a sentence about it.
 *
 * Code fences are routed to `CodeBlock` directly and never to `BlockHandler`: a `questions` fence
 * written inside a description is an example, not another picker, and must stay a code block.
 */
const descriptionComponents = {
  code: ({ className, children }: React.HTMLAttributes<HTMLElement>) => {
    const language = /language-(\w+)/.exec(String(className || ""))?.[1];
    const text = String(children);

    // react-markdown hands both spans and fences to `code`; only a fence is block content.
    if (!language && !text.includes("\n")) return <code>{children}</code>;

    return <CodeBlock content={text.replace(/\n$/, "")} language={language} />;
  },
  // DraftMarkdown overrides `pre` globally to render bare, and CodeBlock brings its own.
  pre: ({ children }: React.HTMLAttributes<HTMLPreElement>) => <>{children}</>,
};

const remarkPlugins = [remarkGfm];

const DescriptionMarkdown: React.FC<{ text: string }> = ({ text }) => (
  <Markdown remarkPlugins={remarkPlugins} components={descriptionComponents}>
    {text}
  </Markdown>
);

/**
 * The tinted frame every questions block sits in. It carries no heading of its own — the question
 * text is the heading — so the block leads with what is actually being asked.
 */
const Shell: React.FC<{ children: React.ReactNode }> = ({ children }) => (
  <div className="pmv-questions" role="note">
    {children}
  </div>
);

/** The pre-schema rendering: the fence body as plain text. Unchanged since plan 00073. */
const StaticCallout: React.FC<{ content: string }> = ({ content }) => (
  <Shell>
    <div className="pmv-questions-content">{content}</div>
  </Shell>
);

interface QuestionViewProps {
  question: PlanQuestion;
  blockIndex: number;
  onAnswer: AnswerCallback;
}

const QuestionView: React.FC<QuestionViewProps> = ({ question, blockIndex, onAnswer }) => {
  const entries = answerEntries(question);
  const options = question.options ?? [];
  const hasOptions = options.length > 0;
  // Not memoized: `options` is a fresh array whenever `question.options` is absent, so a memo keyed
  // on it would never hit anyway, and a set of 2-4 slugs is cheaper to rebuild than to track.
  const optionValues = new Set(options.map((o) => o.value));
  const typed = otherEntry(question);

  // The typed text is a draft, not answer state: the host is told only what changed and may never
  // echo an updated document back, so the input has to hold what the user is typing. Selection
  // state proper is still derived from `question` on every render.
  const [draft, setDraft] = useState(typed ?? "");
  const [seenTyped, setSeenTyped] = useState(typed);
  const [otherOpen, setOtherOpen] = useState(typed !== undefined);
  if (typed !== seenTyped) {
    // The document changed underneath us — resync the draft to it.
    setSeenTyped(typed);
    setDraft(typed ?? "");
    setOtherOpen(typed !== undefined);
  }

  const groupName = `pmv-q-${blockIndex}-${question.id}`;
  const otherActive = typed !== undefined;

  const selectOption = (option: QuestionOption) => {
    setOtherOpen(false);
    if (!question.multiple) {
      onAnswer(question.id, option.value);
      return;
    }

    const next = entries.includes(option.value)
      ? entries.filter((entry) => entry !== option.value)
      : [...entries, option.value];
    onAnswer(question.id, next);
  };

  const writeOther = (value: string) => {
    setDraft(value);
    if (!question.multiple) {
      onAnswer(question.id, value);
      return;
    }

    const kept = entries.filter((entry) => optionValues.has(entry));
    onAnswer(question.id, value ? [...kept, value] : kept);
  };

  const toggleOther = () => {
    if (question.multiple && otherActive) {
      setOtherOpen(false);
      onAnswer(
        question.id,
        entries.filter((entry) => optionValues.has(entry)),
      );
      return;
    }

    setOtherOpen(true);
    if (draft) writeOther(draft);
  };

  // Clearing is a block-level action — see `QuestionsCallout`. Nothing to undo here: the answer
  // leaves the document, the new content arrives, and the resync above returns `draft` to empty.

  const freeTextInput = (
    <input
      type="text"
      className="pmv-question-other-input"
      value={draft}
      placeholder="Type your answer"
      aria-label={`Other answer for ${question.title || question.id}`}
      onChange={(e) => writeOther(e.target.value)}
    />
  );

  return (
    <div className="pmv-question">
      {/* `header` was the tab's chip label. With the tabs gone it becomes an eyebrow, which is
          what keeps a stack of questions scannable. */}
      {question.header && <div className="pmv-question-header">{question.header}</div>}
      <div className="pmv-question-title">{question.title}</div>
      {question.description && (
        <div className="pmv-question-description">
          <DescriptionMarkdown text={question.description} />
        </div>
      )}

      {/* `answer: null` selects nothing, so without this a skipped question is indistinguishable
          from one that was never touched. */}
      {isSkipped(question) && <div className="pmv-question-skipped">Skipped — you decide</div>}

      <div className="pmv-question-options">
        {options.map((option) => {
          const selected = entries.includes(option.value);
          return (
            // A div rather than a label, with the label around the radio and title only: a
            // description may hold a code block, whose copy button would otherwise toggle the
            // option on its way through the label.
            <div
              key={option.value}
              className={`pmv-question-option${selected ? " pmv-question-option--selected" : ""}`}
            >
              <label className="pmv-question-option-main">
                <input
                  type={question.multiple ? "checkbox" : "radio"}
                  name={groupName}
                  className="pmv-question-check"
                  checked={selected}
                  onChange={() => selectOption(option)}
                />
                <span className="pmv-question-option-title">
                  {option.title}
                  {option.recommended && (
                    <span className="pmv-question-option-recommended">Recommended</span>
                  )}
                </span>
              </label>
              {option.description && (
                <div className="pmv-question-option-description">
                  <DescriptionMarkdown text={option.description} />
                </div>
              )}
            </div>
          );
        })}

        {hasOptions && question.other && (
          <div
            className={`pmv-question-option pmv-question-option--other${otherActive ? " pmv-question-option--selected" : ""}`}
          >
            <label className="pmv-question-other-label">
              <input
                type={question.multiple ? "checkbox" : "radio"}
                name={groupName}
                className="pmv-question-check"
                checked={otherActive}
                onChange={toggleOther}
              />
              <span className="pmv-question-option-title">Other</span>
            </label>
            {otherOpen && freeTextInput}
          </div>
        )}

        {!hasOptions && freeTextInput}
      </div>
    </div>
  );
};

export interface QuestionsCalloutProps {
  content: string;
  /** Which `questions` fence this is, 0-based. Keeps radio groups distinct across blocks. */
  blockIndex?: number;
  /** Absent when the host did not subscribe to `OnAnswersChange`, which means read-only. */
  onAnswer?: AnswerCallback;
}

export const QuestionsCallout: React.FC<QuestionsCalloutProps> = ({ content, blockIndex = 0, onAnswer }) => {
  const parsed = useMemo(() => parseQuestions(content), [content]);

  if (parsed.kind === "invalid" || parsed.questions.length === 0 || !onAnswer) {
    return <StaticCallout content={content} />;
  }

  const questions = parsed.questions;

  // One Clear for the whole block rather than one per question: the block is what the user is
  // working through, and a row of identical buttons down a stack reads as clutter. It resets
  // every answered question in the block, and stays hidden until there is one.
  //
  // `answerPresent` covers a skip too — `answer: null` is a decision worth being able to retract.
  const answered = questions.filter((question) => question.answerPresent);

  const clearAll = () => {
    for (const question of answered) {
      onAnswer(question.id, undefined);
    }
  };

  // Every question in the block is on screen at once, stacked. A block holds at most four, and
  // they are usually related — reading them together is how you notice that answering one settles
  // the next. Tabs hid that, and hid how much was still open.
  return (
    <Shell>
      {answered.length > 0 && (
        <div className="pmv-questions-actions">
          <button
            type="button"
            className="pmv-question-clear"
            onClick={clearAll}
            aria-label={questions.length > 1 ? "Clear all answers in this block" : "Clear answer"}
          >
            Clear
          </button>
        </div>
      )}

      {questions.map((question) => (
        <QuestionView
          key={question.id}
          question={question}
          blockIndex={blockIndex}
          onAnswer={onAnswer}
        />
      ))}
    </Shell>
  );
};
