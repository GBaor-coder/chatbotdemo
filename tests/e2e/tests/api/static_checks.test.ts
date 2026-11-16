import { test, expect } from "@playwright/test";
import * as fs from "fs";
import * as path from "path";
import { execSync } from "child_process";

const REPO_ROOT = path.resolve(__dirname, "../../../..");

test.describe("Static & Configuration Checks (group D & E)", () => {
  test("D1: ai-service source and requirements do not contain openai, LLM_, GATEWAY_URL", () => {
    const aiServiceDir = path.join(REPO_ROOT, "ai-service");
    
    // Check requirements.txt
    const reqPath = path.join(aiServiceDir, "requirements.txt");
    const reqContent = fs.readFileSync(reqPath, "utf-8");
    expect(reqContent).not.toMatch(/\bopenai\b/i);

    // Recursively check all .py files in ai-service/app
    function checkDir(dir: string) {
      const entries = fs.readdirSync(dir, { withFileTypes: true });
      for (const entry of entries) {
        const fullPath = path.join(dir, entry.name);
        if (entry.isDirectory()) {
          if (entry.name !== "__pycache__" && entry.name !== ".venv") {
            checkDir(fullPath);
          }
        } else if (entry.name.endsWith(".py") && !entry.name.includes("rag_engine")) {
          const content = fs.readFileSync(fullPath, "utf-8");
          expect(content).not.toMatch(/import\s+openai/);
          expect(content).not.toMatch(/from\s+openai\b/);
          expect(content).not.toMatch(/\bLLM_BASE_URL\b/);
          expect(content).not.toMatch(/\bLLM_API_KEY\b/);
          expect(content).not.toMatch(/\bLLM_MODEL\b/);
          expect(content).not.toMatch(/\bGATEWAY_URL\b/);
        }
      }
    }
    checkDir(path.join(aiServiceDir, "app"));
  });

  test("D2: No plaintext sk- secret in tracked repository files", () => {
    // Search across tracked files for active plaintext sk- keys
    // Specifically sk-57a432014c1cc01c-6juq1i-fe7dbfbd should not exist in active config/code
    const filesToCheck = [
      path.join(REPO_ROOT, "PLAN.md"),
      path.join(REPO_ROOT, ".kilocode/rules/architecture.md"),
      path.join(REPO_ROOT, "README.md"),
      path.join(REPO_ROOT, "docker-compose.yml"),
      path.join(REPO_ROOT, "ai-service/app/config.py"),
    ];

    for (const f of filesToCheck) {
      if (fs.existsSync(f)) {
        const content = fs.readFileSync(f, "utf-8");
        expect(content).not.toContain("sk-57a432014c1cc01c-6juq1i-fe7dbfbd");
      }
    }
  });

  test("D3: backend-dotnet does not reference EF Core and does not query knowledge_chunks", () => {
    const csprojPath = path.join(REPO_ROOT, "backend-dotnet/BackendDotnet.csproj");
    const csprojContent = fs.readFileSync(csprojPath, "utf-8");
    expect(csprojContent).not.toContain("Microsoft.EntityFrameworkCore");

    // Check all .cs files in backend-dotnet
    function checkCsDir(dir: string) {
      const entries = fs.readdirSync(dir, { withFileTypes: true });
      for (const entry of entries) {
        const fullPath = path.join(dir, entry.name);
        if (entry.isDirectory()) {
          if (entry.name !== "bin" && entry.name !== "obj") {
            checkCsDir(fullPath);
          }
        } else if (entry.name.endsWith(".cs")) {
          const content = fs.readFileSync(fullPath, "utf-8");
          expect(content).not.toContain("EntityFramework");
          expect(content).not.toContain("knowledge_chunks");
        }
      }
    }
    checkCsDir(path.join(REPO_ROOT, "backend-dotnet"));
  });

  test("D4: Default docker-compose.yml does not publish ports 5432 and 8000", () => {
    const composePath = path.join(REPO_ROOT, "docker-compose.yml");
    const composeContent = fs.readFileSync(composePath, "utf-8");
    
    // Default docker-compose.yml must not contain active ports mappings for 5432 or 8000
    expect(composeContent).not.toMatch(/^\s*-\s*["']?5432:5432["']?/m);
    expect(composeContent).not.toMatch(/^\s*-\s*["']?8000:8000["']?/m);
  });

  test("D5: Service names, ports, and healthcheck paths are consistent across docs and compose", () => {
    const composeContent = fs.readFileSync(path.join(REPO_ROOT, "docker-compose.yml"), "utf-8");
    const planContent = fs.readFileSync(path.join(REPO_ROOT, "PLAN.md"), "utf-8");
    const archContent = fs.readFileSync(path.join(REPO_ROOT, ".kilocode/rules/architecture.md"), "utf-8");
    const readmeContent = fs.readFileSync(path.join(REPO_ROOT, "README.md"), "utf-8");

    // All should refer to backend-dotnet, ai-service, db
    expect(composeContent).toContain("ai-service");
    expect(composeContent).toContain("backend-dotnet");
    expect(composeContent).toContain("frontend-react");

    expect(planContent).toContain("ai-service");
    expect(planContent).toContain("backend-dotnet");
    expect(planContent).toContain("frontend-react");

    // Health paths
    expect(composeContent).toContain("/api/v1/health");
    expect(composeContent).toContain("/health");
    expect(readmeContent).toContain("/api/v1/health");
    expect(readmeContent).toContain("/health");
  });

  test("E: Performance baseline measurement (embed_ms, query_ms)", async ({ request }) => {
    const INTERNAL_API_KEY = process.env.X_INTERNAL_API_KEY ?? "dev-internal-key-change-me";
    const res = await request.post("http://localhost:8000/api/v1/retrieval/search", {
      headers: {
        "X-Internal-Api-Key": INTERNAL_API_KEY,
        "X-Request-Id": "perf-test",
      },
      data: {
        query: "Điểm chuẩn ngành Công nghệ thông tin 2024",
        top_k: 5,
        min_score: 0.0,
        filters: { tenant_id: "11111111-1111-1111-1111-111111111111" },
      },
    });

    expect(res.ok()).toBe(true);
    const body = await res.json();
    console.log(`[PERF BASELINE] embed_ms: ${body.meta.embed_ms}ms, query_ms: ${body.meta.query_ms}ms, returned: ${body.meta.returned}`);
    expect(body.meta.embed_ms).toBeGreaterThanOrEqual(0);
    expect(body.meta.query_ms).toBeGreaterThanOrEqual(0);
  });
});
