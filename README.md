# AI Chatbot Tuyển Sinh — Monorepo

4 services: PostgreSQL 16 + pgvector, ai-service (FastAPI/bge-m3/9Router RAG), backend-dotnet (.NET 10/Dapper), frontend-react (React 19/Vite/Tailwind).

## Quick Start
```bash
docker compose up --build
```
- DB: `localhost:5432`
- AI service: `http://localhost:8000/health`
- Gateway: `http://localhost:5000/api/v1/health`
- Frontend: `http://localhost:5173`

## Kiểm tra
1. Mở frontend, gõ "Điểm chuẩn 2024".
2. Xem_STREAM SSE từ ai-service qua gateway.
3. Seed data load tự động qua `init.sql` + `seed_data.py`.

## Cấu trúc
Xem `architecture.md` (`.kilocode/rules/architecture.md`) và `PLAN.md`.
