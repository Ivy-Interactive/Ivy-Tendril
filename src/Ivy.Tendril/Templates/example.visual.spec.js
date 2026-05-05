// @ts-check
const { test, expect } = require('@playwright/test');
const path = require('path');
const fs = require('fs');

/**
 * Example Playwright test demonstrating screenshot capture for visual verification.
 *
 * This test shows how to:
 * 1. Navigate to a page
 * 2. Capture "before" and "after" screenshots
 * 3. Save screenshots to the plan's artifacts folder
 * 4. Generate a markdown report
 */

// Get plan folder and screenshot directory from environment
const planFolder = process.env.PLAN_FOLDER || process.cwd();
const screenshotDir = path.join(planFolder, 'artifacts', 'screenshots');

// Ensure screenshot directory exists
if (!fs.existsSync(screenshotDir)) {
  fs.mkdirSync(screenshotDir, { recursive: true });
}

test.describe('Visual Regression Example', () => {
  test('capture component screenshots', async ({ page }) => {
    // Navigate to the page under test
    // await page.goto('http://localhost:3000');

    // Example: Capture a "before" screenshot
    const timestamp = new Date().toISOString().replace(/[:.]/g, '-');
    const componentName = 'example-component';

    await page.screenshot({
      path: path.join(screenshotDir, `${componentName}-${timestamp}.png`),
      fullPage: true,
    });

    // Example: Interact with the page
    // await page.click('button#submit');

    // Example: Capture an "after" screenshot
    // await page.screenshot({
    //   path: path.join(screenshotDir, `${componentName}-after-${timestamp}.png`),
    //   fullPage: true,
    // });

    // Verify the page is loaded (example assertion)
    // await expect(page.locator('h1')).toBeVisible();
  });

  test('capture multiple component states', async ({ page }) => {
    // Example: Test multiple states of a component
    const componentName = 'multi-state-component';
    const timestamp = new Date().toISOString().replace(/[:.]/g, '-');

    // State 1: Default
    // await page.goto('http://localhost:3000/component');
    // await page.screenshot({
    //   path: path.join(screenshotDir, `${componentName}-default-${timestamp}.png`),
    // });

    // State 2: Hover
    // await page.hover('button.primary');
    // await page.screenshot({
    //   path: path.join(screenshotDir, `${componentName}-hover-${timestamp}.png`),
    // });

    // State 3: Active/Clicked
    // await page.click('button.primary');
    // await page.screenshot({
    //   path: path.join(screenshotDir, `${componentName}-active-${timestamp}.png`),
    // });
  });
});

// After all tests, generate a report
test.afterAll(async () => {
  // List all screenshots in the directory
  const screenshots = fs.readdirSync(screenshotDir)
    .filter(file => file.endsWith('.png'))
    .sort();

  // Generate markdown report
  const reportPath = path.join(screenshotDir, 'report.md');
  let reportContent = '# Visual Verification Report\n\n';
  reportContent += `Generated: ${new Date().toISOString()}\n\n`;
  reportContent += `## Screenshots Captured\n\n`;

  if (screenshots.length === 0) {
    reportContent += 'No screenshots captured.\n';
  } else {
    screenshots.forEach(screenshot => {
      reportContent += `### ${screenshot}\n\n`;
      reportContent += `![${screenshot}](${screenshot})\n\n`;
    });
  }

  fs.writeFileSync(reportPath, reportContent);
  console.log(`Visual verification report generated at: ${reportPath}`);
});
