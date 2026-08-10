# Ivy Tendril Release Notes - v1.1.27 (2026-08-07)

> [!NOTE]
> Sign up on [https://ivy.app/](https://ivy.app/) to get release notes directly to your inbox.

## Highlights & Features

### Plan Creation & Project Picker
- **Responsive Project Selector**: Switched to a 3-column card grid with segmented toggle styling (`SelectInputVariant.Toggle`) when managing 6 or fewer projects, and automatically upgrade to a searchable `SelectInput` dropdown when managing more than 6 projects.
- **Auto Mode Icon**: Integrated `Icons.WandSparkles` into the Auto project selector option.
- **Quick Project Creation**: Added inline "+ Add New Project" navigation directly within the project picker dropdown.

### Chat App Enhancements
- **Clean Session Titles**: Automatically strip trailing `...` and ellipsis characters when auto-generating chat session titles from initial prompts or renaming sessions.
- **LaTeX Math Rendering**: Added support for rendering KaTeX math expressions (`$...$` and `$$...$$`) in chat messages, markdown widgets, and `AgentViewer`.
- **Sidebar & History Navigation**: Fully restored and polished the Chat App sidebar layout with history search, session rename/delete dialogs, and model selectors.

### Review & Diff Viewer Polish
- **Diff Viewer Actions**: Added inline toolbar actions for "Request Changes" and "Discuss" directly within `PlanDiffView`.
- **Sticky Headers & Kebab Menu**: Fixed kebab menu z-index clipping behind surrounding headers in file diff views.
- **Type Scale Alignment**: Aligned diff viewer typography and font sizes with the Ivy framework app type scale.
- **Git Tab Restoration**: Restored the Git changes tab in both Drafts and Review apps.

### Performance & Usability Improvements
- **Smooth Sidebar Resizing**: Optimized `SidebarLayout` drag-resizing with `requestAnimationFrame` DOM updates and transparent drag overlay backdrops for 60fps smooth resizing over heavy markdown/diff views.
- **Smart Plan Retry**: Updated plan retries to resume prior work rather than re-implementing plans from scratch.
- **Multi-Reviewer PR Support**: Added multi-reviewer selection support when creating GitHub Pull Requests.
