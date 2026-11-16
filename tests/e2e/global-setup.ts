import { chromium } from "@playwright/test";
import { expect } from "@playwright/test";

const PYTHON_READY = process.env.PYTHON_READY_URL ?? "http://localhost:8000/api/v1/ready";
const DOTNET_HEALTH = process.env.DOTNET_HEALTH_URL ?? "http://localhost:5000/health";
const SEARCH_URL = process.env.SEARCH_URL ?? "http://localhost:8000/api/v1/retrieval/search";
const INTERNAL_API_KEY = process.env.X_INTERNAL_API_KEY ?? "dev-internal-key-change-me";
const FAKE_LLM_RESET = process.env.FAKE_LLM_RESET_URL ?? "http://localhost:20129/__reset";

async function waitFor(url: string, label: string, maxAttempts = 30) {
  for (let i = 0; i < maxAttempts; i++) {
    try {
      const res = await fetch(url);
      if (res.ok) {
        console.log(`[global-setup] ${label} OK: ${url}`);
        return;
      }
    } catch {
      // continue retrying
    }
    console.log(`[global-setup] Waiting for ${label}... (attempt ${i + 1})`);
    await new Promise((r) => setTimeout(r, 2000));
  }
  throw new Error(`[global-setup] ${label} not ready after ${maxAttempts} attempts: ${url}`);
}

async function seedViaIngestion() {
  console.log("[global-setup] Seeding via ingestion pipeline...");
  const payload = {
    tenant_id: "11111111-1111-1111-1111-111111111111",
    source: "seed",
    title: "Test seed document",
    doc_type: "text",
    status: "published",
    content:
      "Điểm chuẩn ngành Công nghệ thông tin 2024 là 26.0. Học phí ngành Kinh doanh 32 triệu VND/năm.",
    metadata: { category: "test_seed", year: 2024 },
  };

  const res = await fetch("http://localhost:8000/ingestion/documents", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "X-Internal-Api-Key": INTERNAL_API_KEY,
      "X-Request-Id": "global-setup-seed",
    },
    body: JSON.stringify(payload),
  });
  if (!res.ok) {
    const text = await res.text();
    throw new Error(`[global-setup] Seed ingestion failed: ${res.status} ${text}`);
  }
  const job = await res.json();
  console.log(`[global-setup] Ingestion job created: ${job.job_id}`);

  // Poll job until completed
  for (let i = 0; i < 30; i++) {
    await new Promise((r) => setTimeout(r, 1000));
    const jobRes = await fetch(
      `http://localhost:8000/ingestion/jobs/${job.job_id}`,
      { headers: { "X-Internal-Api-Key": INTERNAL_API_KEY } }
    );
    const jobStatus = await jobRes.json();
    if (jobStatus.status === "completed") {
      console.log("[global-setup] Seed ingestion completed.");
      return;
    }
    if (jobStatus.status === "failed") {
      throw new Error(`[global-setup] Seed ingestion failed: ${jobStatus.error_message}`);
    }
  }
  throw new Error("[global-setup] Seed ingestion timed out.");
}

export default async function globalSetup() {
  await waitFor(PYTHON_READY, "Python /api/v1/ready");
  await waitFor(DOTNET_HEALTH, ".NET /health");

  // Reset fake-llm call counter để test guardrail deterministik
  try {
    await fetch(FAKE_LLM_RESET, { method: "POST" });
    console.log("[global-setup] Fake-LLM call counter reset.");
  } catch {
    console.log("[global-setup] Warning: could not reset fake-llm counter.");
  }

  await seedViaIngestion();
}
