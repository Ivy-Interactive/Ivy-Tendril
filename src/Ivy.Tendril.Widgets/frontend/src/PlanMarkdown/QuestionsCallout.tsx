import React, { useContext, useEffect, useMemo, useRef, useState } from "react";
import { parseQuestions } from "./questionsSchema";
import type { AnswerCallback, QuestionSubmitCallback } from "./questionsContext";
import { QuestionsDraftContext } from "./questionsContext";
import { ChatQuestionsBlock } from "../TendrilQuestions/ChatQuestionsBlock";
import { QuestionsForm } from "../TendrilQuestions/QuestionsForm";
import { documentAnswers, documentOtherOpen } from "../TendrilQuestions/answers";

/**
 * How long an optimistically-shown answer is trusted before the document has to have caught up.
 * Guards against a host that never echoes back (a `TryApply` miss on a document that moved on, a
 * dropped connection) leaving the picker permanently out of sync with what was actually saved.
 */
const PENDING_ANSWER_TIMEOUT_MS = 4000;

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

  // An answer just reported to the host, shown immediately instead of waiting for the document to
  // round-trip. `undefined` records a pending clear. Reconciled below once the document agrees.
  const [pending, setPending] = useState<Record<string, string[] | undefined>>({});
  const pendingTimers = useRef<Record<string, ReturnType<typeof setTimeout>>>({});

  const clearPendingTimer = (questionId: string) => {
    const timer = pendingTimers.current[questionId];
    if (timer !== undefined) {
      clearTimeout(timer);
      delete pendingTimers.current[questionId];
    }
  };

  useEffect(() => {
    const timers = pendingTimers.current;
    return () => {
      for (const timer of Object.values(timers)) clearTimeout(timer);
    };
  }, []);

  // Once the document carries a value matching what was pending, the document is the single
  // source of truth again — drop the overlay entry (and its safety-valve timer) for that question.
  useEffect(() => {
    const settled = Object.keys(pending).filter((questionId) => {
      const expected = pending[questionId];
      const actual = answers[questionId];
      if (expected === undefined) return actual === undefined;
      return (
        actual !== undefined &&
        actual.length === expected.length &&
        actual.every((entry, index) => entry === expected[index])
      );
    });
    if (settled.length === 0) return;
    for (const questionId of settled) clearPendingTimer(questionId);
    setPending((prev) => {
      const next = { ...prev };
      for (const questionId of settled) delete next[questionId];
      return next;
    });
  }, [answers, pending]);

  // Records an optimistic answer and arms the safety-valve timeout that drops it if the document
  // never agrees.
  const setPendingAnswer = (questionId: string, value: string[] | undefined) => {
    clearPendingTimer(questionId);
    setPending((prev) => ({ ...prev, [questionId]: value }));
    pendingTimers.current[questionId] = setTimeout(() => {
      delete pendingTimers.current[questionId];
      setPending((prev) => {
        if (!(questionId in prev)) return prev;
        const next = { ...prev };
        delete next[questionId];
        return next;
      });
    }, PENDING_ANSWER_TIMEOUT_MS);
  };

  // The overlay the draft flow actually renders: the document, with any still-pending answers
  // layered on top.
  const displayAnswers = useMemo(() => {
    const overlay = { ...answers };
    for (const questionId of Object.keys(pending)) {
      const entries = pending[questionId];
      if (entries === undefined) delete overlay[questionId];
      else overlay[questionId] = entries;
    }
    return overlay;
  }, [answers, pending]);

  const displayOpen = useMemo(() => {
    const overlay = { ...documentOpen };
    for (const question of questions) {
      if (!(question.id in pending)) continue;
      const entries = pending[question.id];
      if (entries === undefined) {
        delete overlay[question.id];
        continue;
      }
      const optionValues = new Set((question.options ?? []).map((option) => option.value));
      const hasTyped = entries.some((entry) => !optionValues.has(entry));
      if (hasTyped) overlay[question.id] = true;
      else delete overlay[question.id];
    }
    return overlay;
  }, [documentOpen, pending, questions]);

  // Once a chat block's document carries every answer, the submitted-draft view below takes over —
  // the store entry that kept it on screen during the round trip has served its purpose.
  const draftStore = useContext(QuestionsDraftContext);
  const allAnswered = questions.length > 0 && questions.every((q) => q.answerPresent);
  useEffect(() => {
    if (!onAnswer && onSubmit && allAnswered) {
      draftStore?.clear(questions.map((q) => q.id).join("|"));
    }
  }, [onAnswer, onSubmit, allAnswered, draftStore, questions]);

  // A block that does not parse is the pre-schema plain-text form, and there is nothing to render
  // but the text itself.
  if (questions.length === 0) {
    return <StaticCallout content={content} />;
  }

  // Inside a chat message answers are drafted locally and submitted in one go; a settled block
  // presents the decisions.
  if (!onAnswer && onSubmit) {
    if (allAnswered) {
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
      if (question.answerPresent) {
        setPendingAnswer(question.id, undefined);
        onAnswer(question.id, undefined);
      }
    }
    setOpened({});
  };

  // Each change is reported the moment it is made, so the draft flow has no Submit: an emptied
  // entry list means the question is unanswered again, not answered with nothing.
  return (
    <Shell>
      <QuestionsForm
        questions={questions}
        answers={displayAnswers}
        otherOpen={{ ...displayOpen, ...opened }}
        onAnswer={(questionId, entries) => {
          const value = entries.length === 0 ? undefined : entries;
          setPendingAnswer(questionId, value);
          onAnswer(questionId, value);
        }}
        onOtherOpenChange={(questionId, open) =>
          setOpened((prev) => ({ ...prev, [questionId]: open }))
        }
        onClear={clearAll}
      />
    </Shell>
  );
};
