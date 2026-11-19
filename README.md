# AI Chatbot Tuyển Sinh — Monorepo (khuôn mẫu tái sử dụng)

4 services: PostgreSQL 16 + pgvector, ai-service (Python FastAPI RAG Data Service: bge-m3 1024 chiều, ingestion, retrieval), backend-dotnet (.NET 10/Dapper Gateway & Orchestration), frontend-react (React 19/Vite/Tailwind).

Python là dịch vụ nền tảng (embedding, ingestion, retrieval) không biết gì về nghiệp vụ. .NET giữ toàn bộ nghiệp vụ (xác thực, phân quyền, tenant scope, session, lịch sử, truy vấn có cấu trúc, guardrail, prompt, gọi LLM, stream realtime qua SignalR, citations).

## Quick Start
```bash
# Sao chép env mẫu, điền giá trị thật (không commit secret)
cp .env.example .env
# Chỉnh LLM_API_KEY (key thật, che sk-****), LLM_BASE_URL, LLM_MODEL nếu cần

docker compose up --build
```
- DB (nội bộ, không expose ra host ngoài profile dev): service `db` (hostname `db`)
- AI service (nội bộ): `http://ai-service:8000/api/v1/health` và `/api/v1/ready` (khi dùng `docker-compose.override.yml`: `http://localhost:8000/...`)
- Gateway: `http://localhost:5000/health`
- Frontend: `http://localhost:5173`

## Kiểm tra
1. Mở frontend, gõ "Điểm chuẩn 2024".
2. Xem stream realtime từ gateway qua SignalR theo chuẩn `{"type":"token|citations|done|error"}`.
3. Seed data: nạp qua script ingestion riêng (`scripts/seed.py`), idempotent theo `content_hash`, không tự sinh trong code Python.

## Cấu trúc
Xem `architecture.md` (`.kilocode/rules/architecture.md`), `PLAN.md`, và `docs/api-contract-python.md`.

## Môi trường
Sao chép `.env.example` thành `.env`, điền `LLM_API_KEY` (không commit secret vào repo).

## Kiểm chứng chạy thật
Chạy `scripts/verify.ps1` (Windows) hoặc `scripts/verify.sh` để kiểm chứng L0–L6 trên stack thật, báo cáo bằng chứng vào `docs/verification/`.
