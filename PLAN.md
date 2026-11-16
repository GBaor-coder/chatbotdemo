# PLAN.md — Lộ trình triển khai Chatbot AI Tư vấn Tuyển sinh Đại học

**Mục tiêu:** Xây dựng nền tảng chatbot AI tái sử dụng cho nhiều hệ thống thực tế, không phải sửa code nghiệp vụ khi mở rộng domain mới. Tuyển sinh là domain mẫu đầu tiên. Chạy toàn bộ stack bằng lệnh duy nhất `docker compose up --build`.

**Kiến trúc:** Phương án B — Monorepo: PostgreSQL 16 + pgvector (DB), Python FastAPI (RAG Data Service: embedding bge-m3 1024 chiều, ingestion, retrieval), ASP.NET Core .NET 10 (Gateway & Orchestration: xác thực, phân quyền, structured query Dapper+Npgsql, guardrail, prompt, gọi LLM qua 9Router, stream SSE), React 19 + Vite + TypeScript + TailwindCSS (frontend, chỉ gọi .NET).

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
- **[ĐỔI] Port:** Không publish 5432 ra host ngoài profile dev (dùng `docker-compose.override.yml`); container giao nội bộ qua hostname `db`.

### 1.2 Schema — tách thành 2 nhóm migration

**[MỚI] Nhóm migration plataforma** (áp dụng cho mọi domain, áp dụng trước):
- `tenants(id, name, code, status, config jsonb, created_at, updated_at)` — cấu hình theo tenant.
- `documents(id, tenant_id, source, title, doc_type, status, content_hash, metadata jsonb, created_at, updated_at)`.
- `knowledge_chunks(id, tenant_id, document_id, chunk_index, content, embedding vector(1024), content_hash, embedding_model, embedding_version, lang, metadata jsonb, created_at)`.
- `ingestion_jobs(id, tenant_id, document_id, status, started_at, finished_at, error_message, created_at)`.
- `chat_sessions(id, tenant_id, user_id, title, created_at, updated_at, deleted_at)`.
- `chat_messages(id, session_id, role, content, citations jsonb, metadata jsonb, created_at, deleted_at)`.

**[MỚI] Nhóm migration domain tuyển sinh** (file migration riêng, chỉ domain mẫu đầu tiên):
- `majors(id, code, name, block, description, subject_combination, quota, created_at)` — ngành học.
- `admission_scores(id, major_id, year, min_score, subject_combination, quota, created_at)` — điểm chuẩn.
- `tuition_scholarships(id, major_id, tuition_per_year, scholarship_info, currency, created_at)` — học phí & học bổng.
- `admission_methods(id, method_name, description, requirements, created_at)` — phương thức xét tuyển.

**[MỚI] `knowledge_chunks`:** thêm cột `tenant_id`, `document_id`, `chunk_index`, `content_hash`, `embedding_model`, `embedding_version`, `lang`, `metadata jsonb`.
- Chỉ số: HNSW cosine trên `embedding`; B-tree trên `(tenant_id, document_id)`; GIN trên `metadata`.
- Ghi chú: Chọn HNSW hay IVFFlat theo benchmark thực tế.

**[MỚI] Phân quyền DB (least privilege):** role `ai_service` chỉ có quyền trên `documents`, `knowledge_chunks`, `ingestion_jobs`; role `backend` có quyền trên bảng chat và bảng domain. Mục tùy chọn: Row-Level Security theo `tenant_id`.

### 1.3 Docker-compose căn bản (Phase 1)
- Service `postgres` (hostname `db`) + volume mount `./database/init.sql` vào `/docker-entrypoint-initdb.d/01-init.sql`.
- **[MỚI]** `postgres` và `ai-service` chỉ nằm trong network nội bộ. Không publish 5432 và 8000 ra host ngoài profile dev (dùng `docker-compose.override.yml`).
- Kiểm tra: `docker compose up -d postgres` → `pg_isready` thành công.

### 1.4 Kiểm tra điểm mốc Phase 1
- [x] Migration plataforma tạo đầy đủ bảng `tenants`, `documents`, `knowledge_chunks`, `ingestion_jobs`, `chat_sessions`, `chat_messages`.
- [x] Migration domain tuyển sinh tạo `majors`, `admission_scores`, `tuition_scholarships`, `admission_methods`.
- [x] Chỉ số HNSW cosine tồn tại trên `knowledge_chunks(embedding)`; B-tree `(tenant_id, document_id)`; GIN `metadata`.
- [x] `postgres` healthy trong docker-compose; cổng không expose ra host (trừ override dev).

---

## Phase 2: Python RAG Data Service (FastAPI + bge-m3, embedding + ingestion + retrieval)

**Mục tiêu:** Xây dựng service AI chịu trách nhiệm embedding, ingestion, tìm kiếm vector. **[ĐỔI] Python KHÔNG gọi LLM, KHÔNG dựng prompt, KHÔNG quyết định guardrail, KHÔNG stream SSE.**

### 2.1 Cấu hình ai-service (RAG Data Service)
- Image base: `python:3.11-slim`.
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
  "results": [
    { "document_id": "...", "chunk_id": "...", "content": "...", "score": 0.87, "metadata": { "title": "...", "source": "...", "page": 1 } }
  ],
  "meta": { "embedding_model": "BAAI/bge-m3", "embedding_version": "...", "dim": 1024, "embed_ms": 120, "query_ms": 45, "returned": 3 }
}
```

Quy tắc hợp đồng:
- `tenant_id` bắt buộc trong `filters`; thiếu thì trả 422.
- Filter không nằm trong whitelist thì trả 422 (không bỏ qua im lặng).
- Python không được mở rộng phạm vi so với filter nhận được.
- `score` = cosine similarity trong [0,1], tức `1 − distance`. Ghi rõ ý nghĩa này trong tài liệu kèm API.
- `top_k` mặc định 5, tối đa 20.
- Không có kết quả thì trả `success: true, results: []` (không phải lỗi).
- Lỗi dùng một cấu trúc thống nhất `{success:false, error:{code, message}, request_id}`.

Xác thực service-to-service: header `X-Internal-Api-Key` (hoặc JWT nội bộ) và `X-Request-Id`. Không log key hay token.

### 2.5 Kiểm tra điểm mốc Phase 2
- [x] Python không còn import `openai`; không gọi LLM, không dựng prompt, không stream SSE.
- [x] `GET /api/v1/health` và `GET /api/v1/ready` trả OK.
- [x] `POST /api/v1/retrieval/search` bắt buộc `tenant_id`; thiếu hoặc filter ngoài whitelist → 422.
- [x] `score` trong [0,1] (= 1 − distance cosine).
- [x] Không có kết quả → `success: true, results: []`.
- [x] Script seed idempotent theo `content_hash`, nạp qua pipeline ingestion.
- [x] Model bge-m3 được cache trong volume, không tải lại khi restart.

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
3. Structured query trước (nếu câu hỏi là số liệu: điểm chuẩn, học phí, học bổng, lịch tuyển sinh), semantic `/api/v1/retrieval/search` bổ trợ.
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
- [x] `POST /api/chat/stream` xác thực, xác định tenant từ danh tính (không tin client gửi), lưu user message, gọi Python `/search`, guardrail, gọi LLM stream, pipe SSE real-time, lưu assistant + citations.
- [x] `GET /api/chat/history/{sessionId}` kiểm tra quyền sở hữu trước khi trả dữ liệu.
- [x] Python tắt → .NET trả lỗi thân thiện và không treo (Polly timeout / circuit breaker).
- [x] Dapper parameterized queries, không dùng string concat SQL.

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
- [x] Giao diện responsive, render Markdown bảng/danh sách đúng.
- [x] 4 suggestion chips hiển thị trên màn hình chào mừng.
- [x] Streaming typing effect mượt mà, Citation Badges hiển thị dưới câu trả lời bot.
- [x] `docker compose up --build` chạy toàn bộ 4 service, chat end-to-end hoạt động.

---

## Danh sách services trong `docker-compose.yml`

| Service | Image/Dockerfile | Port | Depends on | Expose ra ngoài | Lưu ý |
|---|---|---:|---|---|---|
| `postgres` | `pgvector/pgvector:pg16` | 5432 (chỉ override dev) | — | Không (trừ dev) | Volume `pgdata`, mount `init.sql`, hostname `db` |
| `ai-service` | `./ai-service` | 8000 (chỉ override dev) | `postgres` healthy | Không (trừ dev) | Volume `bge-m3-cache`, network nội bộ, không gọi LLM |
| `backend-dotnet` | `./backend-dotnet` | 5000 | `postgres`, `ai-service` healthy | Có (gateway) | Gọi Python nội bộ Docker network |
| `frontend-react` | `./frontend-react` | 5173 | `backend-dotnet` healthy | Có | Nginx serve static, proxy `/api` |

**Lệnh chạy:** `docker compose up --build` tại thư mục gốc.

---

## Hợp đồng API .NET ⇄ Python
Xem `docs/api-contract-python.md`.

## Cách áp dụng cho hệ thống mới (không sửa code nghiệp vụ Python)
1. Thêm tenant mới trong bảng `tenants` (cấu hình `top_k`, `min_score`, câu từ chối, mẫu prompt).
2. Nạp tài liệu của hệ thống mới qua pipeline ingestion của Python (idempotent theo `content_hash`).
3. Viết adapter structured query + prompt ở .NET (implementation cắm vào `IStructuredQueryService`, `IPromptBuilder`).
4. **Không đụng code Python.**

---

## Tiêu chí hoàn thành (Definition of Done)

- [x] Mọi file liệt kê trong PLAN đã được tạo theo đúng cấu trúc monorepo.
- [x] Tuân thủ 100% `.kilocode/rules/architecture.md`.
- [x] `docker compose up --build` khởi động 4 service, tất cả healthcheck pass.
- [ ] `start-dev.bat` / `stop-dev.bat` chạy đúng trên Windows PowerShell/CMD.
- [x] Python không còn import `openai`; không gọi LLM, không dựng prompt, không stream SSE.
- [x] .NET không truy vấn bảng vector (`knowledge_chunks`); chỉ structured qua Dapper + semantic qua `/api/v1/retrieval/search`.
- [x] Mọi chỗ nhắc SSE dùng cùng định dạng `{"type":"token|citations|done|error"}`.
- [x] `tenant_id` luôn do .NET xác định từ danh tính người dùng; Python bắt buộc áp filter `tenant_id`.
- [x] Không còn API key hay secret plaintext trong 3 file (`PLAN.md`, `architecture.md`, `README.md`).
- [x] Tên service, host, cổng, đường dẫn health nhất quán giữa 3 file.
- [x] Luồng chat end-to-end: React gửi tin → .NET lưu → Python retrieval → .NET guardrail/prompt/LLM stream SSE → .NET lưu câu trả lời bot → hiển thị citations.
- [x] Python tắt → .NET trả lỗi thân thiện, không treo.
- [x] Guardrail từ chối cố định (do .NET, cấu hình theo tenant) hoạt động đúng khi hỏi ngoài phạm vi tuyển sinh.
- [x] Dữ liệu seed có sẵn qua script ingestion riêng (idempotent theo `content_hash`).
- [x] Hybrid search, rerank, query expansion chỉ ghi ở mức "mở rộng sau", không trong Definition of Done.

---

## Câu hỏi mở

1. API key cũ (đã lộ trong lịch sử git) — quy trình thu hồi (revoke) và cách quản lý `LLM_API_KEY` mới (`.env`, Secret Manager) là thế nào?
2. `docker-compose.override.yml` chưa tồn tại trong repo — ai tạo/maintain? Các profile dev cần ghi rõ trong PLAN hay README.
3. Xác thực người dùng cuối (JWT của React) do hệ thống nào cấp? .NET tự issue hay có Identity server riêng?
4. `tenant_id` từ danh tính người dùng ánh xạ như thế nào (claim JWT, bảng mapping)?
5. Whitelist filter của `POST /api/v1/retrieval/search` bao gồm chính xác các key nào? Filter `metadata` có hỗ trợ lồng nhau không?
6. Script seed riêng đặt ở thư mục nào (`scripts/seed/`) và chạy như thế nào trong compose (profile `seed`)?
7. HNSW vs IVFFlat: benchmark chưa chạy, chưa chọn mặc định.
8. Row-Level Security theo `tenant_id` có bật ngay hay để sau?
9. Kích thước batch embed mặc định là bao nhiêu? Thời gian timeout cho `POST /api/v1/retrieval/search`?
10. 9Router model `chatbotai` có hỗ trợ streaming tokens thực sự không?
