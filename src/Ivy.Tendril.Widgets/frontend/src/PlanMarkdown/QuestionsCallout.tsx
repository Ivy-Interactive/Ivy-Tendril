import React, { useMemo, useState } from "react";
import { parseQuestions } from "./questionsSchema";
import type { AnswerCallback, QuestionSubmitCallback } from "./questionsContext";
import { ChatQuestionsBlock } from "../TendrilQuestions/ChatQuestionsBlock";
import { QuestionsForm } from "../TendrilQuestions/QuestionsForm";
import { documentAnswers, documentOtherOpen } from "../TendrilQuestions/answers";

/**
 * The frame every questions block sits in. It carries no heading of its own — the question text is
 * the heading — so the block leads with what is actually being asked.
 *
 * The class is load-bearing beyond styling: annotations exclude anything inside it, and a host's
 * ScrollTo frames the whole block when it targets the block's first question.
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

export interface QuestionsCalloutProps {
  content: string;
  /** Absent when the host did not subscribe to `OnAnswersChange`, which means read-only. */
  onAnswer?: AnswerCallback;
  /** Present when rendered inside chat to enable interactive answers and submission. */
  onSubmit?: QuestionSubmitCallback;
}

export const QuestionsCallout: React.FC<QuestionsCalloutProps> = ({ content, onAnswer, onSubmit }) => {
  const parsed = useMemo(() => parseQuestions(content), [content]);
  const questions = parsed.kind === "invalid" ? [] : parsed.questions;

  // Answers live in the document in the draft flow: the host merges each reported change into the
  // revision and sends the new content back, so the document is the single source of truth and
  // they are recomputed from it on every render rather than held as state.
  const answers = useMemo(() => documentAnswers(questions), [questions]);
  const documentOpen = useMemo(() => documentOtherOpen(questions), [questions]);

  // An Other field the user just opened holds nothing yet, so the document cannot remember it.
  // That much is local: it is what keeps the field on screen between opening it and typing in it.
  const [opened, setOpened] = useState<Record<string, boolean>>({});

  // A block that does not parse is the pre-schema plain-text form, and there is nothing to render
  // but the text itself.
  if (questions.length === 0) {
    return <StaticCallout content={content} />;
  }

  // Inside a chat message answers are drafted locally and submitted in one go; a settled block
  // presents the decisions.
  if (!onAnswer && onSubmit) {
    if (questions.every((q) => q.answerPresent)) {
      return <QuestionsForm questions={questions} answers={answers} readOnly />;
    }

    return <ChatQuestionsBlock questions={questions} onSubmit={onSubmit} />;
  }

  // No subscriber means the host is showing a plan rather than working through it — the Review
  // stage, or any other read-only view. Present the decisions.
  if (!onAnswer) {
    return (
      <Shell>
        <QuestionsForm questions={questions} answers={answers} readOnly />
      </Shell>
    );
  }

  // One Clear for the whole block rather than one per question: the block is what the user is
  // working through, and a row of identical buttons down a stack reads as clutter. Optional
  // questions are included — being optional does not make an answer unretractable.
  const clearAll = () => {
    for (const question of questions) {
      if (question.answerPresent) onAnswer(question.id, undefined);
    }
    setOpened({});
  };

  // Each change is reported the moment it is made, so the draft flow has no Submit: an emptied
  // entry list means the question is unanswered again, not answered with nothing.
  return (
    <Shell>
      <QuestionsForm
        questions={questions}
        answers={answers}
        otherOpen={{ ...documentOpen, ...opened }}
        onAnswer={(questionId, entries) =>
          onAnswer(questionId, entries.length === 0 ? undefined : entries)
        }
        onOtherOpenChange={(questionId, open) =>
          setOpened((prev) => ({ ...prev, [questionId]: open }))
        }
        onClear={clearAll}
      />
    </Shell>
  );
};
