import { test, expect, request as playwrightRequest, type APIRequestContext } from "@playwright/test";

const PYTHON_BASE = process.env.PYTHON_BASE_URL ?? "http://localhost:8000";
const INTERNAL_API_KEY = process.env.X_INTERNAL_API_KEY ?? "dev-internal-key-change-me";
const TENANT_A = "11111111-1111-1111-1111-111111111111";
const TENANT_B = "22222222-2222-2222-2222-222222222222";

test.describe("Python RAG Data Service (group A)", () => {
  let request: APIRequestContext;

  test.beforeAll(async () => {
    request = await playwrightRequest.newContext({
      baseURL: PYTHON_BASE,
      extraHTTPHeaders: {
        "X-Internal-Api-Key": INTERNAL_API_KEY,
        "X-Request-Id": "test-api-setup",
      },
    });
  });

  test.afterAll(async () => {
    await request.dispose();
  });

  test("GET /api/v1/health returns ok", async () => {
    const res = await request.get("/api/v1/health");
    expect(res.ok()).toBe(true);
    const body = await res.json();
    expect(body.status).toBe("ok");
  });

  test("GET /api/v1/ready returns ready when model loaded", async () => {
    const res = await request.get("/api/v1/ready");
    expect(res.ok()).toBe(true);
    const body = await res.json();
    expect(body.status).toBe("ready");
    expect(body.dim).toBe(1024);
  });

  test("POST /api/v1/retrieval/search valid returns correct structure", async () => {
    const res = await request.post("/api/v1/retrieval/search", {
      data: {
        query: "Điểm chuẩn ngành Công nghệ thông tin 2024",
        top_k: 5,
        min_score: 0.0,
        filters: { tenant_id: TENANT_A },
      },
    });
    expect(res.ok()).toBe(true);
    const body = await res.json();
    expect(body.success).toBe(true);
    expect(Array.isArray(body.results)).toBe(true);
    expect(body.meta.embedding_model).toBe("BAAI/bge-m3");
    expect(body.meta.dim).toBe(1024);
    expect(typeof body.meta.query_ms).toBe("number");
    expect(body.meta.returned).toBeLessThanOrEqual(5);

    for (const r of body.results) {
      expect(r).toHaveProperty("chunk_id");
      expect(r).toHaveProperty("document_id");
      expect(r).toHaveProperty("content");
      expect(r).toHaveProperty("score");
      expect(r).toHaveProperty("metadata");
      expect(r.score).toBeGreaterThanOrEqual(0);
      expect(r.score).toBeLessThanOrEqual(1);
    }
  });

  test("results sorted descending by score and len <= top_k", async () => {
    const res = await request.post("/api/v1/retrieval/search", {
      data: {
        query: "học phí",
        top_k: 3,
        min_score: 0.0,
        filters: { tenant_id: TENANT_A },
      },
    });
    const body = await res.json();
    const scores = body.results.map((r: any) => r.score);
    for (let i = 1; i < scores.length; i++) {
      expect(scores[i]).toBeLessThanOrEqual(scores[i - 1]);
    }
    expect(body.results.length).toBeLessThanOrEqual(3);
  });

  test("missing tenant_id returns 422", async () => {
    const res = await request.post("/api/v1/retrieval/search", {
      data: {
        query: "test",
        top_k: 5,
        min_score: 0.0,
        filters: { doc_type: ["text"] },
      },
    });
    expect(res.status()).toBe(422);
    const body = await res.json();
    expect(body.success).toBe(false);
    expect(body.error.code).toBe("validation_error");
    expect(body.request_id).toBeDefined();
  });

  test("filter outside whitelist returns 422", async () => {
    const res = await request.post("/api/v1/retrieval/search", {
      data: {
        query: "test",
        top_k: 5,
        min_score: 0.0,
        filters: { tenant_id: TENANT_A, invalid_filter: "x" },
      },
    });
    expect(res.status()).toBe(422);
    const body = await res.json();
    expect(body.success).toBe(false);
  });

  test("top_k above max is clamped to 20", async () => {
    const res = await request.post("/api/v1/retrieval/search", {
      data: {
        query: "test",
        top_k: 100,
        min_score: 0.0,
        filters: { tenant_id: TENANT_A },
      },
    });
    const body = await res.json();
    expect(body.meta.returned).toBeLessThanOrEqual(20);
  });

  test("no results returns success:true, results:[]", async () => {
    const res = await request.post("/api/v1/retrieval/search", {
      data: {
        query: "zzzzznothingmatches",
        top_k: 5,
        min_score: 0.99,
        filters: { tenant_id: TENANT_A },
      },
    });
    expect(res.ok()).toBe(true);
    const body = await res.json();
    expect(body.success).toBe(true);
    expect(body.results).toEqual([]);
  });

  test("missing X-Internal-Api-Key returns 401/403", async () => {
    const noKey = await playwrightRequest.newContext({ baseURL: PYTHON_BASE });
    const res = await noKey.post("/api/v1/retrieval/search", {
      data: {
        query: "test",
        top_k: 5,
        min_score: 0.0,
        filters: { tenant_id: TENANT_A },
      },
    });
    expect([401, 403]).toContain(res.status());
    await noKey.dispose();
  });

  test("tenant isolation: tenant A never returns tenant B chunks", async () => {
    // Ingest a document for tenant B first
    await request.post("/ingestion/documents", {
      data: {
        tenant_id: TENANT_B,
        source: "tenant-b-secret",
        title: "Tenant B secret doc",
        doc_type: "text",
        status: "published",
        content: "Tenant B chỉ có dữ liệu riêng của trường B, không chia sẻ với A.",
        metadata: { category: "tenant_b_only" },
      },
    });
    // Wait for ingestion to complete (poll)
    let job = await (await request.post("/ingestion/documents", {
      data: {
        tenant_id: TENANT_B,
        source: "tenant-b-secret-2",
        title: "Tenant B secret doc 2",
        doc_type: "text",
        status: "published",
        content: "Tenant B chỉ có dữ liệu riêng của trường B, không chia sẻ với A.",
        metadata: { category: "tenant_b_only" },
      },
    })).json();

    for (let i = 0; i < 20; i++) {
      await new Promise((r) => setTimeout(r, 500));
      const status = await request.get(`/ingestion/jobs/${job.job_id}`, {
        headers: { "X-Internal-Api-Key": INTERNAL_API_KEY },
      });
      const sj = await status.json();
      if (sj.status === "completed") break;
    }

    // Search tenant A - must not contain tenant B content
    const resA = await request.post("/api/v1/retrieval/search", {
      data: {
        query: "tenant B secret",
        top_k: 10,
        min_score: 0.0,
        filters: { tenant_id: TENANT_A },
      },
    });
    const bodyA = await resA.json();
    for (const r of bodyA.results) {
      expect(r.metadata.title).not.toContain("Tenant B");
    }

    // Search tenant B - must contain tenant B content
    const resB = await request.post("/api/v1/retrieval/search", {
      data: {
        query: "tenant B secret",
        top_k: 10,
        min_score: 0.0,
        filters: { tenant_id: TENANT_B },
      },
    });
    const bodyB = await resB.json();
    expect(bodyB.results.length).toBeGreaterThan(0);
  });

  test("metadata filter (doc_type) returns correct subset", async () => {
    const res = await request.post("/api/v1/retrieval/search", {
      data: {
        query: "điểm chuẩn",
        top_k: 10,
        min_score: 0.0,
        filters: { tenant_id: TENANT_A, doc_type: ["text"] },
      },
    });
    const body = await res.json();
    for (const r of body.results) {
      expect(r.metadata.doc_type).toBe("text");
    }
  });

  test("error response never leaks stack trace", async () => {
    const res = await request.post("/api/v1/retrieval/search", {
      data: {
        query: "test",
        top_k: 5,
        min_score: 0.0,
        filters: { tenant_id: TENANT_A, invalid_filter: "x" },
      },
    });
    const body = await res.json();
    expect(JSON.stringify(body)).not.toContain("stack");
    expect(JSON.stringify(body)).not.toContain("Traceback");
  });
});
