import { createContext } from "react";

/**
 * Where the plan being rendered serves its wireframes, ending in a slash
 * (for example `/__wireframes/123/`). A `wireframe` fence resolves its name against it.
 *
 * A context rather than a prop because `BlockHandler` sits between `PlanMarkdown` and the block
 * and takes only the react-markdown component signature. `undefined` means the markdown is not a
 * plan (chat, an agent's summary), and the block renders a placeholder instead of a preview.
 */
export const WireframeBaseContext = createContext<string | undefined>(undefined);