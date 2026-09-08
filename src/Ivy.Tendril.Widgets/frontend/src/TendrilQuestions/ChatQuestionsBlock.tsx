import React, { useContext, useState } from "react";
import type { PlanQuestion } from "../PlanMarkdown/questionsSchema";
import { QuestionsDraftContext } from "../PlanMarkdown/questionsContext";
import type { QuestionSubmitCallback, QuestionsDraftState } from "../PlanMarkdown/questionsContext";
import { QuestionsForm } from "./QuestionsForm";
import {
  buildAnswersSummary,
  canSubmitAnswers,
  documentAnswers,
  documentOtherOpen,
  hasEntries,
} from "./answers";

interface ChatQuestionsBlockProps {
  questions: PlanQuestion[];
  onSubmit: QuestionSubmitCallback;
}

/**
 * A questions block inside a chat message. Answers are drafted locally and sent in one go with
 * Submit; the draft lives in the surrounding `QuestionsDraftContext` store so it outlives the
 * re-renders and remounts a streaming conversation causes.
 */
export const ChatQuestionsBlock: React.FC<ChatQuestionsBlockProps> = ({ questions, onSubmit }) => {
  // Question ids are unique per block by schema, so they identify the block within its message.
  const blockKey = questions.map((question) => question.id).join("|");
  const store = useContext(QuestionsDraftContext);

  const [draft, setDraft] = useState<QuestionsDraftState>(
    () =>
      store?.read(blockKey) ?? {
        answers: documentAnswers(questions),
        otherOpen: documentOtherOpen(questions),
      },
  );

  const update = (next: (prev: QuestionsDraftState) => QuestionsDraftState) =>
    setDraft((prev) => {
      const state = next(prev);
      store?.write(blockKey, state);
      return state;
    });

  const handleAnswer = (questionId: string, entries: string[]) =>
    update((prev) => {
      const answers = { ...prev.answers };
      if (entries.length === 0) delete answers[questionId];
      else answers[questionId] = entries;
      return { ...prev, answers };
    });

  const handleOtherOpenChange = (questionId: string, open: boolean) =>
    update((prev) => ({ ...prev, otherOpen: { ...prev.otherOpen, [questionId]: open } }));

  const clearAll = () => update(() => ({ answers: {}, otherOpen: {} }));

  const hasAnyAnswers = Object.values(draft.answers).some(hasEntries);
  const submitEnabled = canSubmitAnswers(questions, draft.answers);

  const handleSubmit = () => {
    if (!submitEnabled) return;
    onSubmit(draft.answers, buildAnswersSummary(questions, draft.answers));
    // The answers now live in the message document, so nothing is left to draft for this block.
    store?.clear(blockKey);
  };

  return (
    <QuestionsForm
      questions={questions}
      answers={draft.answers}
      otherOpen={draft.otherOpen}
      onAnswer={handleAnswer}
      onOtherOpenChange={handleOtherOpenChange}
      onClear={hasAnyAnswers ? clearAll : undefined}
      submit={{ label: "Submit response", disabled: !submitEnabled, onSubmit: handleSubmit }}
    />
  );
};
