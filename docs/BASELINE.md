# BASELINE.md — Trạng thái hệ thống trước khi refactor (Giai đoạn 0)

## Thông tin chung
- Nhánh: `refactor/plan-b` (tách từ `main`, commit `4b7b31b init chatbot`)
- Ngày: 2026-11-16
- Môi trường: Windows PowerShell, Docker 29.9.3, Compose v5.7.0, .NET 10.0.404, Python 3.14.6, Node 26.12.1

## Các file đã sửa trước giai đoạn 0 (theo PLAN.md / architecture.md mới)
- `PLAN.md`: viết lại 4 Phase theo phương án B, đánh dấu `[ĐỔI]`/`[MỚI]`/`[BỎ]`.
- `.kilocode/rules/architecture.md`: luồng 12 bước, bảng ranh giới cứng, xóa key plaintext.
- `README.md`: đồng bộ URL health, luồng seed, sửa "Xem_STREAM".
- `docs/api-contract-python.md` (mới): hợp đồng `.NET ⇄ Python`.

## Docker compose (chưa chạy)
File `docker-compose.yml` hiện tại:
- `postgres` (hostname `db`): publish `5432:5432` (PHẢI BỎ trong override mặc định theo PLAN).
- `ai-service`: publish `8000:8000`, env có `LLM_BASE_URL`, `LLM_API_KEY` (plaintext `sk-...`), `GATEWAY_URL`, `extra_hosts host.docker.internal`.
- `backend-dotnet`: publish `5000:5000`, env `AI_SERVICE_URL=http://ai-service:8000`.
- `frontend-react`: publish `5173:80`.

## Code hiện tại (trước khi sửa theo Phase 1-4)

### ai-service (Python FastAPI) — LÀM QUÁ NHIỀU VIỆC (vi phạm ranh giới)
- `config.py`: có `LLM_BASE_URL`, `LLM_API_KEY` (plaintext đã lộ), `LLM_MODEL`, `GATEWAY_URL`.
- `requirements.txt`: có `openai==1.52.0`.
- `main.py`: endpoint `/health`, `/ingest/seed`, `/rag/stream` (dùng OpenAI AsyncClient stream tokens, emit SSE `token/citations/done`, gọi `seed()` trên startup).
- `rag_engine.py`: system prompt, `build_prompt`, `stream_llm_tokens` (gọi 9Router), `GUARDRAIL_REFUSAL` hard-code.
- `database.py`: `search_similar_chunks` (không có `tenant_id` filter), `upsert_knowledge_chunk` (không `tenant_id`), `get_or_create_session`, `save_message`, `get_session_messages`.
- `embeddings.py`: `SentenceTransformer` bge-m3.
- `seed_data.py`: sinh dữ liệu tuyển sinh giả lập trong code, embed knowledge_chunks.

### backend-dotnet (.NET 10)
- `Program.cs`: Dapper + Npgsql, `AI_SERVICE_URL`, CORS, `/health`.
- `Controllers/ChatController.cs`: `POST /api/chat/stream`, nhận `userId` từ client, proxy SSE từ ai-service `/rag/stream`.
- `Controllers/HistoryController.cs`: `GET /api/chat/history/{sessionId}` (không kiểm tra quyền sở hữu).
- `Controllers/SuggestionsController.cs`: `GET /api/suggestions` (4 câu cố định).
- `Services/ChatService.cs`: gọi `ai-service:8000/rag/stream`, lưu user/assistant message, Dapper parameterized.
- `Services/HistoryService.cs`: lấy lịch sử, không filter theo user/tenant.

### frontend-react (React 19)
- `src/hooks/useChatStream.ts`: gọi `/api/chat/stream` với `userId: "web-user"`, consume SSE `token/citations/done/error`.
- `src/App.tsx`: suggestion chips, gửi `sessionId` + `userId`.
- `tests/admissions.e2e.ts`: gọi trực tiếp `http://localhost:8000/health` (vi phạm ranh giới frontend không gọi Python).
- `tests/api-connectivity.e2e.ts`: gọi `/api/suggestions`.

## Kết quả chạy `docker compose up --build -d` (baseline)
Tất cả 4 container healthy:
- `db` (pgvector/pgvector:pg16): healthy, port 5432 publish
- `ai-service`: healthy, port 8000 publish
- `backend-dotnet`: healthy, port 5000 publish
- `frontend-react`: healthy, port 5173 publish

## Endpoint kiểm tra (baseline)
- `GET http://localhost:5000/health` → `{"status":"ok","service":"backend-dotnet"}` ✓
- `GET http://localhost:8000/health` → `{"status":"ok","service":"ai-service"}` ✓
- `GET http://localhost:5173/` → HTML frontend ✓
- `POST http://localhost:8000/ingest/seed` → `{"status":"ok","message":"Seed data loaded and embedded into pgvector."}` ✓
- `POST http://localhost:5000/api/chat/stream` → SSE `token` events stream từ 9Router qua Python ✓ (ví dụ: `data: {"type":"token","content":"The",...}`)

## Hệ thống chạy được gì / hỏng gì (trước sửa)
- Chạy được: toàn bộ stack 4 service, health check, seed, chat stream end-to-end qua Python→9Router.
- Hỏng / vi phạm ranh giới (theo PLAN mới):
  - Python gọi LLM qua `openai`, stream SSE, dựng prompt (phải thuộc .NET).
  - Python có `LLM_API_KEY` plaintext trong code và docker-compose.
  - Frontend test gọi trực tiếp cổng 8000 (vi phạm ranh giới).
  - `.NET` không có guardrail, không có structured query, không có abstractions.
  - Không có `tenant_id` trong schema `knowledge_chunks`, `chat_sessions`.
  - SSE format hiện tại `{"type":"token","content":...,"sources":null,...}` — .NET passthrough, chưa chuẩn `token|citations|done|error` rõ ràng.
  - `history/{sessionId}` không kiểm tra quyền sở hữu (IDOR).

## Ghi chú
- Key cũ (đã lộ trong repo/lịch sử git) → cần thu hồi.
- Ports 5432 và 8000 đang publish ra host trong docker-compose mặc định (phải bỏ, chỉ publish qua override dev).
