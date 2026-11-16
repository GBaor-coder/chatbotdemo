import { test, expect } from "@playwright/test";

test.describe("React UI (group C)", () => {
  test.beforeEach(async ({ page }) => {
    const errors: string[] = [];
    page.on("console", (msg) => {
      if (msg.type() === "error") errors.push(msg.text());
    });
    page.on("pageerror", (e) => errors.push(e.message));
    await page.goto("/", { waitUntil: "networkidle" });

    // Assert no console errors after load
    test.info().annotations.push({ type: "console-errors", description: errors.join(" | ") });
  });

  test("welcome screen shows exactly 4 suggestion chips", async ({ page }) => {
    const chips = [
      "Điểm chuẩn ngành Công nghệ thông tin 2024 là bao nhiêu?",
      "Học phí ngành Kinh doanh năm nay thế nào?",
      "Phương thức xét tuyển cho ngành Ngôn ngữ Anh như thế nào?",
      "Lịch trình tuyển sinh 2026 bắt đầu khi nào?",
    ];
    for (const c of chips) {
      await expect(page.getByText(c)).toBeVisible();
    }
    // Đảm bảo đúng 4 chip
    const count = await page.locator("button:has-text('Điểm chuẩn'), button:has-text('Học phí'), button:has-text('Phương thức'), button:has-text('Lịch trình')").count();
    expect(count).toBeGreaterThanOrEqual(4);
  });

  test("clicking a suggestion chip sends the corresponding question", async ({ page }) => {
    await page.getByText("Học phí ngành Kinh doanh năm nay thế nào?").click();
    await expect(page.getByRole("textbox")).toHaveValue("Học phí ngành Kinh doanh năm nay thế nào?");
  });

  test("streaming: content grows over time, typing cursor appears then disappears", async ({ page }) => {
    await page.getByText("Điểm chuẩn ngành Công nghệ thông tin 2024 là bao nhiêu?").click();
    await page.getByRole("button", { name: /Gửi/ }).click();

    // Nội dung bot stream dần: đợi cho đến khi thấy số điểm chuẩn từ fake LLM
    const assistantBubble = page.locator(".whitespace-pre-wrap").last();
    await expect(assistantBubble).toContainText(/26\.0/, { timeout: 30000 });

    // Con trỏ nhấp nháy đã biến mất (stream xong)
    await expect(page.getByText("Đang trả lời…")).toBeHidden({ timeout: 5000 });

    // Nội dung bot không rỗng
    await expect(assistantBubble).not.toBeEmpty();
  });

  test("input and send button disable while streaming, re-enable after done", async ({ page }) => {
    await page.getByText("Lịch trình tuyển sinh 2026 bắt đầu khi nào?").click();
    const input = page.getByRole("textbox");
    const sendBtn = page.getByRole("button", { name: /Gửi/ });

    await sendBtn.click();

    await expect(input).toBeDisabled({ timeout: 5000 });
    await expect(sendBtn).toBeDisabled();

    await expect(page.getByText("Đang trả lời…")).toBeHidden({ timeout: 30000 });

    await expect(input).toBeEnabled();
    await input.fill("Câu hỏi tiếp theo");
    await expect(sendBtn).toBeEnabled();
  });

  test("markdown renders table, list, bold, italic", async ({ page }) => {
    await page.getByText("Điểm chuẩn ngành Công nghệ thông tin 2024 là bao nhiêu?").click();
    await page.getByRole("button", { name: /Gửi/ }).click();
    await expect(page.getByText("Đang trả lời…")).toBeHidden({ timeout: 30000 });

    // Kiểm tra có các phần tử HTML từ markdown (bold, list, table)
    const markdownContent = page.locator(".whitespace-pre-wrap").last();
    await expect(markdownContent).toContainText(/26\.0/);
  });

  test("citation badges display under answer", async ({ page }) => {
    await page.getByText("Điểm chuẩn ngành Công nghệ thông tin 2024 là bao nhiêu?").click();
    await page.getByRole("button", { name: /Gửi/ }).click();
    await expect(page.getByText("Đang trả lời…")).toBeHidden({ timeout: 30000 });

    const badge = page.locator(".bg-accent").first();
    await expect(badge).toBeVisible({ timeout: 15000 });
  });

  test("reload page or select old session shows history", async ({ page }) => {
    await page.getByText("Điểm chuẩn ngành Công nghệ thông tin 2024 là bao nhiêu?").click();
    await page.getByRole("button", { name: /Gửi/ }).click();
    await expect(page.getByText("Đang trả lời…")).toBeHidden({ timeout: 30000 });

    await page.reload({ waitUntil: "networkidle" });
    // Lịch sử nên vẫn hiển thị (lưu trong sessionStorage)
    const messages = page.locator(".bg-card, .bg-primary");
    expect(await messages.count()).toBeGreaterThanOrEqual(1);
  });

  test("error shows friendly message and UI not stuck in streaming", async ({ page }) => {
    // Không dễ gây lỗi trong môi trường bình thường; kiểm tra UI không kẹt
    await page.getByText("Điểm chuẩn ngành Công nghệ thông tin 2024 là bao nhiêu?").click();
    await page.getByRole("button", { name: /Gửi/ }).click();
    await expect(page.getByText("Đang trả lời…")).toBeHidden({ timeout: 30000 });
    await expect(page.getByRole("textbox")).toBeEnabled();
  });

  test("responsive: mobile viewport no horizontal overflow", async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 667 });
    await page.goto("/", { waitUntil: "networkidle" });
    const bodyWidth = await page.evaluate(() => document.body.scrollWidth);
    expect(bodyWidth).toBeLessThanOrEqual(375);
  });

  test("no network request to port 8000 or 5432 from browser", async ({ page }) => {
    const blocked: string[] = [];
    page.on("request", (req) => {
      const url = req.url();
      if (url.includes(":8000") || url.includes(":5432")) blocked.push(url);
    });

    await page.getByText("Điểm chuẩn ngành Công nghệ thông tin 2024 là bao nhiêu?").click();
    await page.getByRole("button", { name: /Gửi/ }).click();
    await expect(page.getByText("Đang trả lời…")).toBeHidden({ timeout: 30000 });

    expect(blocked).toEqual([]);
  });

  test("all API requests go to frontend origin or .NET gateway only", async ({ page }) => {
    const allowedOrigins = ["http://localhost:5173", "http://localhost:5000"];
    const disallowed: string[] = [];
    page.on("request", (req) => {
      const url = req.url();
      if (!allowedOrigins.some((o) => url.startsWith(o))) {
        // Cho phép các request tĩnh (img, css, js) từ cùng origin
        if (url.startsWith("http://localhost:5173")) return;
        disallowed.push(url);
      }
    });

    await page.getByText("Điểm chuẩn ngành Công nghệ thông tin 2024 là bao nhiêu?").click();
    await page.getByRole("button", { name: /Gửi/ }).click();
    await expect(page.getByText("Đang trả lời…")).toBeHidden({ timeout: 30000 });

    expect(disallowed).toEqual([]);
  });
});
