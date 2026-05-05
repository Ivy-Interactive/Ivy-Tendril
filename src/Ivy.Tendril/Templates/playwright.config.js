// @ts-check
const { defineConfig, devices } = require('@playwright/test');
const path = require('path');

/**
 * Playwright configuration template for Tendril visual verification.
 *
 * This configuration is designed to capture screenshots of frontend changes
 * and store them in the plan's artifacts folder for review.
 *
 * Usage:
 * 1. Copy this file to your project's test directory
 * 2. Set the PLAN_FOLDER environment variable to your plan's folder path
 * 3. Run: npx playwright test
 *
 * Screenshots will be saved to: <PLAN_FOLDER>/artifacts/screenshots/
 */

// Get plan folder from environment variable (set by VisualCheck verification)
const planFolder = process.env.PLAN_FOLDER || process.cwd();
const screenshotDir = path.join(planFolder, 'artifacts', 'screenshots');

module.exports = defineConfig({
  testDir: './tests',

  // Maximum time one test can run
  timeout: 30 * 1000,

  // Run tests in files in parallel
  fullyParallel: true,

  // Fail the build on CI if you accidentally left test.only in the source code
  forbidOnly: !!process.env.CI,

  // Retry on CI only
  retries: process.env.CI ? 2 : 0,

  // Reporter configuration
  reporter: [
    ['list'],
    ['html', { outputFolder: path.join(screenshotDir, 'report-html') }]
  ],

  // Shared settings for all projects
  use: {
    // Base URL for navigation
    // baseURL: 'http://localhost:3000',

    // Collect trace on failure
    trace: 'on-first-retry',

    // Screenshot on failure
    screenshot: 'only-on-failure',

    // Video on failure
    video: 'retain-on-failure',
  },

  // Configure projects for major browsers
  projects: [
    {
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome'],
        // Custom screenshot path for this browser
        screenshot: {
          mode: 'on',
          fullPage: true,
        },
      },
    },

    // Uncomment to test on Firefox
    // {
    //   name: 'firefox',
    //   use: { ...devices['Desktop Firefox'] },
    // },

    // Uncomment to test on Safari
    // {
    //   name: 'webkit',
    //   use: { ...devices['Desktop Safari'] },
    // },

    // Uncomment to test on mobile viewports
    // {
    //   name: 'Mobile Chrome',
    //   use: { ...devices['Pixel 5'] },
    // },
    // {
    //   name: 'Mobile Safari',
    //   use: { ...devices['iPhone 12'] },
    // },
  ],

  // Configure output folders
  outputDir: path.join(screenshotDir, 'test-results'),
});
