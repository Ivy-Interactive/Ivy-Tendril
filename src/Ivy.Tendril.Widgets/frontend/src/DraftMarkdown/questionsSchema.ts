import { parse } from "yaml";

/**
 * A TypeScript mirror of the `questions` block schema, plus a tolerant reader.
 *
 * The schema of record lives in `Prompts/Plans.md` and is enforced C#-side on the
 * `write-revision` path. This reader is deliberately **tolerant, not a validator**: it reports
 * `invalid` rather than throwing, because the widget's job is to display a plan, never to refuse
 * to.
 */

export interface QuestionOption {
  title: string;
  description?: string;
  value: string;
  recommended?: boolean;
}

export interface PlanQuestion {
  /** Stable and unique across the whole document — `OnAnswersChange` identifies a question by it. */
  id: string;
  title: string;
  header?: string;
  description?: string;
  /** True when several options may be selected. Answers are then always a list. */
  multiple: boolean;
  /** Whether the user may type a value of their own. Defaults to true, per the schema. */
  other: boolean;
  options?: QuestionOption[];
  /** The raw `answer` node: absent, null, a scalar or a list. */
  answer?: unknown;
  /** Whether the `answer` key was present at all — absent and explicit null mean different things. */
  answerPresent: boolean;
}

export type ParsedQuestions =
  | { kind: "questions"; questions: PlanQuestion[] }
  /**
   * The body is not a mapping with a `questions` key — the plain-text form that predates the
   * schema, which existing revisions contain — or its `id`s are missing or collide within this
   * block. Renders as the static callout.
   */
  | { kind: "invalid" };

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function optionalString(value: unknown): string | undefined {
  return typeof value === "string" && value.length > 0 ? value : undefined;
}

function readOptions(raw: unknown): QuestionOption[] | undefined {
  if (!Array.isArray(raw)) return undefined;

  return raw.filter(isRecord).map((option) => ({
    title: typeof option.title === "string" ? option.title : "",
    description: optionalString(option.description),
    value: typeof option.value === "string" ? option.value : "",
    recommended: option.recommended === true,
  }));
}

/**
 * Reads one fence body. Never throws: malformed YAML, prose, and a mapping without a `questions`
 * key all come back as `kind: "invalid"`.
 *
 * `id` is required and must be unique **within this block**. A cross-block collision is out of
 * reach here — this parser only ever sees one fence — and belongs in the C# validator, the one
 * component with the whole document in view.
 */
export function parseQuestions(body: string): ParsedQuestions {
  let raw: unknown;
  try {
    raw = parse(body);
  } catch {
    return { kind: "invalid" };
  }

  if (!isRecord(raw) || !Array.isArray(raw.questions)) return { kind: "invalid" };

  const seen = new Set<string>();
  const questions: PlanQuestion[] = [];

  for (const entry of raw.questions) {
    if (!isRecord(entry)) return { kind: "invalid" };

    const id = entry.id;
    if (typeof id !== "string" || id.length === 0 || seen.has(id)) return { kind: "invalid" };
    seen.add(id);

    questions.push({
      id,
      title: typeof entry.title === "string" ? entry.title : "",
      header: optionalString(entry.header),
      description: optionalString(entry.description),
      multiple: entry.multiple === true,
      other: entry.other !== false,
      options: readOptions(entry.options),
      answer: entry.answer,
      answerPresent: Object.prototype.hasOwnProperty.call(entry, "answer"),
    });
  }

  return { kind: "questions", questions };
}

/**
 * The answer as a list of entries, which is what drives selection state. Empty when the question
 * is unanswered or explicitly skipped. An entry matching an option's `value` is that option; an
 * entry matching nothing is the user's own free text.
 */
export function answerEntries(question: PlanQuestion): string[] {
  if (!question.answerPresent || question.answer === null || question.answer === undefined) return [];

  const raw = Array.isArray(question.answer) ? question.answer : [question.answer];
  return raw.filter((entry): entry is string => typeof entry === "string");
}

/** `answer: null` — asked and deliberately skipped. */
export function isSkipped(question: PlanQuestion): boolean {
  return question.answerPresent && question.answer === null;
}

/** The answer entry that matches no option, i.e. what the user typed into "Other". */
export function otherEntry(question: PlanQuestion): string | undefined {
  const values = new Set((question.options ?? []).map((option) => option.value));
  return answerEntries(question).find((entry) => !values.has(entry));
}
