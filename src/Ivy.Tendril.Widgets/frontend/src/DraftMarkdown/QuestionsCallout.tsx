import React, { useMemo, useState } from "react";
import Markdown from "react-markdown";
import { answerEntries, isSkipped, otherEntry, parseQuestions, questionLabel } from "./questionsSchema";
import type { PlanQuestion, QuestionOption } from "./questionsSchema";
import type { AnswerCallback } from "./questionsContext";

const HelpCircleIcon = () => (
  <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
    <circle cx="12" cy="12" r="10" />
    <path d="M9.09 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3" />
    <path d="M12 17h.01" />
  </svg>
);

const CheckIcon = () => (
  <svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round">
    <polyline points="20 6 9 17 4 12" />
  </svg>
);

/**
 * Question and option descriptions are markdown, but only inline markdown: a paragraph collapses to
 * its children and a code span stays a plain `code` element. Deliberately *not* routed through
 * `BlockHandler` — a `questions` fence nested in a description is text, not another picker.
 */
const inlineComponents = {
  p: ({ children }: React.HTMLAttributes<HTMLParagraphElement>) => <>{children}</>,
  code: ({ children }: React.HTMLAttributes<HTMLElement>) => <code>{children}</code>,
};

const InlineMarkdown: React.FC<{ text: string }> = ({ text }) => (
  <Markdown components={inlineComponents}>{text}</Markdown>
);

const Shell: React.FC<{ children: React.ReactNode }> = ({ children }) => (
  <div className="pmv-questions" role="note">
    <div className="pmv-questions-header">
      <HelpCircleIcon />
      <span className="pmv-questions-title">Questions</span>
    </div>
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

  const clear = () => {
    setDraft("");
    setOtherOpen(false);
    onAnswer(question.id, undefined);
  };

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
      <div className="pmv-question-title">{question.title}</div>
      {question.description && (
        <div className="pmv-question-description">
          <InlineMarkdown text={question.description} />
        </div>
      )}

      {/* `answer: null` selects nothing, so without this a skipped question is indistinguishable
          from one that was never touched. */}
      {isSkipped(question) && <div className="pmv-question-skipped">Skipped — you decide</div>}

      <div className="pmv-question-options">
        {options.map((option) => {
          const selected = entries.includes(option.value);
          return (
            <label
              key={option.value}
              className={`pmv-question-option${selected ? " pmv-question-option--selected" : ""}`}
            >
              <input
                type={question.multiple ? "checkbox" : "radio"}
                name={groupName}
                className="pmv-question-check"
                checked={selected}
                onChange={() => selectOption(option)}
              />
              <span className="pmv-question-option-body">
                <span className="pmv-question-option-title">
                  {option.title}
                  {option.recommended && (
                    <span className="pmv-question-option-recommended">Recommended</span>
                  )}
                </span>
                {option.description && (
                  <span className="pmv-question-option-description">
                    <InlineMarkdown text={option.description} />
                  </span>
                )}
              </span>
            </label>
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

      <div className="pmv-question-actions">
        <button type="button" className="pmv-question-clear" onClick={clear}>
          Clear
        </button>
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
  const [tab, setTab] = useState(0);

  if (parsed.kind === "invalid" || parsed.questions.length === 0 || !onAnswer) {
    return <StaticCallout content={content} />;
  }

  const questions = parsed.questions;
  const active = Math.min(tab, questions.length - 1);

  return (
    <Shell>
      {questions.length > 1 && (
        <div className="pmv-questions-tabs" role="tablist">
          {questions.map((question, index) => (
            <button
              key={question.id}
              type="button"
              role="tab"
              aria-selected={index === active}
              className={`pmv-questions-tab${index === active ? " pmv-questions-tab--active" : ""}`}
              onClick={() => setTab(index)}
            >
              {questionLabel(question, index)}
              {question.answerPresent && (
                <span className="pmv-questions-tab-badge" aria-label="answered">
                  <CheckIcon />
                </span>
              )}
            </button>
          ))}
        </div>
      )}

      <QuestionView
        key={questions[active].id}
        question={questions[active]}
        blockIndex={blockIndex}
        onAnswer={onAnswer}
      />
    </Shell>
  );
};
