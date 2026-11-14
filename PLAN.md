# PLAN.md — Lộ trình triển khai Chatbot AI Tư vấn Tuyển sinh Đại học

**Mục tiêu:** Xây dựng hệ thống Chatbot AI tư vấn tuyển sinh Đại học Việt Nam, chạy toàn bộ stack bằng lệnh duy nhất `docker compose up --build`.

**Kiến trúc:** Monorepo 4 services — PostgreSQL 16 + pgvector (DB), Python FastAPI (ai-service, RAG + bge-m3 + 9Router), ASP.NET Core .NET 10 (backend-dotnet, Gateway & History), React 19 + Vite + TailwindCSS (frontend-react).

**Luật bắt buộc:** Tuân thủ nghiêm ngặt `.kilocode/rules/architecture.md` (tech stack, data flow, guardrails anti-hallucination).

---

## Phase 1: Infra & Database (PostgreSQL 16 + pgvector)

**Mục tiêu:** Khởi tạo cơ sở dữ liệu vector, schema nghiệp vụ và cơ sở hạ tầng docker-compose.

### 1.1 Cấu hình PostgreSQL 16 + pgvector
- Image: `pgvector/pgvector:pg16`.
- Bật extension: `CREATE EXTENSION IF NOT EXISTS vector;`.
- Môi trường: `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB`, `PGVECTOR_DIM=1024`.
- Volume `pgdata` lưu trữ dữ liệu liên tục.
- Healthcheck: `pg_isready -U postgres -d chatbot`.
- Port expose: `5432:5432`.

### 1.2 Schema `database/init.sql`
Các bảng nghiệp vụ:
- `majors(id, code, name, block, description, created_at)` — ngành học.
- `admission_scores(id, major_id, year, min_score, subject_combination, quota, created_at)` — điểm chuẩn 3 năm (2023, 2024, 2025).
- `tuition_scholarships(id, major_id, tuition_per_year, scholarship_info, currency, created_at)` — học phí & học bổng.
- `admission_methods(id, method_name, description, requirements, created_at)` — phương thức xét tuyển.
- `chat_sessions(id, user_id, title, created_at, updated_at, deleted_at)` — phiên trò chuyện.
- `chat_messages(id, session_id, role, content, metadata, created_at, deleted_at)` — tin nhắn (user/assistant/system).

Bảng vector:
- `knowledge_chunks(id, source, title, content, embedding vector(1024), metadata jsonb, created_at)`.
- Chỉ số HNSW: `CREATE INDEX ... ON knowledge_chunks USING hnsw (embedding vector_cosine_ops);` cho tìm kiếm vector gần đúng.
- Chỉ số B-tree cho `chat_sessions(user_id, created_at)` và `chat_messages(session_id, created_at)`.

### 1.3 Docker-compose căn bản (Phase 1)
- Service `postgres` + volume mount `./database/init.sql` vào `/docker-entrypoint-initdb.d/01-init.sql`.
- Kiểm tra: `docker compose up -d postgres` → `pg_isready` thành công.

### 1.4 Kiểm tra điểm mốc Phase 1
- [ ] `init.sql` áp dụng extension vector và tạo đầy đủ 7 bảng.
- [ ] Chỉ số HNSW tồn tại trên `knowledge_chunks(embedding)`.
- [ ] `postgres` service healthy trong docker-compose.

---

## Phase 2: Python AI Service & Seed Data (FastAPI + bge-m3 + 9Router RAG)

**Mục tiêu:** Xây dựng service AI chịu trách nhiệm embedding, tìm kiếm vector, sinh câu trả lời qua 9Router và stream SSE.

### 2.1 Cấu hình ai-service
- Image base: `python:3.11-slim`.
- Dependencies: `fastapi`, `uvicorn`, `pydantic`, `psycopg2-binary`, `pgvector`, `sentence-transformers`, `openai`, `httpx`, `tenacity`, `python-dotenv`, `structlog`.
- Volume cache model: `bge-m3-cache` mount vào `/root/.cache/huggingface/hub` (tránh tải lại model khi restart container).
- Môi trường: `POSTGRES_*`, `PGVECTOR_DIM=1024`, `EMBEDDING_MODEL=BAAI/bge-m3`, `EMBEDDING_DEVICE=cpu`, `LLM_BASE_URL=http://host.docker.internal:20128/v1`, `LLM_API_KEY`, `LLM_MODEL=chatbotai`, `GATEWAY_URL=http://backend-dotnet:5000`.
- `extra_hosts: ["host.docker.internal:host-gateway"]` để container gọi 9Router chạy trên máy host.
- Port expose: `8000:8000`.
- Healthcheck: `GET /health`.

### 2.2 Embedding & Vector Search
- Hàm `embed_text(text)` sử dụng `sentence-transformers` với `BAAI/bge-m3`, output 1024 dims, normalize cosine.
- Hàm `search_similar_chunks(embedding, limit=5)` dùng pgvector cosine distance `<=>` trên `knowledge_chunks`, trả về top-k kèm metadata.
- Hybrid search (tương lai): kết hợp full-text search (tsvector) + vector search, rerank bởi điểm số trộn.

### 2.3 Endpoint `POST /ingest/seed`
- Tự động sinh dữ liệu tuyển sinh giả lập chuẩn Đại học Việt Nam (ít nhất 12 ngành):
  - Khối CNTT: Công nghệ thông tin, Khoa học máy tính, Hệ thống thông tin, Kỹ thuật phần mềm, An toàn thông tin.
  - Khối Kinh tế: Kinh doanh, Kinh tế, Tài chính ngân hàng, Kế toán.
  - Khối Ngôn ngữ: Ngôn ngữ Anh, Ngôn ngữ Hàn, Ngôn ngữ Nhật.
  - Khối Truyền thông: Truyền thông đa phương tiện, Quảng cáo.
- Đối mỗi ngành: điền điểm chuẩn 2023, 2024, 2025; tổ hợp xét tuyển; học phí/năm; thông tin học bổng; lịch trình tuyển sinh 2026.
- Ghi vào các bảng nghiệp vụ (`majors`, `admission_scores`, `tuition_scholarships`, `admission_methods`).
- Sinh `knowledge_chunks` cho mỗi mẩu tin với `embedding` từ bge-m3 và `metadata` chứa `source` (tên bảng/nguồn) để trích dẫn.
- Idempotent: chạy nhiều lần không trùng dữ liệu.

### 2.4 Endpoint `POST /rag/stream`
- Input: `{ message, sessionId, userId }`.
- System Prompt (Guardrail chống ảo giác):
  - Chỉ trả lời dựa trên context đã truy xuất.
  - Nếu context không có câu trả lời hoặc câu hỏi ngoài phạm vi tuyển sinh (thời tiết, code, chính trị, ngành không hỗ trợ...), trả lời cứng:
    *"Xin lỗi, hiện tại tôi chỉ hỗ trợ giải đáp các thông tin liên quan đến tuyển sinh của Nhà trường dựa trên dữ liệu chính thức. Bạn vui lòng đặt câu hỏi về ngành học, điểm chuẩn, học phí hoặc phương thức xét tuyển nhé!"*
- Luồng xử lý:
  1. Embed câu hỏi bằng bge-m3.
  2. Tìm kiếm vector top-5 chunks từ `knowledge_chunks`.
  3. Xây dựng prompt kèm context và system prompt.
  4. Gọi 9Router qua OpenAI AsyncClient (`base_url=http://host.docker.internal:20128/v1`, model `chatbotai`), stream tokens.
  5. Emit SSE sự kiện:
     - `data: {"type":"token","content":"..."}` cho từng token.
     - `data: {"type":"citations","sources":[...]}` trước `[DONE]`, chứa danh sách nguồn (`id`, `title`, `source`, `similarity`).
     - `data: {"type":"done"}` kết thúc.
- Xử lý lỗi: emit `data: {"type":"error","message":"..."}` và đóng stream an toàn.

### 2.5 Kiểm tra điểm mốc Phase 2
- [ ] `POST /ingest/seed` nạp ≥12 ngành, điểm chuẩn 3 năm, học phí, học bổng, phương thức, lịch 2026.
- [ ] `POST /rag/stream` trả về SSE tokens + citations + done.
- [ ] Guardrail từ chối đúng khi hỏi ngoài phạm vi.
- [ ] Model bge-m3 được cache trong volume, không tải lại khi restart.

---

## Phase 3: .NET 10 Gateway & History (ASP.NET Core Web API + Dapper)

**Mục tiêu:** Xây dựng API Gateway chịu trách nhiệm lưu lịch sử, proxy SSE từ ai-service và cung cấp danh sách gợi ý.

### 3.1 Cấu hình backend-dotnet
- Target framework: `net10.0`.
- Packages: `Dapper`, `Npgsql`, `Microsoft.Extensions.Http.Polly` (retry/circuit breaker cho gọi ai-service).
- Môi trường: `ConnectionStrings__Default` (Host=db;...), `AI_SERVICE_URL=http://ai-service:8000`, `ASPNETCORE_URLS=http://+:5000`.
- Port expose: `5000:5000`.
- Healthcheck: `GET /health`.
- Không dùng Entity Framework Core; tất cả truy vấn qua Dapper + Npgsql parameterized (tránh SQL injection).

### 3.2 Endpoint `POST /api/chat/stream`
- Input: `{ sessionId?, message, userId }`.
- Luồng xử lý:
  1. Nếu `sessionId` trống → tạo bản ghi mới trong `chat_sessions` (title rút gọn từ tin nhắn đầu).
  2. Lưu tin nhắn người dùng vào `chat_messages` (role=`user`) qua Dapper.
  3. Gọi `POST http://ai-service:8000/rag/stream` với `HttpCompletionOption.ResponseHeadersRead`.
  4. Pipe từng dòng SSE (`data: {...}`) từ ai-service về React ngay thời gian thực (flush mỗi chunk).
  5. Gom toàn bộ nội dung assistant từ các sự kiện `token`.
  6. Khi stream kết thúc (`done`), lưu tin nhắn assistant hoàn chỉnh + citations vào `chat_messages` (role=`assistant`, metadata JSON chứa sources).
  7. Trả về `sessionId` để client theo dõi phiên.

### 3.3 Endpoint `GET /api/chat/history/{sessionId}`
- Truy xuất danh sách `chat_messages` của phiên, sắp xếp theo `created_at` ASC.
- Trả về JSON mảng `{ id, role, content, metadata, created_at }`.

### 3.4 Endpoint `GET /api/suggestions`
- Trả về 4 câu hỏi gợi ý mẫu cố định (ví dụ: "Điểm chuẩn ngành Công nghệ thông tin 2024 là bao nhiêu?", "Học phí ngành Kinh doanh năm nay thế nào?", "Phương thức xét tuyển cho ngành Ngôn ngữ Anh như thế nào?", "Lịch trình tuyển sinh 2026 bắt đầu khi nào?").

### 3.5 Kiểm tra điểm mốc Phase 3
- [ ] `POST /api/chat/stream` lưu user message, proxy SSE real-time, lưu assistant message sau khi xong.
- [ ] `GET /api/chat/history/{sessionId}` trả về lịch sử đúng thứ tự.
- [ ] `GET /api/suggestions` trả về 4 câu hỏi.
- [ ] Dapper parameterized queries, không dùng string concat SQL.

---

## Phase 4: React 19 UI (Vite + TypeScript + TailwindCSS)

**Mục tiêu:** Xây dựng giao diện chatbot hiện đại, responsive, hỗ trợ Markdown và streaming mượt mà.

### 4.1 Cấu hình frontend-react
- Build tool: Vite.
- Framework: React 19 + TypeScript strict.
- Styling: TailwindCSS + PostCSS + Autoprefixer.
- Icon: `lucide-react`.
- Proxy dev: `/api` → `http://backend-dotnet:5000`.
- Port expose: `5173:80` (container chạy Nginx phục vụ static build).
- Dockerfile multi-stage: build với Node 20, serve với `nginx:alpine`.

### 4.2 Thành phần giao diện
- **Layout:** Responsive, sidebar trên desktop (danh sách phiên), full-screen trên mobile.
- **Welcome screen:** Hiển thị 4 nút Suggestion Chips (câu hỏi gợi ý nhanh) khi chưa có cuộc trò chuyện.
- **ChatWindow:** Vùng hiển thị tin nhắn, cuộn tự động xuống tin nhắn mới nhất.
- **MessageBubble:** Hiển thị nội dung user (màu xanh bên phải) và assistant (trắng bên trái), hỗ trợ render Markdown (bảng điểm chuẩn, danh sách ngành, in đậm, in nghiêng).
- **Streaming effect:** Hiệu ứng gõ chữ mượt mà, hiển thị con trỏ nhấp nháy khi bot đang trả lời.
- **CitationBadges:** Hiển thị thẻ nguồn trích dẫn (`Nguồn 1: Tên chunk — similar`) dưới mỗi câu trả lời của bot.
- **Input form:** Ô nhập liệu, nút Gửi, disable khi đang stream.

### 4.3 Hook `useChatStream`
- Sử dụng native `fetch` ReadableStream hoặc `@microsoft/fetch-event-source` để consume SSE từ `/api/chat/stream`.
- Quản lý trạng thái: `messages[]`, `isStreaming`, `currentSessionId`.
- Xử lý sự kiện SSE: `token` → cập nhật nội dung đang gõ; `citations` → gắn fuentes; `done` → kết thúc stream và lưu sessionId; `error` → hiển thị thông báo lỗi.

### 4.4 Kiểm tra điểm mốc Phase 4
- [ ] Giao diện responsive, render Markdown bảng/danh sách đúng.
- [ ] 4 suggestion chips hiển thị trên màn hình chào mừng.
- [ ] Streaming typing effect mượt mà, Citation Badges hiển thị dưới câu trả lời bot.
- [ ] `docker compose up --build` chạy toàn bộ 4 service, chat end-to-end hoạt động.

---

## Danh sách services trong `docker-compose.yml`

| Service | Image/Dockerfile | Port | Depends on | Lưu ý |
|---|---|---:|---|---|
| `postgres` | `pgvector/pgvector:pg16` | 5432 | — | Volume `pgdata`, mount `init.sql` |
| `ai-service` | `./ai-service` | 8000 | `postgres` healthy | Volume `bge-m3-cache`, `extra_hosts: host.docker.internal:host-gateway` |
| `backend-dotnet` | `./backend-dotnet` | 5000 | `postgres`, `ai-service` healthy | Gọi ai-service nội bộ Docker network |
| `frontend-react` | `./frontend-react` | 5173 | `backend-dotnet` healthy | Nginx serve static, proxy `/api` |

**Lệnh chạy:** `docker compose up --build` tại thư mục gốc.

---

## DevOps Scripts (Windows)

### `start-dev.bat`
- Kiểm tra Docker đang chạy (`docker info`), nếu chưa thì thông báo và dừng.
- Chạy `docker compose up --build` tại thư mục gốc để khởi tạo và khởi động toàn bộ 4 services.
- Hiển thị URL truy cập: Postgres (5432), AI Service (8000), Backend .NET (5000), Frontend (5173).
- Giữ terminal mở để theo dõi logs các container.

### `stop-dev.bat`
- Chạy `docker compose down` để dừng và xóa containers, networks (không xóa volume `pgdata` để giữ dữ liệu).
- Tùy chọn: `docker compose down -v` nếu muốn xóa sạch dữ liệu database (thông báo trước khi thực hiện).

## Tiêu chí hoàn thành (Definition of Done)

- [ ] Mọi file liệt kê trong PLAN đã được tạo theo đúng cấu trúc monorepo.
- [ ] Tuân thủ 100% `.kilocode/rules/architecture.md`.
- [ ] `docker compose up --build` khởi động 4 service, tất cả healthcheck pass.
- [ ] `start-dev.bat` / `stop-dev.bat` chạy đúng trên Windows PowerShell/CMD.
- [ ] Luồng chat end-to-end: React gửi tin → .NET lưu → FastAPI RAG + 9Router stream SSE → .NET pipe real-time → lưu câu trả lời bot → hiển thị citations.
- [ ] Dữ liệu seed có sẵn ≥12 ngành, điểm chuẩn 3 năm, học phí, học bổng, phương thức, lịch 2026.
- [ ] Guardrail anti-hallucination hoạt động đúng khi hỏi ngoài phạm vi tuyển sinh.
