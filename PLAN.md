# PLAN.md — Khuôn mẫu tái sử dụng Chatbot AI (domain tuyển sinh là bản mẫu đầu tiên)

**Mục tiêu:** Xây dựng nền tảng chatbot AI có thể gắn vào nhiều hệ thống thực tế, mỗi hệ thống có backend .NET và nghiệp vụ riêng. Python là dịch vụ nền tảng (embedding, ingestion, retrieval) không biết gì về nghiệp vụ. .NET giữ toàn bộ nghiệp vụ (xác thực, phân quyền, tenant scope, session, lịch sử, truy vấn có cấu trúc, guardrail, prompt, gọi LLM, stream SSE, citations). Chạy toàn bộ stack bằng lệnh duy nhất `docker compose up --build`.

**Kiến trúc:** Phương án B — Monorepo: PostgreSQL 16 + pgvector (DB), Python FastAPI (RAG Data Service: bge-m3 1024 chiều, ingestion, retrieval), ASP.NET Core .NET 10 (Gateway & Orchestration: xác thực, phân quyền, structured query Dapper+Npgsql, guardrail, prompt, gọi LLM qua 9Router, stream SSE), React 19 + Vite + TypeScript + TailwindCSS (frontend, chỉ gọi .NET).

**Luật bắt buộc:** Tuân thủ nghiêm ngặt `.kilocode/rules/architecture.md` (tech stack, data flow, ranh giới cứng, guardrails anti-hallucination).

---

## Phase 1: Infra & Database (PostgreSQL 16 + pgvector)

**Mục tiêu:** Khởi tạo cơ sở dữ liệu vector, schema chia thành migration plataforma và migration domain, cơ sở hạ tầng docker-compose.

### 1.1 Cấu hình PostgreSQL 16 + pgvector
- Image: `pgvector/pgvector:pg16`.
- Bật extension: `CREATE EXTENSION IF NOT EXISTS vector;`.
- Môi trường: `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB`, `PGVECTOR_DIM=1024`.
- Volume `pgdata` lưu trữ dữ liệu liên tục.
- Healthcheck: `pg_isready -U postgres -d chatbot`.
- **[ĐỔI] Port:** Không publish 5432 ra host trong base compose (chỉ qua `docker-compose.override.yml` profile dev); container giao nội bộ qua hostname `db`.

### 1.2 Schema — tách thành 2 nhóm migration (cộng thêm, không sửa bảng nghiệp vụ hiện có)

**[MỚI] Nhóm migration plataforma** (áp dụng cho mọi domain, áp dụng trước) — file `database/migrations/01_platform.sql`:
- `tenants(id, name, code, status, config jsonb, created_at, updated_at)` — cấu hình theo tenant (top_k, min_score, câu từ chối, mẫu prompt, hệ thống tên miền).
- `documents(id, tenant_id, source, title, doc_type, status, content_hash, metadata jsonb, created_at, updated_at)`.
- `knowledge_chunks(id, tenant_id, document_id, chunk_index, content, embedding vector(1024), content_hash, embedding_model, embedding_version, lang, metadata jsonb, created_at)`.
- `ingestion_jobs(id, tenant_id, document_id, status, started_at, finished_at, error_message, created_at)`.
- `chat_sessions(id, tenant_id, user_id, title, created_at, updated_at, deleted_at)`.
- `chat_messages(id, session_id, role, content, citations jsonb, metadata jsonb, created_at, deleted_at)`.

**[MỚI] Nhóm migration domain tuyển sinh** (file migration riêng, chỉ domain mẫu đầu tiên) — `database/migrations/02_admissions_domain.sql`:
- `majors(id, code, name, block, description, subject_combination, quota, created_at)` — ngành học.
- `admission_scores(id, major_id, year, min_score, subject_combination, quota, created_at)` — điểm chuẩn.
- `tuition_scholarships(id, major_id, tuition_per_year, scholarship_info, currency, created_at)` — học phí & học bổng.
- `admission_methods(id, method_name, description, requirements, created_at)` — phương thức xét tuyển.
- **Không sửa** các bảng/domain này khi gắn hệ thống mới; mỗi hệ thống mới thêm migration domain riêng.

**[MỚI] `knowledge_chunks`:** cột `tenant_id`, `document_id`, `chunk_index`, `content_hash`, `embedding_model`, `embedding_version`, `lang`, `metadata jsonb`.
- Chỉ số: HNSW cosine trên `embedding`; B-tree trên `(tenant_id, document_id)`; GIN trên `metadata`; UNIQUE(`document_id`, `chunk_index`).
- Chọn HNSW hay IVFFlat theo benchmark thực tế (xem mục mở rộng sau).

**[MỚI] Phân quyền DB (least privilege):** role `ai_service` chỉ có quyền trên `documents`, `knowledge_chunks`, `ingestion_jobs`; không chạm bảng chat và bảng domain. role `backend` có quyền trên bảng chat và bảng domain. Mục tùy chọn: Row-Level Security theo `tenant_id` (bắt buộc nếu nhiều tenant chung một DB — xem Giả định chờ xác nhận).

### 1.3 Docker-compose căn bản (Phase 1)
- Service `db` (hostname `db`) + volume mount `./database/init.sql` vào `/docker-entrypoint-initdb.d/01-init.sql`.
- **[MỚI]** `db` và `ai-service` chỉ nằm trong network nội bộ. Không publish 5432 và 8000 ra host ngoài profile dev (dùng `docker-compose.override.yml`).
- Kiểm tra: `docker compose up -d db` → `pg_isready` thành công.

### 1.4 Kiểm tra điểm mốc Phase 1
- [ ] Migration plataforma tạo đầy đủ bảng `tenants`, `documents`, `knowledge_chunks`, `ingestion_jobs`, `chat_sessions`, `chat_messages`.
- [ ] Migration domain tuyển sinh tạo `majors`, `admission_scores`, `tuition_scholarships`, `admission_methods`.
- [ ] Chỉ số HNSW cosine tồn tại trên `knowledge_chunks(embedding)`; B-tree `(tenant_id, document_id)`; GIN `metadata`.
- [ ] `db` healthy trong docker-compose; cổng không expose ra host (trừ override dev).

---

## Phase 2: Python "Embedding & Retrieval Service" (FastAPI + bge-m3)

**Mục tiêu:** Xây dựng service AI chịu trách nhiệm embedding, ingestion, tìm kiếm vector. **[ĐỔI] Python KHÔNG gọi LLM, KHÔNG dựng prompt, KHÔNG quyết định guardrail, KHÔNG stream SSE.**

### 2.1 Cấu hình ai-service (RAG Data Service)
- Image base: `python:3.12-slim`.
- **[ĐỔI] Dependencies:** `fastapi`, `uvicorn`, `pydantic`, `psycopg2-binary`, `pgvector`, `sentence-transformers`, `httpx`, `tenacity`, `python-dotenv`, `structlog`. **[BỎ] `openai`.**
- Volume cache model: `bge-m3-cache` mount vào `/root/.cache/huggingface/hub` (tránh tải lại model khi restart container).
- Môi trường: `POSTGRES_*`, `PGVECTOR_DIM=1024`, `EMBEDDING_MODEL=BAAI/bge-m3`, `EMBEDDING_DEVICE=cpu`, `X_INTERNAL_API_KEY` (service-to-service). **[BỎ] `LLM_BASE_URL`, `LLM_API_KEY`, `LLM_MODEL`, `GATEWAY_URL`.**
- **[MỚI] Healthcheck:** `GET /api/v1/health` (process sống) và `GET /api/v1/ready` (chỉ OK khi model đã load và DB kết nối được).
- **[ĐỔI] Port expose:** Không publish 8000 ra host ngoài profile dev (`docker-compose.override.yml`).
- **[BỎ]** `extra_hosts: host.docker.internal:host-gateway` (không còn gọi 9Router từ Python).

### 2.2 Embedding & Vector Search
- Hàm `embed_text(text)` sử dụng `sentence-transformers` với `BAAI/bge-m3`, output 1024 dims, normalize cosine.
- **[MỚI]** Chạy embed trong executor để không chặn event loop; batch size cấu hình; cache model volume.
- Hàm `search_similar_chunks(...)` dùng pgvector cosine distance `<=>` trên `knowledge_chunks`, áp `tenant_id` và filter whitelist, trả top-k kèm score + metadata.
- Hybrid search (tương lai), rerank, query expansion: ghi ở mục "Mở rộng sau", không đưa vào Definition of Done.

### 2.3 Ingestion (bất đồng bộ)
- **[MỚI] `POST /ingestion/documents`** — nhận file hoặc nội dung, trả `jobId` (xử lý bất đồng bộ, theo dõi trong `ingestion_jobs`).
- `GET /ingestion/jobs/{jobId}`, `DELETE /ingestion/documents/{id}`, `POST /ingestion/reembed`.
- **[ĐỔI] Seed:** chuyển `/ingest/seed` thành **script seed riêng** (CLI hoặc profile compose), nạp dữ liệu mẫu qua đúng pipeline ingestion. Idempotent theo `content_hash`.
- **[BỎ]** Tự sinh dữ liệu tuyển sinh giả lập trong code endpoint.

### 2.4 Endpoint `POST /api/v1/retrieval/search` (tiền tố `/api/v1`)

Input:
```json
{
  "query": "string",
  "top_k": 5,
  "min_score": 0.0,
  "filters": { "tenant_id": "...", "doc_type": ["..."], "document_status": "published", "metadata": {"year": 2024} }
}
```

Response:
```json
{
  "success": true,
  "request_id": "...",
  "results": [
    { "document_id": "...", "chunk_id": "...", "content": "...", "score": 0.87, "metadata": { "title": "...", "source": "...", "page": 1 } }
  ],
  "meta": { "embedding_model": "BAAI/bge-m3", "embedding_version": "1", "dim": 1024, "embed_ms": 120, "query_ms": 45, "returned": 3 },
  "timing_ms": { "embed": 120, "vector": 45, "total": 165 }
}
```

Quy tắc hợp đồng:
- `tenant_id` bắt buộc trong `filters`; thiếu thì trả 422.
- Filter không nằm trong whitelist thì trả 422 (không bỏ qua im lặng, không bao giờ nối chuỗi SQL).
- Python không được mở rộng phạm vi so với filter nhận được.
- `score` = cosine similarity trong [0,1], tức `1 − distance`. Ghi rõ ý nghĩa này trong tài liệu kèm API.
- `top_k` mặc định 5, tối đa 20.
- Không có kết quả thì trả `success: true, results: []` (không phải lỗi).
- Lỗi dùng một cấu trúc thống nhất `{success:false, error:{code,message}, request_id}`.
  `INVALID_REQUEST` 400 · `UNAUTHORIZED` 401 · `FILTER_NOT_ALLOWED` 403
  `MODEL_NOT_READY` 503 · `DB_UNAVAILABLE` 503 · `INTERNAL` 500

Xác thực service-to-service: header `X-Internal-Api-Key` (hoặc JWT nội bộ) và `X-Request-Id`. Không log key hay token.

### 2.5 Kiểm tra điểm mốc Phase 2
- [ ] Python không còn import `openai`; không gọi LLM, không dựng prompt, không stream SSE.
- [ ] `GET /api/v1/health` và `GET /api/v1/ready` trả OK.
- [ ] `POST /api/v1/retrieval/search` bắt buộc `tenant_id`; thiếu hoặc filter ngoài whitelist → 422.
- [ ] `score` trong [0,1] (= 1 − distance cosine).
- [ ] Không có kết quả → `success: true, results: []`.
- [ ] Script seed idempotent theo `content_hash`, nạp qua pipeline ingestion.
- [ ] Model bge-m3 được cache trong volume, không tải lại khi restart.

---

## Phase 3: .NET 10 Gateway & Orchestration (ASP.NET Core Web API + Dapper)

**Mục tiêu:** Xây dựng gateway chịu trách nhiệm xác thực, phân quyền, tenant scope, điều phối luồng chat, guardrail, prompt, gọi LLM qua 9Router, stream SSE về React, lưu message và citations.

### 3.1 Cấu hình backend-dotnet
- Target framework: `net10.0`.
- Packages: `Dapper`, `Npgsql`, `Microsoft.Extensions.Http.Polly`.
- Môi trường: `ConnectionStrings__Default` (Host=db;...), `PYTHON_RAG_URL=http://ai-service:8000`, `LLM_API_KEY` (đọc từ `.env`, dùng cho 9Router), `ASPNETCORE_URLS=http://+:5000`.
- Port expose: `5000:5000`.
- Healthcheck: `GET /health`.
- Không dùng Entity Framework Core; tất cả truy vấn qua Dapper + Npgsql parameterized (tránh SQL injection).

### 3.2 Abstraction (mới)
- `IRetrievalClient` — gọi Python `/api/v1/retrieval/search`.
- `ILlmClient` — gọi 9Router (OpenAI-compatible).
- `IGuardrail`, `IPromptBuilder`, `IStructuredQueryService`.
- Domain tuyển sinh là một implementation cắm vào abstractions này; thêm hệ thống mới không đụng Python.

### 3.3 Endpoint `POST /api/chat/stream`
Input: `{ sessionId?, message }`. **[ĐỔI] Bỏ `userId` do client gửi.**

Luồng xử lý:
1. Xác thực và xác định phạm vi dữ liệu từ danh tính người dùng (`tenant_id`, `organization_ids`...). **Không tin `tenant_id` hay `userId` do client gửi.**
2. Tạo hoặc kiểm tra quyền sở hữu session, lưu tin nhắn người dùng vào `chat_messages` (role=`user`) qua Dapper.
3. Structured query trước (nếu câu hỏi là số liệu: điểm chuẩn, học phí, học bổng, lịch tuyển sinh), semantic `/api/v1/retrieval-search` bổ trợ.
4. Guardrail: nếu không có chunk nào vượt ngưỡng `min_score` và không có kết quả structured, trả câu từ chối cố định, **không gọi LLM**.
5. Dựng prompt, gọi LLM stream qua 9Router, pipe SSE về React (`CancellationToken` khi client ngắt kết nối).
6. Lưu tin nhắn assistant hoàn chỉnh + citations.

Prompt, câu từ chối, `top_k`, `min_score` nằm trong **cấu hình theo tenant**, không hard-code.

Polly: timeout, retry có giới hạn, circuit breaker cho lời gọi Python và LLM.

### 3.4 Endpoint `GET /api/chat/history/{sessionId}`
- Truy xuất danh sách `chat_messages` của phiên, sắp xếp theo `created_at` ASC.
- **[MỚI]** Phải kiểm tra quyền sở hữu session (chống IDOR) trước khi trả dữ liệu.

### 3.5 Endpoint `GET /api/suggestions`
- Giữ nguyên: trả về 4 câu hỏi gợi ý mẫu cố định.

### 3.6 Chuẩn SSE duy nhất
- `{"type":"token|citations|done|error", ...}`. **[MỚI] Thay thế định dạng `[CITATIONS]`/`[DONE]` thô.**

### 3.7 Kiểm tra điểm mốc Phase 3
- [ ] `POST /api/chat/stream` xác thực, xác định tenant từ danh tính (không tin client gửi), lưu user message, gọi Python `/search`, guardrail, gọi LLM stream, pipe SSE real-time, lưu assistant + citations.
- [ ] `GET /api/chat/history/{sessionId}` kiểm tra quyền sở hữu trước khi trả dữ liệu.
- [ ] Python tắt → .NET trả lỗi thân thiện và không treo (Polly timeout / circuit breaker).
- [ ] Dapper parameterized queries, không dùng string concat SQL.

---

## Phase 4: React 19 UI (Vite + TypeScript + TailwindCSS)

**Mục tiêu:** Giữ giao diện và hook `useChatStream`; chỉ đồng bộ với chuẩn SSE đã thống nhất.

### 4.1 Cấu hình frontend-react
- Build tool: Vite.
- Framework: React 19 + TypeScript strict.
- Styling: TailwindCSS + PostCSS + Autoprefixer.
- Icon: `lucide-react`.
- Proxy dev: `/api` → `http://backend-dotnet:5000`.
- Port expose: `5173:80` (container chạy Nginx phục vụ static build).
- Dockerfile multi-stage: build với Node 20, serve với `nginx:alpine`.
- **[MỚI]** Frontend chỉ gọi .NET. Không gọi Python, không kết nối DB.

### 4.2 Thành phần giao diện
- **Layout:** Responsive, sidebar trên desktop (danh sách phiên), full-screen trên mobile.
- **Welcome screen:** Hiển thị 4 nút Suggestion Chips khi chưa có cuộc trò chuyện.
- **ChatWindow:** Vùng hiển thị tin nhắn, cuộn tự động xuống tin nhắn mới nhất.
- **MessageBubble:** Hiển thị nội dung user (màu xanh bên phải) và assistant (trắng bên trái), hỗ trợ render Markdown (bảng điểm chuẩn, danh sách ngành, in đậm, in nghiêng).
- **Streaming effect:** Hiệu ứng gõ chữ mượt mà, hiển thị con trỏ nhấp nháy khi bot đang trả lời.
- **CitationBadges:** Hiển thị thẻ nguồn trích dẫn (`Nguồn 1: Tên chunk — score`) dưới mỗi câu trả lời của bot.
- **Input form:** Ô nhập liệu, nút Gửi, disable khi đang stream.

### 4.3 Hook `useChatStream`
- Sử dụng native `fetch` ReadableStream hoặc `@microsoft/fetch-event-source` để consume SSE từ `/api/chat/stream`.
- Quản lý trạng thái: `messages[]`, `isStreaming`, `currentSessionId`.
- Xử lý sự kiện SSE: `token` → cập nhật nội dung đang gõ; `citations` → gắn trích dẫn; `done` → kết thúc stream và lưu sessionId; `error` → hiển thị thông báo lỗi.
- **[ĐỔI]** Sửa lỗi chính tả "fuentes" → "citations".

### 4.4 Kiểm tra điểm mốc Phase 4
- [ ] Giao diện responsive, render Markdown bảng/danh sách đúng.
- [ ] 4 suggestion chips hiển thị trên màn hình chào mừng.
- [ ] Streaming typing effect mượt mà, Citation Badges hiển thị dưới câu trả lời bot.
- [ ] `docker compose up --build` chạy toàn bộ 4 service, chat end-to-end hoạt động.

---

## Phase 5: Harness kiểm chứng chạy thật

**Mục tiêu:** Mọi tuyên bố "đã xong" đều có bằng chứng chạy thật (R1). Harness nằm ở `scripts/verify.ps1` (Windows) và `scripts/verify.sh`, báo cáo vào `docs/verification/run-<timestamp>.md`.

### 5.1 L0 Tĩnh
- `docker compose config` hợp lệ; build cả 4 image; build .NET; lint Python; quét bí mật (`sk-`, `password=`, `Bearer `).

### 5.2 L1 Sống
- `/health` và `/ready` của từng service; model bge-m3 đã nạp; DB có extension vector và index HNSW.

### 5.3 L2 Contract
- Python `/search` đúng schema: request hợp lệ, filter lạ → 400, thiếu key → 401, không kết quả → []. Xuất OpenAPI từ FastAPI và so với `docs/api/*.yaml`. DTO của .NET khớp response thật.

### 5.4 L3 Retrieval
- Nạp tài liệu mẫu qua `/ingest`, chạy bộ câu hỏi vàng `docs/verification/golden.jsonl` (≥ 20 câu có chunk/nguồn kỳ vọng) trên dữ liệu thật. Đo recall@5 và phân bố score cho câu trong phạm vi so với ngoài phạm vi. Ngưỡng `min_score` phải hiệu chỉnh từ số đo này, không đoán.

### 5.5 L4 E2E LLM thật
- `POST /api/chat/stream` qua .NET: có token, citations, done, lưu đủ 2 message. Câu số liệu lấy từ bảng có cấu trúc và số liệu trong câu trả lời khớp DB.

### 5.6 L5 Guardrail
- Câu ngoài phạm vi (thời tiết, code, chính trị, ngành không có) → đúng câu từ chối cố định, và log cho thấy không có lời gọi LLM. Mỗi case chạy 3 lần.

### 5.7 L6 Bảo mật & vận hành
- Cổng 8000 không truy cập được từ ngoài mạng Docker. Gọi Python không key → 401. User A đọc history của session user B → bị từ chối. Ngắt client giữa stream → .NET hủy lời gọi LLM, không lưu message dở. Tắt Python → .NET trả lỗi SSE error rõ ràng, không treo. `X-Request-ID` xuất hiện trong log cả 3 service.

### 5.8 Đối chiếu tự động tài liệu ↔ code (`scripts/check-consistency`)
- Endpoint trong PLAN/README/architecture so với route thực tế (OpenAPI của FastAPI và route .NET).
- Tên biến môi trường trong docker-compose.yml, `.env.example`, code và README.
- Hình dạng sự kiện SSE: bên phát (.NET), bên đọc (hook React), bên tài liệu phải trùng nhau.
- Mọi câu truy vấn Dapper chạy được trên schema thật (chạy thật, không đoán).
- Cổng và hostname trong compose khớp với connection string.
- Checkbox trong PLAN chỉ được tích khi có dòng bằng chứng tương ứng trong `docs/verification/`.

### 5.9 Vòng lặp tự sửa lỗi
- Tái hiện, định vị (theo `X-Request-ID`), nguyên nhân gốc, sửa tối thiểu trong phạm vi R3, thêm kiểm tra hồi quy, chạy lại.
- Cấm: tăng timeout mù quáng, nuốt exception, hạ ngưỡng, xóa kiểm tra, nhánh test, chuyển sang mock.

### 5.10 Điều kiện dừng và hỏi
- Đã thử 5 lần cho cùng một lỗi, hoặc cùng chữ ký lỗi lặp lại 2 lần liên tiếp sau khi sửa.
- Cách sửa cần đụng vùng cấm ở R3, đổi contract đã chốt, hoặc đổi quyết định ở locked_decisions.
- Preflight LLM thất bại, hoặc thiếu thông tin chỉ người dùng mới có (key, địa chỉ 9Router, dữ liệu thật).

---

## Danh sách services trong `docker-compose.yml`

| Service | Image/Dockerfile | Port | Depends on | Expose ra ngoài | Lưu ý |
|---|---|---:|---|---|---|
| `db` | `pgvector/pgvector:pg16` | 5432 (chỉ override dev) | — | Không (trừ dev) | Volume `pgdata`, mount `init.sql`, hostname `db` |
| `ai-service` | `./ai-service` | 8000 (chỉ override dev) | `db` healthy | Không (trừ dev) | Volume `bge-m3-cache`, network nội bộ, không gọi LLM |
| `backend-dotnet` | `./backend-dotnet` | 5000 | `db`, `ai-service` healthy | Có (gateway) | Gọi Python nội bộ Docker network |
| `frontend-react` | `./frontend-react` | 5173 | `backend-dotnet` healthy | Có | Nginx serve static, proxy `/api` |

**Lệnh chạy:** `docker compose up --build` tại thư mục gốc.

---

## Hợp đồng API .NET ⇄ Python
Xem `docs/api-contract-python.md` (sinh từ OpenAPI của FastAPI, không viết tay).

---

## Cách áp dụng cho hệ thống mới (không sửa code nghiệp vụ Python)
1. Thêm tenant mới trong bảng `tenants` (cấu hình `top_k`, `min_score`, câu từ chối, mẫu prompt, tên miền).
2. Nạp tài liệu của hệ thống mới qua pipeline ingestion của Python (idempotent theo `content_hash`).
3. Viết adapter structured query + prompt ở .NET (implementation cắm vào `IStructuredQueryService`, `IPromptBuilder`).
4. **Không đụng code Python.**

---

## Tiêu chí hoàn thành (Definition of Done)

- [ ] Bộ tài liệu đã cập nhật, không còn mâu thuẫn (SSE, health, host DB, seed), không còn key plaintext, đã có `.env.example`.
- [ ] Python chỉ còn `/search`, `/ingest`, `/embed`, `/health`, `/ready`; không còn đường gọi LLM nào trong `ai-service`.
- [ ] .NET đảm nhiệm prompt, guardrail, gọi LLM, phát SSE, lưu lịch sử; React không phải sửa (hoặc đã được cho phép sửa).
- [ ] `docker compose up --build` khởi động 4 service, mọi healthcheck pass.
- [ ] `start-dev.bat` / `stop-dev.bat` chạy đúng trên Windows PowerShell/CMD.
- [ ] Python không còn import `openai`; không gọi LLM, không dựng prompt, không stream SSE.
- [ ] .NET không truy vấn bảng vector (`knowledge_chunks`); chỉ structured qua Dapper + semantic qua `/api/v1/retrieval-search`.
- [ ] Mọi chỗ nhắc SSE dùng cùng định dạng `{"type":"token|citations|done|error"}`.
- [ ] `tenant_id` luôn do .NET xác định từ danh tính người dùng; Python bắt buộc áp filter `tenant_id`.
- [ ] Không còn API key hay secret plaintext trong 3 file (`PLAN.md`, `architecture.md`, `README.md`).
- [ ] Tên service, host, cổng, đường dẫn health nhất quán giữa 3 file.
- [ ] Luồng chat end-to-end: React gửi tin → .NET lưu → Python retrieval → .NET guardrail/prompt/LLM stream SSE → .NET lưu câu trả lời bot → hiển thị citations.
- [ ] Python tắt → .NET trả lỗi thân thiện, không treo.
- [ ] Guardrail từ chối cố định (do .NET, cấu hình theo tenant) hoạt động đúng khi hỏi ngoài phạm vi tuyển sinh.
- [ ] Dữ liệu seed có sẵn qua script ingestion riêng (idempotent theo `content_hash`).
- [ ] Hybrid search, rerank, query expansion chỉ ghi ở mức "mở rộng sau", không trong Definition of Done.

---

## Câu hỏi mở (Giả định chờ xác nhận)

| # | Câu hỏi | Giả định đang dùng | Trạng thái |
|---|---|---|---|
| Q1 | Nhiều tenant dùng chung một PostgreSQL, hay mỗi khách một deployment? | Nhiều tenant chung một DB; cần xem xét Row-Level Security theo `tenant_id` | **Đã trả lời:** nhiều tenant chung DB |
| Q2 | Nguồn dữ liệu thật khi đưa vào hệ thống khác: PDF, Word, Excel, website, hay DB có sẵn? | PDF + DOCX + XLSX | Chưa trả lời |
| Q3 | Giữ seed dữ liệu giả cho môi trường demo, hay chỉ nạp tài liệu thật? | Giữ seed, gắn nhãn `is_demo=true`, tách khỏi luồng nghiệm thu retrieval | Chưa trả lời |
| Q4 | Xác thực service-to-service .NET → Python | API key trong header `X-Internal-Api-Key`, nạp từ env | Chưa trả lời |
| Q5 | 9Router chạy ở đâu khi triển khai thật (host, server, cloud)? Key mới đã cấp chưa? Model vẫn là `chatbotai`? | `http://host.docker.internal:20128/v1`, model `chatbotai` | **Chặn preflight LLM** — chờ trả lời |
| Q6 | Plan này chỉ cho dự án tuyển sinh, hay là template dùng chung để gắn vào hệ thống tiếp theo? | Template dùng chung, tuyển sinh là bản mẫu đầu tiên | Đã trả lời |
| Q7 | Hybrid search và rerank làm ngay hay để sau? | Để sau, chỉ thiết kế sẵn chỗ cắm trong contract | Đã trả lời |
| Q8 | Người dùng cuối đã có đăng nhập chưa? (ảnh hưởng chống IDOR cho history) | Dev auth qua header `X-Tenant-Id` / `Authorization` | **Chờ trả lời** |
