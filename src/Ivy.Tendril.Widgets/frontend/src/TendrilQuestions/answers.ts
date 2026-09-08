import type { PlanQuestion } from "../PlanMarkdown/questionsSchema";
import { answerEntries, otherEntry } from "../PlanMarkdown/questionsSchema";

/** Draft or document answers keyed by question id. An empty list means unanswered. */
export type AnswerMap = Record<string, string[]>;

export const hasEntries = (entries: string[] | undefined): entries is string[] =>
  entries !== undefined && entries.length > 0;

/** The answers the document already carries, which is what a fresh draft starts from. */
export function documentAnswers(questions: PlanQuestion[]): AnswerMap {
  const answers: AnswerMap = {};
  for (const question of questions) {
    if (question.answerPresent) answers[question.id] = answerEntries(question);
  }
  return answers;
}

/** Which questions have a typed (non-option) entry in the document, so their Other field opens. */
export function documentOtherOpen(questions: PlanQuestion[]): Record<string, boolean> {
  const open: Record<string, boolean> = {};
  for (const question of questions) {
    if (question.answerPresent && otherEntry(question) !== undefined) open[question.id] = true;
  }
  return open;
}

/** The option's title for an entry naming an option, else the entry itself (the user's own words). */
export function entryTitle(question: PlanQuestion, entry: string): string {
  return question.options?.find((option) => option.value === entry)?.title ?? entry;
}

/** Every required question has an answer, either drafted here or already in the document. */
export function canSubmitAnswers(questions: PlanQuestion[], answers: AnswerMap): boolean {
  return questions.every(
    (question) => question.optional || hasEntries(answers[question.id]) || question.answerPresent,
  );
}

/**
 * The markdown summary that travels with a submission, so the conversation records what was
 * decided in words rather than ids.
 */
export function buildAnswersSummary(questions: PlanQuestion[], answers: AnswerMap): string {
  const lines = ["Answers:"];
  for (const question of questions) {
    const entries = answers[question.id] ?? (question.answerPresent ? answerEntries(question) : []);
    const label = question.title || question.id;
    if (entries.length > 0) {
      lines.push(`- **${label}**: ${entries.map((entry) => entryTitle(question, entry)).join(", ")}`);
    } else if (question.optional) {
      lines.push(`- **${label}**: *(skipped)*`);
    }
  }
  return lines.join("\n");
}
