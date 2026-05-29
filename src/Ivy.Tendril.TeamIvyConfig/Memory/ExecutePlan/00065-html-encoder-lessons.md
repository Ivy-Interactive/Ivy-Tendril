Key learnings from implementing HTML Encoder/Decoder (Plan 00065):

1. **Entity Map for Encoding**: Simple HTML entity encoding works well with a character-to-entity map. The DOMParser/textContent approach mentioned in the plan doesn't encode quotes and forward slashes, so manual entity replacement is more comprehensive.

2. **Static Components ESLint Rule**: The `react-hooks/static-components` rule flags dynamic lazy loading patterns. For truly dynamic component loading (needed in this app's architecture), the pattern with Map caching requires an eslint-disable comment since the component is still technically created during render.

3. **TypeScript verbatimModuleSyntax**: When this compiler option is enabled, all type-only imports must use `import { type }` syntax. This caught an issue with ComponentType import.

4. **ESLint empty interfaces**: TypeScript `@typescript-eslint/no-empty-object-type` rule requires removing interfaces with no members. Use the parent interface directly instead of extending an empty interface.

5. **DOMParser Error Detection**: DOMParser doesn't throw errors for malformed HTML during parsing. Using a parsererror selector check doesn't work reliably in all cases. For this use case, relying on DOMParser's robust parsing was sufficient since it handles malformed entities gracefully.
