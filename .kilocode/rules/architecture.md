# KILO CODE HARNESS RULES - ADMISSIONS CHATBOT AI (Khuôn mẫu tái sử dụng)

## 1. Tech Stack & Strict Boundaries
- **Frontend:** React 19 + Vite + TypeScript + TailwindCSS. Dùng `@microsoft/signalr` để consume realtime stream từ ASP.NET Core SignalR. Chỉ gọi .NET, không gọi Python, không kết nối DB.
- **Main Backend (Gateway & Orchestration):** ASP.NET Core .NET 10 Web API. BẮT BUỘC dùng **Dapper** + `Npgsql` cho tất cả truy vấn PostgreSQL (KHÔNG Entity Framework Core). Sở hữu xác thực, phân quyền, tenant scope, session & history, structured query, guardrail, dựng prompt, gọi LLM qua 9Router, stream realtime qua SignalR (Hub `ChatHub` tại `/hubs/chat`).
- **AI & RAG Service (Python FastAPI):** Chỉ làm dữ liệu RAG: embedding, ingestion, retrieval. BẮT BUỘC chạy `BAAI/bge-m3` (1024 chiều) cục bộ qua `sentence-transformers` hoặc `FlagEmbedding`. **Không gọi LLM, không dựng prompt, không quyết định guardrail, không stream realtime.**
- **Database:** PostgreSQL 16 với extension `pgvector` (`vector(1024)`).
- **LLM Gateway (9Router):** Kết nối bằng OpenAI-compatible SDK (`openai` package) từ **.NET** với:
  - `base_url`: `http://host.docker.internal:20128/v1` (chạy trong Docker) hoặc `http://localhost:20128/v1` (local)
  - `api_key`: `${LLM_API_KEY}` đọc từ `.env` (không lưu plaintext trong repo)
  - `model`: `chatbotai` (Primary/Fallback được cấu hình trên 9Router)

## 2. Data Flow (Phương án B — .NET Core Backend + Python RAG Service)

1. React 19 kết nối SignalR Hub `/hubs/chat`, gọi method server `StreamChat({ sessionId?, message })`.
2. .NET xác thực người dùng từ JWT (access_token đọc từ query string chỉ cho `/hubs/chat` qua `JwtBearerEvents.OnMessageReceived`), xác định `tenant_id`/`userId` từ claims; **không tin `tenant_id`/`userId` do client gửi.**
3. .NET tạo/kiểm tra quyền sở hữu session, lưu tin nhắn user vào `chat_messages` qua Dapper.
4. Nếu câu hỏi là số liệu (điểm chuẩn, học phí, v.v.): .NET truy vấn structured qua Dapper + Npgsql trên bảng domain.
5. .NET gọi `POST http://ai-service:8000/api/v1/retrieval/search` với `tenant_id` + filters, nhận top-k chunks kèm score và metadata.
6. .NET guardrail: nếu không có chunk vượt `min_score` và không có kết quả structured → trả câu từ chối cố định (cấu hình theo tenant), **không gọi LLM.**
7. .NET dựng prompt từ context + kết quả structured.
8. .NET gọi LLM qua 9Router (OpenAI-compatible SDK), stream tokens.
9. .NET yield `ChatEvent` qua Hub theo chuẩn `{"type":"token|citations|done|error"}`, có `CancellationToken` khi client ngắt kết nối.
10. Python nhiệm embed câu hỏi, truy vấn pgvector cosine `<=>`, áp filter, trả kết quả; Python **không** gọi LLM.
11. Khi stream xong, .NET lưu assistant message + citations vào PostgreSQL qua Dapper.
12. React render stream và citations.

## 3. Strict Guardrails (Domain: Vietnam University Admissions)
- Bot chỉ được trả lời về tuyển sinh dựa trên context đã truy xuất.
- Nếu context không có câu trả lời hoặc người dùng hỏi ngoài phạm vi tuyển sinh (thời tiết, code, chuyên ngành không hỗ trợ, chính trị...), bot BẮT BUỘC trả lời:
  *"Xin lỗi, hiện tại tôi chỉ hỗ trợ giải đáp các thông tin liên quan đến tuyển sinh của Nhà trường dựa trên dữ liệu chính thức. Bạn vui lòng đặt câu hỏi về ngành học, điểm chuẩn, học phí hoặc phương thức xét tuyển nhé!"*
- Câu từ chối thuộc về .NET, cấu hình theo tenant (không hard-code). Tên miền ("Nhà trường") cấu hình theo tenant.
- Structured Query là luồng chính cho số liệu chính thức; Semantic Retrieval chỉ bổ trợ. Không dùng similarity search thay cho truy vấn số liệu.

## 4. Ranh giới cứng

| Việc | Thuộc về | Cấm (không được làm) |
|---|---|---|
| Embedding, ingestion, chunking, re-embed, theo dõi job, quản lý phiên bản embedding model | Python | Python gọi LLM; Python dựng prompt; Python quyết định guardrail; Python stream realtime |
| Retrieval `POST /api/v1/retrieval/search` (embed query, filter, pgvector, top-k + score) | Python | Python tự quyết định quyền truy cập / mở rộng filter ngoài whitelist |
| Xác thực, phân quyền, tenant scope, session & history | .NET | .NET tự truy vấn bảng vector (`knowledge_chunks`) |
| Structured query (điểm chuẩn, học phí, học bổng, phương thức) qua Dapper+Npgsql | .NET | Frontend gọi Python; frontend kết nối DB |
| Guardrail, dựng prompt, gọi LLM qua 9Router, stream realtime qua SignalR | .NET | Tin `tenant_id`/`userId` gửi từ client |

- **API key:** `${LLM_API_KEY}` đọc từ `.env`, không ghi plaintext vào repo. Key cũ đã lộ trong lịch sử repo → **cần thu hồi ngay.**

## 5. Mở rộng sau (không trong Definition of Done)
Hybrid search, rerank, query expansion chỉ ghi ở mức ý định, không đưa vào phạm vi hiện tại. Không thêm công nghệ mới (Redis, message queue, v.v.) nếu không có trong danh sách tech stack. Redis backplane / Azure SignalR Service chỉ được xem xét khi cần scale nhiều instance SignalR, và nằm trong mục này.
