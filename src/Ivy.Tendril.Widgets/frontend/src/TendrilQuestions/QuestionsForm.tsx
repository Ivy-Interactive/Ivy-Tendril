import React, { useId, useState } from "react";
import { Check } from "lucide-react";
import type { PlanQuestion, QuestionOption } from "../PlanMarkdown/questionsSchema";
import { DescriptionMarkdown } from "./DescriptionMarkdown";
import { entryTitle, hasEntries } from "./answers";
import type { AnswerMap } from "./answers";
import "./tendril-questions.css";

export interface QuestionsSubmitAction {
  label?: string;
  disabled?: boolean;
  onSubmit: () => void;
}

export interface QuestionsFormProps {
  questions: PlanQuestion[];
  /** Current answers per question id. Entries naming an option select it; any other entry is free text. */
  answers: AnswerMap;
  /** Whether each question's Other field is open, for a typed answer that is still empty. */
  otherOpen?: Record<string, boolean>;
  /** Presents the decisions instead of the controls. */
  readOnly?: boolean;
  /** Called with the full entry list for the question; an empty list means unanswered again. */
  onAnswer?: (questionId: string, entries: string[]) => void;
  onOtherOpenChange?: (questionId: string, open: boolean) => void;
  /** Shown as a Clear button once anything is answered. */
  onClear?: () => void;
  /** Adds a submit button to the footer; absent when every change is reported live instead. */
  submit?: QuestionsSubmitAction;
}

/** A click on a control, link or selected text inside a card must not toggle the card. */
const ignoresCardClick = (e: React.MouseEvent): boolean =>
  Boolean((e.target as HTMLElement).closest("label, input, textarea, button, a")) ||
  Boolean(window.getSelection()?.toString());

interface OptionCardProps {
  option: QuestionOption;
  selected: boolean;
  multiple: boolean;
  groupName: string;
  onSelect: () => void;
}

const OptionCard: React.FC<OptionCardProps> = ({ option, selected, multiple, groupName, onSelect }) => (
  <div
    className="tq-option"
    data-selected={selected}
    onClick={(e) => {
      if (ignoresCardClick(e)) return;
      onSelect();
    }}
  >
    <label className="tq-option-main">
      <input
        type={multiple ? "checkbox" : "radio"}
        name={groupName}
        className="tq-option-input"
        checked={selected}
        onChange={onSelect}
      />
      {multiple && (
        <span className="tq-option-box" aria-hidden="true">
          <Check size={12} strokeWidth={3} />
        </span>
      )}
      <span className="tq-option-title">
        {option.title}
        {option.recommended && <span className="tq-option-recommended">Recommended</span>}
      </span>
      {!multiple && selected && (
        <span className="tq-option-check" aria-hidden="true">
          <Check size={16} />
        </span>
      )}
    </label>
    {option.description && (
      <div className="tq-option-description">
        <DescriptionMarkdown text={option.description} />
      </div>
    )}
  </div>
);

const QuestionHeading: React.FC<{ question: PlanQuestion }> = ({ question }) => (
  <>
    {question.header && <div className="tq-question-header">{question.header}</div>}
    <div className="tq-question-title">
      {question.title}
      {question.optional && <span className="tq-question-optional">Optional</span>}
    </div>
    {question.description && (
      <div className="tq-question-description">
        <DescriptionMarkdown text={question.description} />
      </div>
    )}
  </>
);

interface QuestionCardProps {
  question: PlanQuestion;
  entries: string[];
  otherOpen: boolean;
  groupName: string;
  onChange: (entries: string[]) => void;
  onOtherOpenChange: (open: boolean) => void;
}

const QuestionCard: React.FC<QuestionCardProps> = ({
  question,
  entries,
  otherOpen,
  groupName,
  onChange,
  onOtherOpenChange,
}) => {
  const options = question.options ?? [];
  const hasOptions = options.length > 0;
  const optionValues = new Set(options.map((option) => option.value));
  const typed = entries.find((entry) => !optionValues.has(entry));

  // The typed text is a draft the input has to hold while the answer list only carries what is
  // committed; it resyncs whenever the answer changes underneath it (a clear, a document update).
  const [draft, setDraft] = useState(typed ?? "");
  const [seenTyped, setSeenTyped] = useState(typed);
  if (typed !== seenTyped) {
    setSeenTyped(typed);
    setDraft(typed ?? "");
  }

  const otherActive = typed !== undefined;
  const showOther = otherOpen || otherActive;

  const selectOption = (option: QuestionOption) => {
    if (!question.multiple) {
      onOtherOpenChange(false);
      onChange([option.value]);
      return;
    }
    onChange(
      entries.includes(option.value)
        ? entries.filter((entry) => entry !== option.value)
        : [...entries, option.value],
    );
  };

  const writeOther = (value: string) => {
    setDraft(value);
    if (!question.multiple) {
      onChange(value ? [value] : []);
      return;
    }
    const kept = entries.filter((entry) => optionValues.has(entry));
    onChange(value ? [...kept, value] : kept);
  };

  const toggleOther = () => {
    if (question.multiple && otherActive) {
      onOtherOpenChange(false);
      onChange(entries.filter((entry) => optionValues.has(entry)));
      return;
    }
    onOtherOpenChange(true);
    if (draft) writeOther(draft);
  };

  const textInput = (
    <input
      type="text"
      className="tq-text-input"
      value={draft}
      placeholder="Type your answer"
      aria-label={`Other answer for ${question.title || question.id}`}
      onChange={(e) => writeOther(e.target.value)}
    />
  );

  return (
    <div className="tq-question" data-question-id={question.id}>
      <QuestionHeading question={question} />
      {question.multiple && hasOptions && <div className="tq-question-hint">Select all that apply</div>}
      <div className="tq-options">
        {options.map((option) => (
          <OptionCard
            key={option.value}
            option={option}
            selected={entries.includes(option.value)}
            multiple={question.multiple}
            groupName={groupName}
            onSelect={() => selectOption(option)}
          />
        ))}

        {hasOptions && question.other && (
          <div
            className="tq-option tq-option--other"
            data-selected={otherActive}
            onClick={(e) => {
              if (ignoresCardClick(e)) return;
              toggleOther();
            }}
          >
            <label className="tq-option-main">
              <input
                type={question.multiple ? "checkbox" : "radio"}
                name={groupName}
                className="tq-option-input"
                checked={otherActive}
                onChange={toggleOther}
              />
              {question.multiple && (
                <span className="tq-option-box" aria-hidden="true">
                  <Check size={12} strokeWidth={3} />
                </span>
              )}
              <span className="tq-option-title">Other</span>
              {!question.multiple && otherActive && (
                <span className="tq-option-check" aria-hidden="true">
                  <Check size={16} />
                </span>
              )}
            </label>
            {showOther && textInput}
          </div>
        )}

        {!hasOptions && textInput}
      </div>
    </div>
  );
};

const ReadOnlyQuestion: React.FC<{ question: PlanQuestion; entries: string[] }> = ({ question, entries }) => (
  <div className="tq-question" data-question-id={question.id}>
    <QuestionHeading question={question} />
    {entries.length > 0 ? (
      <div className="tq-answer">
        {entries.map((entry) => (
          <span key={entry} className="tq-answer-value">
            {entryTitle(question, entry)}
          </span>
        ))}
      </div>
    ) : (
      <div className="tq-answer tq-answer--none">
        {question.optional ? "Not answered (not required)" : "Not answered (agent decided)"}
      </div>
    )}
  </div>
);

/**
 * The questions block as a form: every question of the block stacked, each with its option
 * cards, Other field or free-text input, and a footer with Clear and (optionally) Submit. The same
 * component presents settled answers when `readOnly`.
 */
export const QuestionsForm: React.FC<QuestionsFormProps> = ({
  questions,
  answers,
  otherOpen = {},
  readOnly = false,
  onAnswer,
  onOtherOpenChange,
  onClear,
  submit,
}) => {
  const reactId = useId();
  const hasAnyAnswers = Object.values(answers).some(hasEntries);
  const showFooter = !readOnly && ((onClear && hasAnyAnswers) || submit);

  return (
    <div className="tq-root" role="group">
      {questions.map((question) =>
        readOnly ? (
          <ReadOnlyQuestion key={question.id} question={question} entries={answers[question.id] ?? []} />
        ) : (
          <QuestionCard
            key={question.id}
            question={question}
            entries={answers[question.id] ?? []}
            otherOpen={otherOpen[question.id] ?? false}
            groupName={`tq-${reactId}-${question.id}`}
            onChange={(entries) => onAnswer?.(question.id, entries)}
            onOtherOpenChange={(open) => onOtherOpenChange?.(question.id, open)}
          />
        ),
      )}
      {showFooter && (
        <div className="tq-footer">
          {onClear && hasAnyAnswers && (
            <button
              type="button"
              className="tq-clear"
              onClick={onClear}
              aria-label={questions.length > 1 ? "Clear all answers in this block" : "Clear answer"}
            >
              Clear
            </button>
          )}
          {submit && (
            <button type="button" className="tq-submit" disabled={submit.disabled} onClick={submit.onSubmit}>
              {submit.label ?? "Submit response"}
            </button>
          )}
        </div>
      )}
    </div>
  );
};
