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

/** A block is submittable once it carries at least one answer, drafted here or in the document. */
export function canSubmitAnswers(questions: PlanQuestion[], answers: AnswerMap): boolean {
  return questions.some((question) => hasEntries(answers[question.id]) || question.answerPresent);
}

/** Required questions with no drafted entry and no answer already in the document. */
export function unansweredRequired(questions: PlanQuestion[], answers: AnswerMap): PlanQuestion[] {
  return questions.filter(
    (question) => !question.optional && !hasEntries(answers[question.id]) && !question.answerPresent,
  );
}

const TITLE_TRUNCATE_LENGTH = 60;

const truncateTitle = (title: string): string =>
  title.length > TITLE_TRUNCATE_LENGTH ? `${title.slice(0, TITLE_TRUNCATE_LENGTH - 1)}…` : title;

/**
 * The footer note explaining why Submit is disabled or what a partial submit leaves to the agent.
 * Shared by `ChatQuestionsBlock` and `TendrilQuestions` so both surfaces read identically.
 */
export function submitNote(questions: PlanQuestion[], answers: AnswerMap): string | undefined {
  const hasAnyAnswers = questions.some(
    (question) => hasEntries(answers[question.id]) || question.answerPresent,
  );
  if (!hasAnyAnswers) return "Answer a question to submit.";

  const missing = unansweredRequired(questions, answers);
  if (missing.length === 0) return undefined;
  if (missing.length === 1) {
    const title = missing[0].title || missing[0].id;
    return `Unanswered: "${truncateTitle(title)}". Submitting leaves it to me.`;
  }
  return `${missing.length} questions unanswered. Submitting leaves them to me.`;
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
    } else {
      lines.push(`- **${label}**: *(no preference, your call)*`);
    }
  }
  return lines.join("\n");
}
