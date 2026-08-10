import { test, expect } from "../../fixtures/widget-test.js";
import { navigateToApp } from "../../utils/ivy.js";

test.describe("WebViewer Inspector", () => {
  test.beforeEach(async ({ page }) => {
    await navigateToApp(page, "web-viewer/demo");
    await page.waitForSelector(".wvr-shell", { timeout: 20_000 });
  });

  test("mounts the proxied viewport iframe in view-space", async ({ page, stepScreenshot }) => {
    // The iframe only appears once the service worker is ready, so this also
    // proves SW registration succeeded on the Ivy origin.
    const frame = page.locator("iframe.wvr-frame");
    await expect(frame).toBeVisible({ timeout: 20_000 });
    await expect(frame).toHaveAttribute("src", /\/__view\//, { timeout: 20_000 });
    await stepScreenshot("iframe-mounted");
  });

  test("toolbar exposes navigation, device and capture controls", async ({ page }) => {
    await expect(page.getByRole("button", { name: "Go" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Desktop" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Mobile" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Tablet" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Screenshot" })).toBeVisible();
  });

  test("devtools tabs switch between console, network and captures", async ({ page, stepScreenshot }) => {
    await expect(page.getByRole("button", { name: /^Console \(/ })).toBeVisible();
    await page.getByRole("button", { name: /^Network \(/ }).click();
    await page.getByRole("button", { name: /^Captures \(/ }).click();
    await expect(page.getByText("No captures", { exact: false })).toBeVisible();
    await stepScreenshot("captures-tab");
  });

  test("entering a URL and pressing Enter navigates the viewport", async ({ page, stepScreenshot }) => {
    const input = page.locator('input[type="text"]').first();
    await input.click();
    await input.fill("https://example.com");
    await input.press("Enter");
    await expect(page.locator("iframe.wvr-frame")).toHaveAttribute("src", /example\.com/, {
      timeout: 15_000,
    });
    await stepScreenshot("navigated-via-enter");
  });

  test("no page errors on load", async ({ pageErrors }) => {
    expect(pageErrors).toHaveLength(0);
  });
});
