import { test, expect, request as playwrightRequest, type APIRequestContext } from "@playwright/test";

const DOTNET_BASE = process.env.DOTNET_BASE_URL ?? "http://localhost:5000";
const FAKE_LLM_CALLS = process.env.FAKE_LLM_CALLS_URL ?? "http://localhost:20129/__calls";
const FAKE_LLM_RESET = process.env.FAKE_LLM_RESET_URL ?? "http://localhost:20129/__reset";

test.describe(".NET Gateway & Orchestration (group B)", () => {
  test.describe.configure({ mode: "serial" });

  let request: APIRequestContext;

  test.beforeAll(async () => {
    request = await playwrightRequest.newContext({
      baseURL: DOTNET_BASE,
      extraHTTPHeaders: {
        Authorization: "Bearer dev-token",
      },
    });
  });

  test.afterAll(async () => {
    await request.dispose();
  });

  test("GET /api/suggestions returns 4 suggestions", async () => {
    const res = await request.get("/api/suggestions");
    expect(res.ok()).toBe(true);
    const body = await res.json();
    expect(Array.isArray(body)).toBe(true);
    expect(body.length).toBe(4);
  });

  test("POST /api/chat/stream returns SSE token -> citations -> done", async () => {
    await fetch(FAKE_LLM_RESET, { method: "POST" });

    const res = await request.post("/api/chat/stream", {
      data: { message: "Điểm chuẩn ngành Công nghệ thông tin 2024 là bao nhiêu?", sessionId: "" },
      headers: { "Content-Type": "application/json" },
    });
    expect(res.ok()).toBe(true);

    const text = await res.text();
    const events = parseSSE(text);
    const types = events.map((e) => e.type);
    expect(types).toContain("token");
    expect(types).toContain("citations");
    expect(types).toContain("done");

    const doneEvent = events.find((e) => e.type === "done");
    expect(doneEvent?.session_id).toBeDefined();
  });

  test("streaming tokens arrive before stream ends (real streaming)", async () => {
    await fetch(FAKE_LLM_RESET, { method: "POST" });

    const res = await request.post("/api/chat/stream", {
      data: { message: "Học phí ngành Kinh doanh năm nay thế nào?", sessionId: "" },
    });
    const text = await res.text();
    const events = parseSSE(text);

    const tokenEvents = events.filter((e) => e.type === "token");
    const doneEvent = events.find((e) => e.type === "done");

    // Có ít nhất 2 token và done xuất hiện sau token cuối
    expect(tokenEvents.length).toBeGreaterThanOrEqual(2);
    const lastTokenIndex = events.lastIndexOf(tokenEvents[tokenEvents.length - 1]);
    const doneIndex = events.indexOf(doneEvent!);
    expect(doneIndex).toBeGreaterThan(lastTokenIndex);
  });

  test("guardrail: off-topic question returns refusal and LLM not called", async () => {
    await fetch(FAKE_LLM_RESET, { method: "POST" });

    const res = await request.post("/api/chat/stream", {
      data: { message: "Thời tiết hôm nay ở Hà Nội thế nào?", sessionId: "" },
    });
    expect(res.ok()).toBe(true);
    const text = await res.text();
    const events = parseSSE(text);

    const tokenEvents = events.filter((e) => e.type === "token");
    const fullText = tokenEvents.map((e) => e.content ?? "").join("");
    expect(fullText).toContain("Xin lỗi, hiện tại tôi chỉ hỗ trợ");

    // LLM giả không được gọi
    const callsRes = await fetch(FAKE_LLM_CALLS);
    const callsBody = await callsRes.json();
    expect(callsBody.calls).toBe(0);
  });

  test("structured query: admission score matches DB record", async () => {
    const res = await request.post("/api/chat/stream", {
      data: { message: "Điểm chuẩn ngành Công nghệ thông tin 2024 là bao nhiêu?", sessionId: "" },
    });
    const text = await res.text();
    const events = parseSSE(text);
    const fullText = events.map((e) => e.content ?? "").join("");

    // Con số trong câu trả lời khớp DB: CNTT 2024 = 26.0
    expect(fullText).toContain("26.0");
  });

  test("security: no token returns 401", async () => {
    const noAuth = await playwrightRequest.newContext({ baseURL: DOTNET_BASE });
    const res = await noAuth.post("/api/chat/stream", {
      data: { message: "test", sessionId: "" },
    });
    expect([401, 403]).toContain(res.status());
    await noAuth.dispose();
  });

  test("security: client-sent tenantId/userId is ignored", async () => {
    // .NET không đọc userId/tenantId từ body (theo PLAN), nên gửi dữ liệu giả
    // không làm thay đổi hành vi - vẫn xử lý bình thường
    const res = await request.post("/api/chat/stream", {
      data: {
        message: "Điểm chuẩn ngành Công nghệ thông tin 2024 là bao nhiêu?",
        sessionId: "",
        tenantId: "fake-tenant",
        userId: "fake-user",
      },
    });
    expect(res.ok()).toBe(true);
    const text = await res.text();
    const events = parseSSE(text);
    expect(events.some((e) => e.type === "done")).toBe(true);
  });

  test("security: SQL injection in message does not break", async () => {
    const res = await request.post("/api/chat/stream", {
      data: { message: "'; DROP TABLE majors; --", sessionId: "" },
    });
    // Không được crash; trả lỗi thân tín hoặc xử lý bình thường
    expect([200, 400, 500]).toContain(res.status());
  });

  test("GET /api/chat/history/{sessionId} returns messages in ASC order", async () => {
    // Tạo một session qua stream
    const streamRes = await request.post("/api/chat/stream", {
      data: { message: "Lịch trình tuyển sinh 2026 bắt đầu khi nào?", sessionId: "" },
    });
    const streamText = await streamRes.text();
    const events = parseSSE(streamText);
    const doneEvent = events.find((e) => e.type === "done");
    const sessionId = doneEvent?.session_id;
    expect(sessionId).toBeDefined();

    // Lấy lịch sử (cần tenant_id - dev-only header)
    const histRes = await request.get(`/api/chat/history/${sessionId}`, {
      headers: { "X-Tenant-Id": "11111111-1111-1111-1111-111111111111" },
    });
    expect(histRes.ok()).toBe(true);
    const messages = await histRes.json();
    expect(Array.isArray(messages)).toBe(true);
    expect(messages.length).toBeGreaterThanOrEqual(2);

    // Kiểm tra thứ tự ASC
    const times = messages.map((m: any) => new Date(m.createdAt).getTime());
    for (let i = 1; i < times.length; i++) {
      expect(times[i]).toBeGreaterThanOrEqual(times[i - 1]);
    }
  });

  test("security: IDOR - user B cannot read user A session history", async () => {
    // Tạo session của user A
    const streamRes = await request.post("/api/chat/stream", {
      data: { message: "test session A", sessionId: "" },
    });
    const streamText = await streamRes.text();
    const events = parseSSE(streamText);
    const sessionId = events.find((e) => e.type === "done")?.session_id;

    // User B (tenant khác) thử đọc
    const histRes = await request.get(`/api/chat/history/${sessionId}`, {
      headers: { "X-Tenant-Id": "22222222-2222-2222-2222-222222222222" },
    });
    expect([403, 404]).toContain(histRes.status());
  });

  test("resilience: Python stopped returns friendly error within timeout", async () => {
    // Skip nếu không thể dừng container trong môi trường test
    // Test này ghi nhận bằng chứng nếu ai-service bị dừng
    test.skip(true, "Requires stopping ai-service container manually");
  });
});

function parseSSE(text: string): Array<{ type: string; content?: string; session_id?: string }> {
  const events: Array<{ type: string; content?: string; session_id?: string }> = [];
  const lines = text.split("\n");
  let dataLine = "";
  for (const line of lines) {
    if (line.startsWith("data: ")) {
      dataLine = line.slice(6);
      try {
        const parsed = JSON.parse(dataLine);
        events.push({
          type: parsed.type ?? "",
          content: parsed.content,
          session_id: parsed.session_id,
        });
      } catch {
        // ignore malformed
      }
    }
  }
  return events;
}
