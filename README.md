# AI Chatbot Tuyển Sinh — Monorepo

4 services: PostgreSQL 16 + pgvector, ai-service (Python FastAPI RAG Data Service: bge-m3 1024 chiều, ingestion, retrieval), backend-dotnet (.NET 10/Dapper Gateway & Orchestration), frontend-react (React 19/Vite/Tailwind).

## Quick Start
```bash
docker compose up --build
```
- DB (nội bộ, không expose ra host ngoài profile dev): service `postgres` (hostname `db`)
- AI service (nội bộ): `http://ai-service:8000/api/v1/health` và `/api/v1/ready` (khi dùng `docker-compose.override.yml`: `http://localhost:8000/...`)
- Gateway: `http://localhost:5000/health`
- Frontend: `http://localhost:5173`

## Kiểm tra
1. Mở frontend, gõ "Điểm chuẩn 2024".
2. Xem stream SSE từ gateway theo chuẩn `{"type":"token|citations|done|error"}`.
3. Seed data: nạp qua script ingestion riêng (idempotent theo `content_hash`), không tự sinh trong code Python.

## Cấu trúc
Xem `architecture.md` (`.kilocode/rules/architecture.md`), `PLAN.md`, và `docs/api-contract-python.md`.

## Môi trường
Sao chép `.env.example` thành `.env`, điền `LLM_API_KEY` (không commit secret vào repo).
