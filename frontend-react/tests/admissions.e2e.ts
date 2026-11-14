import { test, expect } from "@playwright/test";

test.describe("Admissions Chatbot E2E", () => {
  test.use({ navigationTimeout: 30000 });

  test.beforeEach(async ({ page }) => {
    await page.goto("/", { waitUntil: "networkidle" });
  });

  test("should load the chat interface", async ({ page }) => {
    await expect(page).toHaveTitle(/Tư vấn tuyển sinh/);
    await expect(page.locator("h1")).toBeVisible();
  });

  test("should show 4 suggestion chips on welcome screen", async ({ page }) => {
    await expect(
      page.locator("text=Điểm chuẩn ngành Công nghệ thông tin 2024 là bao nhiêu?")
    ).toBeVisible();
    await expect(
      page.locator("text=Học phí ngành Kinh doanh năm nay thế nào?")
    ).toBeVisible();
    await expect(
      page.locator("text=Phương thức xét tuyển cho ngành Ngôn ngữ Anh như thế nào?")
    ).toBeVisible();
    await expect(
      page.locator("text=Lịch trình tuyển sinh 2026 bắt đầu khi nào?")
    ).toBeVisible();
  });

  test("should reach suggestions API via browser proxy", async ({ page }) => {
    const response = await page.evaluate(async () => {
      const res = await fetch("/api/suggestions");
      return { status: res.status, body: await res.text() };
    });
    expect(response.status).toBe(200);
    expect(response.body).toContain("Điểm chuẩn");
  });

  test("should check backend health", async ({ page }) => {
    await page.goto("http://localhost:5000/health", { waitUntil: "networkidle" });
    await expect(page.locator("body")).toContainText("ok");
  });

  test("should check AI service health", async ({ page }) => {
    await page.goto("http://localhost:8000/health", { waitUntil: "networkidle" });
    await expect(page.locator("body")).toContainText("ok");
  });
});
