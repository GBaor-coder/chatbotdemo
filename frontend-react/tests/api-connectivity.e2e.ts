import { test, expect } from "@playwright/test";

test.describe("API Connectivity E2E", () => {
  test.use({ navigationTimeout: 30000 });

  test.beforeEach(async ({ page }) => {
    await page.goto("/", { waitUntil: "networkidle" });
  });

  test("should reach suggestions API from browser", async ({ page }) => {
    // Capture console errors
    const errors: string[] = [];
    page.on("console", (msg) => {
      if (msg.type() === "error") {
        errors.push(msg.text());
      }
    });

    let response;
    try {
      response = await page.evaluate(async () => {
        const res = await fetch("/api/suggestions");
        return { status: res.status, body: await res.text() };
      });
      console.log("Suggestions API response:", JSON.stringify(response));
      expect(response.status).toBe(200);
      expect(response.body).toContain("Điểm chuẩn");
    } catch (e) {
      console.log("Fetch error:", e);
      console.log("Console errors:", errors);
      throw e;
    }
  });
});
