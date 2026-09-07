import { createContext } from "react";

/** Reports a single answered, skipped or cleared question. */
export type AnswerCallback = (
  questionId: string,
  answer: string | string[] | null | undefined,
) => void;

/** Reports submitted answers for an interactive questions block in chat. */
export type QuestionSubmitCallback = (
  answers: Record<string, string[]>,
  summaryText: string,
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

/**
 * Carries the submit callback down to a `QuestionsCallout` when rendered inside a chat message.
 */
export const QuestionsSubmitContext = createContext<QuestionSubmitCallback | undefined>(undefined);

/** In-progress answers for one questions block, before the user submits them. */
export interface QuestionsDraftState {
  /** Selected option values and typed free text, by question id. */
  answers: Record<string, string[]>;
  /** Whether the Other field is open, by question id. Not derivable when nothing is typed yet. */
  otherOpen: Record<string, boolean>;
}

export interface QuestionsDraftStore {
  read(blockKey: string): QuestionsDraftState | undefined;
  write(blockKey: string, state: QuestionsDraftState): void;
  clear(blockKey: string): void;
}

/**
 * Carries a per-message draft store down to `QuestionsCallout` so a block's in-progress answers
 * outlive a remount of the message row (a streaming update, a session switch and back).
 *
 * Absent outside chat, which is what keeps plan views on transient state.
 */
export const QuestionsDraftContext = createContext<QuestionsDraftStore | undefined>(undefined);
