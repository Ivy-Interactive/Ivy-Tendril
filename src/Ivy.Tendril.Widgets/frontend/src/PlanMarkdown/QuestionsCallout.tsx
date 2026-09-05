import React, { useMemo, useState } from "react";
import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { CodeBlock } from "../CodeBlock";
import { answerEntries, otherEntry, parseQuestions } from "./questionsSchema";
import type { PlanQuestion, QuestionOption } from "./questionsSchema";
import type { AnswerCallback, QuestionSubmitCallback } from "./questionsContext";

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

  /**
   * Reports one change, mapping an emptied selection to "unanswered".
   *
   * An empty list would otherwise travel as "the answer is nothing in particular". Unchecking your
   * last box means the question is simply unanswered again, so that is what gets reported.
   */
  const report = (answer: string | string[] | null | undefined) => {
    // Emptying the field is not an answer of "": clearing the box means the question is unanswered
    // again. Written literally it would stay struck through in the index and counted as answered,
    // with nothing in it.
    const empty =
      answer === "" || (Array.isArray(answer) && answer.length === 0);

    onAnswer(question.id, empty ? undefined : answer);
  };

  const selectOption = (option: QuestionOption) => {
    if (!question.multiple) {
      // Single-select: the option replaces whatever was typed, so the field goes with it.
      setOtherOpen(false);
      report(option.value);
      return;
    }

    // Multi-select keeps the typed entry in the answer, so the field it was typed into has to stay
    // open. Closing it stranded the text — visible in the document, invisible and uneditable here.
    const next = entries.includes(option.value)
      ? entries.filter((entry) => entry !== option.value)
      : [...entries, option.value];
    report(next);
  };

  const writeOther = (value: string) => {
    setDraft(value);
    if (!question.multiple) {
      report(value);
      return;
    }

    const kept = entries.filter((entry) => optionValues.has(entry));
    report(value ? [...kept, value] : kept);
  };

  const toggleOther = () => {
    if (question.multiple && otherActive) {
      setOtherOpen(false);
      report(entries.filter((entry) => optionValues.has(entry)));
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
    // The anchor a host's ScrollTo addresses. Ids are unique across a revision by schema, so it
    // needs no block qualifier.
    <div className="pmv-question" data-question-id={question.id}>
      {/* `header` was the tab's chip label. With the tabs gone it becomes an eyebrow, which is
          what keeps a stack of questions scannable. */}
      {question.header && <div className="pmv-question-header">{question.header}</div>}
      <div className="pmv-question-title">
        {question.title}
        {/* Says the plan does not wait on this one. Without it the block gives no hint of what the
            index card already knows, and the two would disagree on screen. */}
        {question.optional && <span className="pmv-question-optional">Optional</span>}
      </div>
      {question.description && (
        <div className="pmv-question-description">
          <DescriptionMarkdown text={question.description} />
        </div>
      )}

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

/**
 * The read-only rendering: what was asked, and what was decided.
 *
 * A plan under review is no longer a form — the questions have been settled and the reader wants
 * the decisions, not the controls. Dumping the fence body as YAML (which is what a structured
 * block used to do without a subscriber) shows the data but buries the answer, so this renders the
 * question and its answer as prose instead.
 */
const AnsweredQuestion: React.FC<{ question: PlanQuestion }> = ({ question }) => {
  const entries = answerEntries(question);
  const options = question.options ?? [];

  // An entry naming an option shows that option's title; anything else is the user's own words.
  const answers = entries.map(
    (entry) => options.find((option) => option.value === entry)?.title ?? entry,
  );

  return (
    <div className="pmv-question" data-question-id={question.id}>
      {question.header && <div className="pmv-question-header">{question.header}</div>}
      <div className="pmv-question-title">
        {question.title}
        {question.optional && <span className="pmv-question-optional">Optional</span>}
      </div>
      {question.description && (
        <div className="pmv-question-description">
          <DescriptionMarkdown text={question.description} />
        </div>
      )}

      {answers.length > 0 ? (
        <div className="pmv-question-answer">
          {answers.map((answer) => (
            <span key={answer} className="pmv-question-answer-value">
              {answer}
            </span>
          ))}
        </div>
      ) : (
        // Said out loud, because an unanswered question in a settled plan is itself information:
        // it means the agent chose, using the recommended option where there was one.
        <div className="pmv-question-answer pmv-question-answer--none">
          {question.optional ? "Not answered — Not required" : "Not answered — Agent decided"}
        </div>
      )}
    </div>
  );
};

interface ChatQuestionsBlockProps {
  questions: PlanQuestion[];
  blockIndex: number;
  onSubmit: QuestionSubmitCallback;
}

const ChatQuestionsBlock: React.FC<ChatQuestionsBlockProps> = ({ questions, blockIndex, onSubmit }) => {
  const [localAnswers, setLocalAnswers] = useState<Record<string, string[]>>(() => {
    const initial: Record<string, string[]> = {};
    for (const q of questions) {
      if (q.answerPresent) {
        initial[q.id] = answerEntries(q);
      }
    }
    return initial;
  });

  const handleLocalAnswer = (questionId: string, answer: string | string[] | null | undefined) => {
    setLocalAnswers((prev) => {
      const next = { ...prev };
      if (answer === undefined || answer === null || answer === "" || (Array.isArray(answer) && answer.length === 0)) {
        delete next[questionId];
      } else if (Array.isArray(answer)) {
        next[questionId] = answer;
      } else {
        next[questionId] = [answer];
      }
      return next;
    });
  };

  const hasAnyAnswers = Object.keys(localAnswers).length > 0;
  const clearAll = () => {
    setLocalAnswers({});
  };

  const canSubmit = questions.every((q) => {
    if (q.optional) return true;
    const ans = localAnswers[q.id];
    return (ans && ans.length > 0) || q.answerPresent;
  });

  const handleSubmit = () => {
    if (!canSubmit) return;

    const summaryLines: string[] = ["Answers:"];
    for (const q of questions) {
      const ans = localAnswers[q.id] ?? (q.answerPresent ? answerEntries(q) : []);
      if (ans.length > 0) {
        const displayVals = ans.map((val) => {
          const opt = q.options?.find((o) => o.value === val);
          return opt ? opt.title : val;
        });
        summaryLines.push(`- **${q.title || q.id}**: ${displayVals.join(", ")}`);
      } else if (q.optional) {
        summaryLines.push(`- **${q.title || q.id}**: *(skipped)*`);
      }
    }

    onSubmit(localAnswers, summaryLines.join("\n"));
  };

  return (
    <Shell>
      {hasAnyAnswers && (
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

      {questions.map((question) => {
        const effectiveQuestion: PlanQuestion = {
          ...question,
          answer: localAnswers[question.id] ?? question.answer,
          answerPresent: (localAnswers[question.id] && localAnswers[question.id].length > 0) || question.answerPresent,
        };

        return (
          <QuestionView
            key={question.id}
            question={effectiveQuestion}
            blockIndex={blockIndex}
            onAnswer={handleLocalAnswer}
          />
        );
      })}

      <div className="pmv-questions-footer">
        <button
          type="button"
          className="pmv-questions-submit"
          disabled={!canSubmit}
          onClick={handleSubmit}
        >
          Submit Response
        </button>
      </div>
    </Shell>
  );
};

export interface QuestionsCalloutProps {
  content: string;
  /** Which `questions` fence this is, 0-based. Keeps radio groups distinct across blocks. */
  blockIndex?: number;
  /** Absent when the host did not subscribe to `OnAnswersChange`, which means read-only. */
  onAnswer?: AnswerCallback;
  /** Present when rendered inside chat to enable interactive answers and submission. */
  onSubmit?: QuestionSubmitCallback;
}

export const QuestionsCallout: React.FC<QuestionsCalloutProps> = ({ content, blockIndex = 0, onAnswer, onSubmit }) => {
  const parsed = useMemo(() => parseQuestions(content), [content]);

  // A block that does not parse is the pre-schema plain-text form, and there is nothing to render
  // but the text itself.
  if (parsed.kind === "invalid" || parsed.questions.length === 0) {
    return <StaticCallout content={content} />;
  }

  const questions = parsed.questions;

  // If rendered in chat with submit handler:
  if (!onAnswer && onSubmit) {
    if (questions.every((q) => q.answerPresent)) {
      return (
        <Shell>
          {questions.map((question) => (
            <AnsweredQuestion key={question.id} question={question} />
          ))}
        </Shell>
      );
    }

    return (
      <ChatQuestionsBlock
        questions={questions}
        blockIndex={blockIndex}
        onSubmit={onSubmit}
      />
    );
  }

  // No subscriber means the host is showing a plan rather than working through it — the Review
  // stage, or any other read-only view. Present the decisions.
  if (!onAnswer) {
    return (
      <Shell>
        {questions.map((question) => (
          <AnsweredQuestion key={question.id} question={question} />
        ))}
      </Shell>
    );
  }

  // One Clear for the whole block rather than one per question: the block is what the user is
  // working through, and a row of identical buttons down a stack reads as clutter. It resets
  // every answered question in the block, and stays hidden until there is one.
  //
  // Optional questions are included: being optional does not make an answer unretractable.
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
