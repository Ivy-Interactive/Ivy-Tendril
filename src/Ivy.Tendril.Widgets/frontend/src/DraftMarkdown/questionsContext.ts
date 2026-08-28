import { createContext } from "react";

/** Reports a single answered, skipped or cleared question. */
export type AnswerCallback = (
  questionId: string,
  answer: string | string[] | null | undefined,
) => void;

/**
 * Carries the answer callback from `DraftMarkdown` down to a `QuestionsCallout`.
 *
 * A context rather than a prop because `BlockHandler` sits between the two and takes only the
 * react-markdown component signature. `undefined` means the host did not subscribe to
 * `OnAnswersChange`, which is what puts the callout in read-only mode.
 *
 * It lives in its own module so that `BlockHandler` can read it without importing
 * `DraftMarkdown.tsx`, which imports `BlockHandler` in turn.
 */
export const QuestionsAnswerContext = createContext<AnswerCallback | undefined>(undefined);
