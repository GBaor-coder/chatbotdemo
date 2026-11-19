import { test, expect } from "@playwright/test";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { createHmac } from "crypto";
import { execSync } from "child_process";

const DOTNET_BASE = process.env.DOTNET_BASE_URL ?? "http://localhost:5000";
const FAKE_LLM_CALLS = process.env.FAKE_LLM_CALLS_URL ?? "http://localhost:20129/__calls";
const FAKE_LLM_RESET = process.env.FAKE_LLM_RESET_URL ?? "http://localhost:20129/__reset";
const TENANT_ID = "11111111-1111-1111-1111-111111111111";
const JWT_KEY = "dev-chatbot-signing-key-change-me-32chars";

type ChatEvent = {
  type: string;
  content?: string;
  sources?: unknown[];
  done?: boolean;
  error?: string;
  sessionId?: string;
};

// Xác minh stack đang chạy ở module load (đồng bộ) để đánh dấu bỏ qua nếu không có.
let stackAvailable = false;
try {
  execSync(`curl -sS -m 4 -o /dev/null ${DOTNET_BASE}/health`, { stdio: "ignore", timeout: 5000 });
  stackAvailable = true;
} catch {
  stackAvailable = false;
}

function e2eTest(name: string, fn: () => Promise<void>) {
  if (stackAvailable) {
    test(name, fn);
  } else {
    test.skip(true, `Bỏ qua (stack chưa chạy): ${name}`);
  }
}

function makeJwt(tenantId: string, userId: string): string {
  const b64u = (obj: Record<string, unknown>) => Buffer.from(JSON.stringify(obj)).toString("base64url");
  const header = b64u({ alg: "HS256", typ: "JWT" });
  const payload = b64u({
    tenant_id: tenantId,
    sub: userId,
    name: userId,
    "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier": userId,
    exp: Math.floor(Date.now() / 1000) + 3600,
  });
  const mac = createHmac("sha256", JWT_KEY);
  mac.update(`${header}.${payload}`);
  return `${header}.${payload}.${mac.digest("base64url")}`;
}

async function fetchJson(url: string): Promise<unknown> {
  const res = await fetch(url);
  return res.json();
}

async function resetLlmCalls() {
  await fetch(FAKE_LLM_RESET, { method: "POST" });
}

async function llmCallCount(): Promise<number> {
  const body = (await fetchJson(FAKE_LLM_CALLS)) as { calls?: number };
  return body.calls ?? 0;
}

// Thu nhận tất cả sự kiện cho đến khi gặp done / error / complete hoặc hết timeout.
async function collectStream(token: string | undefined, message: string, timeoutMs = 40000): Promise<ChatEvent[]> {
  const events: ChatEvent[] = [];
  const connection = new HubConnectionBuilder()
    .withUrl(`${DOTNET_BASE}/hubs/chat`, {
      accessTokenFactory: () => token ?? "",
    })
    .withAutomaticReconnect([])
    .configureLogging(LogLevel.Information)
    .build();

  await connection.start();

  return new Promise<ChatEvent[]>((resolve) => {
    let settled = false;
    const subscription = connection.stream<ChatEvent>("StreamChat", {
      sessionId: undefined,
      message,
    }).subscribe({
      next: (e) => {
        events.push(e);
        if (e.type === "done" || e.type === "error") {
          settled = true;
          subscription.dispose();
          resolve(events);
        }
      },
      error: () => {
        if (!settled) {
          settled = true;
          resolve(events);
        }
      },
      complete: () => {
        if (!settled) {
          settled = true;
          resolve(events);
        }
      },
    });
    setTimeout(() => {
      if (!settled) {
        settled = true;
        subscription.dispose();
        resolve(events);
      }
    }, timeoutMs);
  }).finally(async () => {
    try {
      await connection.stop();
    } catch {
      // ignore
    }
  });
}

e2eTest("stream đầy đủ: token → citations → done, có citations, gọi LLM", async () => {
  const token = makeJwt(TENANT_ID, "e2e-user");
  await resetLlmCalls();

  const events = await collectStream(
    token,
    "Điểm chuẩn ngành Công nghệ thông tin 2024 là bao nhiêu?",
  );

  const types = events.map((e) => e.type);
  expect(types).toContain("token");
  expect(types).toContain("citations");
  expect(types).toContain("done");

  const citation = events.find((e) => e.type === "citations");
  expect(citation?.sources).toBeDefined();
  expect((citation?.sources ?? []).length).toBeGreaterThan(0);

  const count = await llmCallCount();
  expect(count).toBeGreaterThan(0);
});

e2eTest("câu ngoài phạm vi → từ chối cố định, không gọi LLM", async () => {
  const token = makeJwt(TENANT_ID, "e2e-user");
  await resetLlmCalls();

  const events = await collectStream(token, "Thời tiết hôm nay thế nào?");

  const types = events.map((e) => e.type);
  expect(types).toContain("token");
  expect(types).toContain("done");

  const refusal = events.find((e) => e.type === "token");
  expect(refusal?.content).toContain("chỉ hỗ trợ giải đáp các thông tin liên quan đến tuyển sinh");

  const count = await llmCallCount();
  expect(count).toBe(0);
});

e2eTest("dừng giữa chừng: subscription.dispose(), không có done", async () => {
  const token = makeJwt(TENANT_ID, "e2e-user");
  const events: ChatEvent[] = [];
  let disposed = false;

  const connection = new HubConnectionBuilder()
    .withUrl(`${DOTNET_BASE}/hubs/chat`, {
      accessTokenFactory: () => token,
    })
    .withAutomaticReconnect([])
    .configureLogging(LogLevel.Information)
    .build();

  await connection.start();

  const subscription = connection.stream<ChatEvent>("StreamChat", {
    sessionId: undefined,
    message: "Hãy trình bày đầy đủ lịch trình tuyển sinh 2026 và tất cả phương thức xét tuyển chi tiết nhất có thể",
  }).subscribe({
    next: (e) => {
      events.push(e);
      if (e.type === "token" && !disposed) {
        disposed = true;
        subscription.dispose();
      }
    },
  });

  await new Promise((r) => setTimeout(r, 2500));

  try {
    await connection.stop();
  } catch {
    // ignore
  }

  expect(disposed).toBe(true);
  expect(events.some((e) => e.type === "token")).toBe(true);
  expect(events.every((e) => e.type !== "done")).toBe(true);
});

e2eTest("chưa xác thực: kết nối SignalR bị từ chối", async () => {
  const connection = new HubConnectionBuilder()
    .withUrl(`${DOTNET_BASE}/hubs/chat`, {
      accessTokenFactory: () => "",
    })
    .withAutomaticReconnect([])
    .configureLogging(LogLevel.Information)
    .build();

  await expect(connection.start()).rejects.toThrow();
});
