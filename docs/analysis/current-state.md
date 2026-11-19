# Current-State Analysis — Trạng thái thực tế của code vs tài liệu

Nguồn sự thật: source code thực tế (đã đọc hết). Tài liệu là nguồn ý định. Khi hai bên lệch, ghi vào bảng sai lệch dưới đây.

## 1. Thành phần và việc đang làm (theo code thật)

| Thành phần | Vị trí | Việc đang làm (code thực) | Khớp mục tiêu architek chốt? |
|---|---|---|---|
| PostgreSQL 16 + pgvector | `database/init.sql`, compose service `db` | Extension `vector`; bảng platform (tenants, documents, knowledge_chunks, ingestion_jobs, chat_sessions, chat_messages); bảng domain (majors, admission_scores, tuition_scholarships, admission_methods); index HNSW cosine + B-tree + GIN; seed 2 tenant + dữ liệu tuyển sinh | Có |
| ai-service (Python FastAPI) | `ai-service/` | `GET /api/v1/health`, `/api/v1/ready`; `POST /api/v1/retrieval-search` (embed bge-m3 trong executor, pgvector cosine, whitelist filter + tenant_id, score 0..1); ingestion async endpoints; `POST /embed`; auth `X-Internal-Api-Key` + `X-Request-Id`. **Không gọi LLM, không prompt, không guardrail, không stream SSE trong main.py.** | Có (chưa hoàn toàn: xem sai lệch #1) |
| backend-dotnet (.NET 10) | `backend-dotnet/` | SignalR Hub `/hubs/chat` (`StreamChat`, structured → retrieval qua Python → guardrail → prompt → LLM stream 9Router → sự kiện `token\|citations\|done\|error` → lưu message); `GET /api/chat/history/{sessionId}` (kiểm tra chủ sở hữu session); `GET /api/suggestions`; Dapper + Npgsql; abstractions (IRetrievalClient, ILlmClient, IGuardrail, IPromptBuilder, IStructuredQueryService, ITenantContext); Polly retry cho Python | Có |
| frontend-react | `frontend-react/src/` | `useChatConnection` consume sự kiện `{"type":"token\|citations\|done\|error"}` qua SignalR; chỉ gọi `.NET` qua `/api` proxy; không gọi Python, không nối DB | Có |
| docker-compose | `docker-compose.yml` | 4 service; 5432/8000 không publish trong base (chỉ override dev); depends_on healthy; ai-service env không có LLM_*/GATEWAY_URL; backend-dotnet env có LLM_API_KEY/BASE_URL/MODEL | Có |

## 2. Sai lệch tài liệu ↔ code (đã xác nhận thật)

| # | Mục | Tài liệu nói | Code thật nói | Tình trạng |
|---|---|---|---|---|
| S1 | Python gọi LLM | architecture.md/PLAN: Python KHÔNG gọi LLM | `ai-service/app/rag_engine.py` vẫn chứa `stream_llm_tokens` (import openai), `GUARDRAIL_REFUSAL`, `SYSTEM_PROMPT_TEMPLATE`, `build_prompt` — **không được import bởi main.py**, là mã đã chết | **Cần xóa** (DONE: "không còn đường gọi LLM nào trong ai-service") |
| S2 | Python sở hữu chat | architecture.md: .NET sở hữu chat_sessions/messages | `ai-service/app/database.py` vẫn có `get_or_create_session`, `save_message`, `get_session_messages`, `check_session_owner` — **không được gọi bởi endpoint nào** | Mã đã chết, để lại hoặc dọn (không vi phạm runtime) |
| S3 | Seed data | README/PLAN: script ingestion riêng, idempotent content_hash | `scripts/seed.py` đã tồn tại (idempotent content_hash) GỌP với `ai-service/app/seed_data.py` (viết trực tiếp bảng domain, không qua ingestion pipeline) | `seed_data.py` là mã cũ không cần thiết |
| S4 | Healthcheck ai-service | Dockerfile HEALTHCHECK gọi `/health` | Endpoint thực tế là `/api/v1/health` | **Bug**: healthcheck sẽ fail |
| S5 | Tên service DB | PLAN 1.1: "service tên postgres" | Compose service tên `db`, hostname `db` | Sai lệch nhỏ trong văn bản PLAN |
| S6 | Port 5432/8000 | Base compose không publish (chỉ override dev) | `docker-compose.override.yml` publish 5432:5432, 8000:8000 | Đúng thiết kế, cần ghi vào README |
| S7 | Bí mật plaintext | architecture.md: key đọc từ .env, không plaintext repo | `.env` (lokal, gitignored) chứa `LLM_API_KEY=sk-57a432014c1cc01c-6juq1i-fe7dbfbd` thực tế | **R2**: key lộ trên disk, cần thu hồi + .env.example |
| S8 | SSE format | `{"type":"token\|citations\|done\|error"}` | .NET `StreamEvent` + React `useChatStream` đều dùng chuẩn này | Có |
| S9 | Health paths | README: /api/v1/health, /api/v1/ready, /health, :5173 | Code chính xác | Có |
| S10 | History IDOR | PLAN: kiểm tra chủ sở hữu | `HistoryController` + `ChatService.GetHistoryAsync` kiểm tra tenant_id | Có |

## 3. Code đã chết (dead code) cần xử lý

- `ai-service/app/rag_engine.py`: gọi LLM, prompt, guardrail — **xóa** (vi phạm DONE nếu còn lại).
- `ai-service/app/seed_data.py`: ghi trực tiếp bảng domain, không qua pipeline ingestion — không được gọi, có thể xóa hoặc bỏ qua.
- `ai-service/app/database.py`: các hàm chat (`get_or_create_session`, `save_message`, ...) không được gọi bởi route Python — không vi phạm runtime, để lại hoặc dọn sau.

## 4. Giả định chờ xác nhận (R4)

- A1 (Q1): Một tenant mỗi deployment, nhưng cột `tenant_id` nullable để mở rộng multi-tenant chung DB. (Chưa hỏi)
- A5 (Q5): 9Router chạy ở đâu khi thật, key mới đã cấp, model `chatbotai`. (Chưa hỏi — **chặn preflight LLM**)
- A8 (Q8): Người dùng cuối đã có đăng nhập? Ảnh hưởng thiết kế chống IDOR cho history. (Chưa hỏi)

## 5. Kết luận Bước 0

Code nghiệp vụ đã chạy đúng phân định: .NET làm nghiệp vụ (guardrail, prompt, LLM, SSE, lưu lịch sử, structured query), Python chỉ retrieval/embedding/ingestion. Các thay đổi lớn **không cần sửa code nghiệp vụ**. Công việc còn lại: (a) dọn mã gọi LLM trong ai-service (`rag_engine.py`), (b) viết lại tài liệu thành khuôn mẫu tái sử dụng, (c) thêm `.env.example`, quét bí mật, (d) dựng harness kiểm chứng L0–L6 chạy thật, (e) chạy preflight + verification thật.

Câu chặn cần trả lời trước khi sửa file: Q1, Q5, Q8 (xem phần Giả định).
