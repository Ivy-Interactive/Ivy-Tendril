import { parseDocument } from "yaml";

/**
 * Locating `questions` fences in markdown source, and editing them in place.
 *
 * Fence tracking follows CommonMark, exactly as the C# `QuestionBlockParser` does and for the same
 * reason: a `questions` fence written inside a longer fence is documentation, not a question. A
 * fence opened with N of a delimiter is closed only by a run of N or more of the same delimiter,
 * and any info line seen while a longer fence is open is body text. Without this, a plan that
 * documents the format gets its own examples turned into live pickers.
 */

const INFO_WORD = "questions";

export interface QuestionBlockSource {
  /** 0-based, document order. */
  index: number;
  /** Offset into the markdown of the first body character. */
  bodyStart: number;
  /** Offset just past the last body character. */
  bodyEnd: number;
  /**
   * The body, verbatim: `markdown.slice(bodyStart, bodyEnd)`. Includes the newline that terminates
   * the last body line, and excludes both fence lines.
   */
  body: string;
}

interface ScannedBlock extends QuestionBlockSource {
  /** Indentation of the opening fence, 0-3 spaces. Body lines carry it too. */
  indent: number;
  /** Offset of the `questions` word on the opening fence's info line. */
  wordStart: number;
  /** Offset just past that word. */
  wordEnd: number;
}

interface SourceLine {
  /** Line content, without its terminator and without a trailing CR. */
  text: string;
  /** Offset of the first character. */
  start: number;
  /** Offset just past `text` — at the CR of a CRLF pair, or at the LF, or at end of input. */
  textEnd: number;
  /** Offset of the start of the next line. */
  next: number;
}

interface Fence {
  delimiter: string;
  length: number;
  indent: number;
  info: string;
}

function splitLines(markdown: string): SourceLine[] {
  const lines: SourceLine[] = [];
  let pos = 0;

  for (;;) {
    const lf = markdown.indexOf("\n", pos);
    const terminated = lf !== -1;
    const lineEnd = terminated ? lf : markdown.length;
    const textEnd = lineEnd > pos && markdown[lineEnd - 1] === "\r" ? lineEnd - 1 : lineEnd;

    lines.push({
      text: markdown.slice(pos, textEnd),
      start: pos,
      textEnd,
      next: terminated ? lf + 1 : markdown.length,
    });

    if (!terminated) return lines;
    pos = lf + 1;
  }
}

function matchFence(line: string): Fence | null {
  let indent = 0;
  while (indent < line.length && indent < 4 && line[indent] === " ") indent++;
  if (indent > 3 || indent >= line.length) return null;

  const delimiter = line[indent];
  if (delimiter !== "`" && delimiter !== "~") return null;

  let end = indent;
  while (end < line.length && line[end] === delimiter) end++;

  const length = end - indent;
  if (length < 3) return null;

  const info = line.slice(end).trim();

  // A backtick fence's info string may not contain a backtick (CommonMark), which is what keeps
  // inline code like ``a ``` b`` from being read as a fence.
  if (delimiter === "`" && info.includes("`")) return null;

  return { delimiter, length, indent, info };
}

/** The first whitespace-delimited word of the info string, matched verbatim like the renderer. */
function isQuestionsInfo(info: string): boolean {
  if (info.length === 0) return false;
  const end = info.search(/[ \t]/);
  return (end < 0 ? info : info.slice(0, end)) === INFO_WORD;
}

/** Offsets of the info string's first word within `line`, or null when there is no info. */
function wordSpan(line: SourceLine, fence: Fence): { wordStart: number; wordEnd: number } {
  const afterRun = line.start + fence.indent + fence.length;
  const raw = line.text.slice(fence.indent + fence.length);
  const leading = raw.length - raw.trimStart().length;
  const wordStart = afterRun + leading;

  const trimmed = raw.trimStart();
  const end = trimmed.search(/[ \t]/);
  const wordLength = end < 0 ? trimmed.trimEnd().length : end;

  return { wordStart, wordEnd: wordStart + wordLength };
}

function scan(markdown: string): ScannedBlock[] {
  const blocks: ScannedBlock[] = [];
  if (!markdown) return blocks;

  const lines = splitLines(markdown);

  let open = false;
  let openChar = "";
  let openLength = 0;
  let openIndent = 0;
  let isQuestions = false;
  let bodyStart = 0;
  let wordStart = 0;
  let wordEnd = 0;

  for (const line of lines) {
    const fence = matchFence(line.text);

    if (!open) {
      if (!fence) continue;

      open = true;
      openChar = fence.delimiter;
      openLength = fence.length;
      openIndent = fence.indent;
      isQuestions = isQuestionsInfo(fence.info);
      bodyStart = line.next;
      ({ wordStart, wordEnd } = wordSpan(line, fence));
      continue;
    }

    // Only a bare run of the same delimiter, at least as long as the opener, closes a fence.
    if (fence && fence.delimiter === openChar && fence.length >= openLength && fence.info.length === 0) {
      if (isQuestions) {
        blocks.push({
          index: blocks.length,
          bodyStart,
          bodyEnd: line.start,
          body: markdown.slice(bodyStart, line.start),
          indent: openIndent,
          wordStart,
          wordEnd,
        });
      }
      open = false;
    }
  }

  // An unterminated fence runs to the end of the document (CommonMark).
  if (open && isQuestions) {
    blocks.push({
      index: blocks.length,
      bodyStart,
      bodyEnd: markdown.length,
      body: markdown.slice(bodyStart, markdown.length),
      indent: openIndent,
      wordStart,
      wordEnd,
    });
  }

  return blocks;
}

/** Every top-level `questions` block in `markdown`, in document order. */
export function scanQuestionBlocks(markdown: string): QuestionBlockSource[] {
  return scan(markdown).map(({ index, bodyStart, bodyEnd, body }) => ({ index, bodyStart, bodyEnd, body }));
}

/**
 * Rewrites each top-level opening info line from `questions` to `questions_<index>`, so the
 * renderer learns which block it is dispatching. `BlockHandler` derives the language from
 * `/language-(\w+)/` and `_` is a `\w` character, so the tagged word survives react-markdown
 * untouched. The info line is never rendered, so annotation offsets — computed over rendered
 * text — are unaffected.
 */
export function tagQuestionBlocks(markdown: string): string {
  const blocks = scan(markdown);
  if (blocks.length === 0) return markdown;

  let out = "";
  let cursor = 0;
  for (const block of blocks) {
    out += markdown.slice(cursor, block.wordStart) + `${INFO_WORD}_${block.index}`;
    cursor = block.wordEnd;
  }

  return out + markdown.slice(cursor);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/**
 * The path to `questionId`'s `answer` key inside a parsed block body, or null when the block holds
 * no such question.
 *
 * The three shapes are the ones `questionsSchema.parseQuestions` reads, and the path differs for
 * each: a `questions:` mapping nests the list under that key, a bare sequence is the list, and a
 * single question written without either wrapper is the question itself. Anything the reader renders
 * as a picker has to be writable here, or answering it would throw.
 */
function answerPath(js: unknown, questionId: string): (string | number)[] | null {
  const isTarget = (q: unknown) => isRecord(q) && q.id === questionId;

  if (Array.isArray(js)) {
    const index = js.findIndex(isTarget);
    return index < 0 ? null : [index, "answer"];
  }

  if (!isRecord(js)) return null;

  if (Array.isArray(js.questions)) {
    const index = js.questions.findIndex(isTarget);
    return index < 0 ? null : ["questions", index, "answer"];
  }

  return isTarget(js) ? ["answer"] : null;
}

function dedent(body: string, indent: number): string {
  if (indent === 0) return body;
  return body
    .split("\n")
    .map((line) => {
      let strip = 0;
      while (strip < indent && strip < line.length && line[strip] === " ") strip++;
      return line.slice(strip);
    })
    .join("\n");
}

function reindent(body: string, indent: number): string {
  if (indent === 0) return body;
  const pad = " ".repeat(indent);
  return body
    .split("\n")
    .map((line) => (line.length === 0 ? line : pad + line))
    .join("\n");
}

/**
 * Writes `answer` onto the question identified by `questionId` inside block `blockIndex`, and
 * returns the whole document with only that block's body replaced.
 *
 * The body round-trips through `parseDocument`, so comments, key order and scalar style survive;
 * every byte outside `[bodyStart, bodyEnd)` — including other blocks — is untouched. Passing
 * `undefined` deletes the `answer` key (back to unanswered); passing `null` writes an explicit
 * null (asked and deliberately skipped).
 *
 * Throws when `blockIndex` is out of range or `questionId` matches no question in that block —
 * both are caller-side bugs, not the malformed-document case the renderer tolerates.
 */
export function setAnswer(
  markdown: string,
  blockIndex: number,
  questionId: string,
  answer: string | string[] | null | undefined,
): string {
  const blocks = scan(markdown);
  const block = blocks[blockIndex];
  if (!block) {
    throw new Error(`setAnswer: no questions block at index ${blockIndex} (found ${blocks.length})`);
  }

  const doc = parseDocument(dedent(block.body, block.indent));
  const path = answerPath(doc.toJS(), questionId);
  if (!path) {
    throw new Error(`setAnswer: block ${blockIndex} has no question with id "${questionId}"`);
  }

  if (answer === undefined) {
    doc.deleteIn(path);
  } else {
    doc.setIn(path, answer);
  }

  // lineWidth: 0 disables folding, so a long answer is never re-wrapped into a shape the author
  // did not write.
  const updated = reindent(doc.toString({ lineWidth: 0 }), block.indent);

  return markdown.slice(0, block.bodyStart) + updated + markdown.slice(block.bodyEnd);
}
