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
  /**
   * Whether the plan is complete without an answer. Worth asking, not worth blocking on — an index
   * treats an unanswered optional question as settled.
   */
  optional: boolean;
  options?: QuestionOption[];
  /** The raw `answer` node: absent, a scalar or a list. */
  answer?: unknown;
  /**
   * Whether the question carries an answer. A present-but-null `answer` is not a state the schema
   * has — the validator rejects it — so it reads as unanswered.
   */
  answerPresent: boolean;
}

export type ParsedQuestions =
  | { kind: "questions"; questions: PlanQuestion[] }
  /**
   * The body is none of the shapes below — the plain-text form that predates the schema, which
   * existing revisions contain — or its `id`s are missing or collide within this block. Renders as
   * the static callout.
   */
  | { kind: "invalid" };

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/**
 * A YAML scalar as the string the author wrote.
 *
 * The schema says these fields are strings, but YAML decides otherwise: an option titled `4.2` or
 * valued `2024` parses as a number, and a `title: yes` as a boolean. The C# validator accepts all
 * of them — YamlDotNet coerces a scalar into a `string` field — so a block that lints clean would
 * otherwise render blank here. Coerce rather than reject: the widget's job is to display a plan.
 */
function scalarString(value: unknown): string | undefined {
  if (typeof value === "string") return value;
  if (typeof value === "number" || typeof value === "boolean") return String(value);
  return undefined;
}

function requiredString(value: unknown): string {
  return scalarString(value) ?? "";
}

function optionalString(value: unknown): string | undefined {
  const text = scalarString(value);
  return text !== undefined && text.length > 0 ? text : undefined;
}

/**
 * A mapping carrying an `id`. That is the one field a question cannot do without — an answer travels
 * as an id and a value and nothing else — and it is what tells a wrapper-less block apart from a
 * legacy bullet list of prose.
 */
function looksLikeQuestion(value: unknown): boolean {
  return isRecord(value) && "id" in value;
}

/**
 * The question list of one fence body, or undefined when the body is not a questions block at all.
 *
 * Three shapes say the same thing, and agents write all three: the canonical `questions:` mapping,
 * the list written bare without that wrapper, and a single question written without either. The
 * fence already says `questions`, so repeating the word inside reads as redundant — and a block
 * turned away here renders as a code listing the user cannot answer.
 *
 * The C# `QuestionBlockParser` and `QuestionAnswers` accept exactly these three, and must keep
 * agreeing with this: a shape one side reads and another does not is a picker whose answer goes
 * nowhere.
 */
function questionList(raw: unknown): unknown[] | undefined {
  if (Array.isArray(raw)) return raw.length > 0 && raw.every(looksLikeQuestion) ? raw : undefined;
  if (!isRecord(raw)) return undefined;
  if (Array.isArray(raw.questions)) return raw.questions;
  return looksLikeQuestion(raw) ? [raw] : undefined;
}

function readOptions(raw: unknown): QuestionOption[] | undefined {
  if (!Array.isArray(raw)) return undefined;

  return raw.filter(isRecord).map((option) => ({
    title: requiredString(option.title),
    description: optionalString(option.description),
    value: requiredString(option.value),
    recommended: option.recommended === true,
  }));
}

/**
 * Reads one fence body. Never throws: malformed YAML, prose, and a mapping that is none of the
 * shapes `questionList` knows all come back as `kind: "invalid"`.
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

  const list = questionList(raw);
  if (list === undefined) return { kind: "invalid" };

  const seen = new Set<string>();
  const questions: PlanQuestion[] = [];

  for (const entry of list) {
    if (!isRecord(entry)) return { kind: "invalid" };

    const id = scalarString(entry.id);
    if (id === undefined || id.length === 0 || seen.has(id)) return { kind: "invalid" };
    seen.add(id);

    questions.push({
      id,
      title: requiredString(entry.title),
      header: optionalString(entry.header),
      description: optionalString(entry.description),
      multiple: entry.multiple === true,
      other: entry.other !== false,
      optional: entry.optional === true,
      options: readOptions(entry.options),
      answer: entry.answer,
      answerPresent:
        Object.prototype.hasOwnProperty.call(entry, "answer") && entry.answer !== null,
    });
  }

  return { kind: "questions", questions };
}

/**
 * The answer as a list of entries, which is what drives selection state. Empty when the question is
 * unanswered. An entry matching an option's `value` is that option; an entry matching nothing is
 * the user's own free text.
 */
export function answerEntries(question: PlanQuestion): string[] {
  if (!question.answerPresent || question.answer === undefined) return [];

  // Same coercion as the option values these are matched against, so `answer: 2024` still selects
  // the option valued `2024`.
  const raw = Array.isArray(question.answer) ? question.answer : [question.answer];
  return raw.map(scalarString).filter((entry): entry is string => entry !== undefined);
}

/** The answer entry that matches no option, i.e. what the user typed into "Other". */
export function otherEntry(question: PlanQuestion): string | undefined {
  const values = new Set((question.options ?? []).map((option) => option.value));
  return answerEntries(question).find((entry) => !values.has(entry));
}
