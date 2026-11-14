# KILO CODE HARNESS RULES - ADMISSIONS CHATBOT AI

## 1. Tech Stack & Strict Boundaries
- **Frontend:** React 19 + Vite + TypeScript + TailwindCSS. Use `@microsoft/fetch-event-source` or native `fetch` ReadableStream to consume SSE.
- **Main Backend (Gateway & Business):** ASP.NET Core .NET 10 Web API. MUST use **Dapper** + `Npgsql` for all PostgreSQL queries (NO Entity Framework Core).
- **AI & RAG Service:** Python 3.11+ + FastAPI. MUST run `BAAI/bge-m3` (1024 dimensions) locally via `sentence-transformers` or `FlagEmbedding` for embeddings.
- **Database:** PostgreSQL 16 with `pgvector` extension enabled (`vector(1024)`).
- **LLM Gateway (9Router):** Connect using OpenAI-compatible SDK (`openai` Python package) with:
  - `base_url`: `http://host.docker.internal:20128/v1` (when running in Docker) or `http://localhost:20128/v1` (local)
  - `api_key`: `sk-57a432014c1cc01c-6juq1i-fe7dbfbd`
  - `model`: `chatbotai` (Primary/Fallback configured on 9Router pointing to `oc/deepseek-v4.1-flash-free`)

## 2. Data Flow (Architecture Option A)
1. React 19 sends `POST /api/chat/stream` (with `sessionId` and `message`) to **.NET 10 Backend**.
2. .NET 10 persists the user message into PostgreSQL (`chat_messages` table) via **Dapper**.
3. .NET 10 proxies the request to **Python FastAPI** (`POST http://ai-service:8000/rag/stream`) and streams SSE chunks back to React 19.
4. FastAPI embeds the query with `BAAI/bge-m3`, performs cosine similarity search (`<=>`) on `pgvector`, retrieves top-5 chunks + metadata citations, streams LLM tokens from `9Router`, and emits a final `[CITATIONS]` SSE event before `[DONE]`.
5. Once streaming finishes, .NET 10 saves the complete assistant response + citations into PostgreSQL via **Dapper**.

## 3. Strict Guardrails (Domain: Vietnam University Admissions)
- The bot is ONLY allowed to answer questions regarding University Admissions based on the retrieved context.
- If the context does not contain the answer or the user asks off-topic questions (weather, coding, unsupported majors, politics, etc.), the bot MUST strictly reply:
  *"Xin lỗi, hiện tại tôi chỉ hỗ trợ giải đáp các thông tin liên quan đến tuyển sinh của Nhà trường dựa trên dữ liệu chính thức. Bạn vui lòng đặt câu hỏi về ngành học, điểm chuẩn, học phí hoặc phương thức xét tuyển nhé!"*