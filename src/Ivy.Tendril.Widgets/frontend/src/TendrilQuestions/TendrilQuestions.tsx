import React, { useMemo, useState } from "react";
import type { IvyEventHandler } from "../TendrilProcessViewer/types";
import { parseQuestions } from "../PlanMarkdown/questionsSchema";
import { QuestionsForm } from "./QuestionsForm";
import {
  buildAnswersSummary,
  canSubmitAnswers,
  documentAnswers,
  documentOtherOpen,
  hasEntries,
} from "./answers";
import type { AnswerMap } from "./answers";
import "./tendril-questions.css";

export interface TendrilQuestionsProps {
  id: string;
  events?: string[];
  eventHandler: IvyEventHandler;
  /** The body of a `questions` fence: YAML in the plan questions schema. */
  content?: string;
  readOnly?: boolean;
  showSubmit?: boolean;
  submitLabel?: string;
}

/**
 * The questions block as a standalone widget, for hosts that hold the YAML themselves (a plan's
 * questions section, a chat message). Every change is reported through OnAnswer with the full
 * entry list of the question it belongs to; OnSubmit additionally sends every answer at once with a
 * markdown summary, for hosts that want the decisions in one message.
 */
export const TendrilQuestions: React.FC<TendrilQuestionsProps> = ({
  id,
  events = [],
  eventHandler,
  content = "",
  readOnly = false,
  showSubmit = false,
  submitLabel,
}) => {
  const parsed = useMemo(() => parseQuestions(content), [content]);
  const questions = parsed.kind === "questions" ? parsed.questions : [];

  const [answers, setAnswers] = useState<AnswerMap>(() => documentAnswers(questions));
  const [otherOpen, setOtherOpen] = useState<Record<string, boolean>>(() => documentOtherOpen(questions));
  const [seenContent, setSeenContent] = useState(content);
  if (content !== seenContent) {
    // The document changed underneath us: the draft restarts from what it now says.
    setSeenContent(content);
    setAnswers(documentAnswers(questions));
    setOtherOpen(documentOtherOpen(questions));
  }

  if (parsed.kind === "invalid" || questions.length === 0) {
    return (
      <div className="tq-root">
        <pre className="tq-static">{content}</pre>
      </div>
    );
  }

  const emit = (eventName: string, payload: unknown) => {
    if (events.includes(eventName)) eventHandler(eventName, id, [payload]);
  };

  const handleAnswer = (questionId: string, entries: string[]) => {
    setAnswers((prev) => {
      const next = { ...prev };
      if (entries.length === 0) delete next[questionId];
      else next[questionId] = entries;
      return next;
    });
    emit("OnAnswer", { questionId, values: entries });
  };

  const clearAll = () => {
    for (const question of questions) {
      if (hasEntries(answers[question.id])) emit("OnAnswer", { questionId: question.id, values: [] });
    }
    setAnswers({});
    setOtherOpen({});
  };

  if (readOnly) {
    return <QuestionsForm questions={questions} answers={documentAnswers(questions)} readOnly />;
  }

  const submitEnabled = canSubmitAnswers(questions, answers);

  return (
    <QuestionsForm
      questions={questions}
      answers={answers}
      otherOpen={otherOpen}
      onAnswer={handleAnswer}
      onOtherOpenChange={(questionId, open) => setOtherOpen((prev) => ({ ...prev, [questionId]: open }))}
      onClear={Object.values(answers).some(hasEntries) ? clearAll : undefined}
      submit={
        showSubmit
          ? {
              label: submitLabel || "Submit response",
              disabled: !submitEnabled,
              onSubmit: () => emit("OnSubmit", { answers, summary: buildAnswersSummary(questions, answers) }),
            }
          : undefined
      }
    />
  );
};
