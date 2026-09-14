/**
 * A small syntax highlighter, so `CodeBlock` and `CodeInput` colour the same
 * code the same way.
 *
 * It is deliberately not a full parser and not a dependency: a wireframe needs
 * code to *read* as code — keywords, strings, numbers and comments telling
 * themselves apart at a glance — not to be semantically correct about generics.
 * A handful of token classes over a C-family scan covers every language the
 * library is likely to be shown with, and degrades to plain text for the rest.
 */
export type TokenKind = "plain" | "comment" | "string" | "number" | "keyword" | "function" | "punctuation";
export interface Token {
    text: string;
    kind: TokenKind;
}
/**
 * Splits `code` into one token array per line. Tokens never span a newline, so
 * a multi-line comment or template string comes back as one token per line and
 * both renderers can lay code out line by line.
 */
export declare function tokenize(code: string, language?: string): Token[][];
/**
 * Token colours, in the same desaturated pencil range as the rest of the
 * palette — enough hue to tell a string from a keyword, not enough to look like
 * an IDE theme dropped into a wireframe.
 */
export declare const CODE_COLORS: Record<TokenKind, string>;
