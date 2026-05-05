# Playwright Visual Verification Templates

This directory contains templates for setting up Playwright visual verification in your projects.

## Files

- **playwright.config.js** - Playwright configuration template that automatically saves screenshots to the plan's artifacts folder
- **example.visual.spec.js** - Example test demonstrating screenshot capture patterns

## Usage

### 1. Add VisualCheck Verification to Your Project

In your `config.yaml`, add the `VisualCheck` verification to your project:

```yaml
projects:
  - name: MyProject
    verifications:
      - name: VisualCheck
        required: false  # Optional by default
```

### 2. Setup Playwright in Your Project

If you don't have Playwright installed:

```bash
npm init playwright@latest
```

Or add to an existing project:

```bash
npm install -D @playwright/test
npx playwright install
```

### 3. Copy Templates

Copy the configuration and example test to your project:

```bash
# Copy config to your project root
cp Templates/playwright.config.js <your-project>/playwright.config.js

# Copy example test to your tests directory
cp Templates/example.visual.spec.js <your-project>/tests/
```

### 4. Create Your Visual Tests

Edit the example test to match your components:

```javascript
test('my component', async ({ page }) => {
  await page.goto('http://localhost:3000/my-page');
  
  const timestamp = new Date().toISOString().replace(/[:.]/g, '-');
  
  // Capture screenshot
  await page.screenshot({
    path: path.join(screenshotDir, `my-component-${timestamp}.png`),
    fullPage: true,
  });
});
```

### 5. Run Visual Verification

During plan execution, the `VisualCheck` verification will:

1. Set the `PLAN_FOLDER` environment variable
2. Run your Playwright tests
3. Collect screenshots in `<PlanFolder>/artifacts/screenshots/`
4. Generate a visual comparison report

## Screenshot Naming Conventions

Use descriptive filenames that include:
- Component/page name
- State (before/after, default/hover/active)
- Timestamp for uniqueness

Examples:
- `login-page-before-2026-05-05.png`
- `button-hover-state-2026-05-05.png`
- `dashboard-after-filter-applied-2026-05-05.png`

## Tips

- Use `fullPage: true` to capture the entire page scroll
- Capture screenshots at key interaction points (before/after actions)
- Test multiple viewport sizes if responsive design is important
- Use the generated `report.md` to document what each screenshot shows
