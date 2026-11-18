# CHANGELOG-PLAN.md — Bảng thay đổi PLAN (khuôn mẫu tái sử dụng)

Mỗi thay đổi lớn đi kèm lý do. Giữ nguyên những gì đã chạy: Dapper, history, suggestions, lệnh `docker compose up --build`, script `.bat`.

| # | Cũ | Mới | Lý do | Ảnh hưởng code |
|---|----|-----|-------|----------------|
| 1 | PLAN gọi Python là "AI & RAG Service" làm nhiều việc (embed, search, prompt, guardrail, LLM, SSE) | Python chỉ còn `/search`, `/ingest`, `/embed`, `/health`, `/ready`; .NET làm prompt, guardrail, gọi LLM, SSE | Tuân thủ ranh giới cứng: Python không gọi LLM, không dựng prompt, không quyết định guardrail, không stream SSE | Xóa `ai-service/app/rag_engine.py` (mã gọi LLM); `.NET` giữ `LlmClient`, `PromptBuilder`, `Guardrail`, `ChatService` stream SSE |
| 2 | `PLAN.md` ghi health `/health` cho Python; `README` ghi `/api/v1/health` | Nhất quán: `/api/v1/health` và `/api/v1/ready`; Dockerfile HEALTHCHECK trỏ `/api/v1/health` | Tránh sai lệch tài liệu ↔ code | Sửa `ai-service/Dockerfile` HEALTHCHECK |
| 3 | `PLAN.md` ghi service DB tên `postgres` | Service tên `db`, hostname `db` | Khớp `docker-compose.yml` và connection string | Chỉ sửa văn bản PLAN |
| 4 | `PLAN.md` không có migration phân chia plataforma/domain | Thêm `database/migrations/01_platform.sql`, `02_admissions_domain.sql`; schema cộng thêm, không sửa bảng nghiệp vụ | Cho phép gắn hệ thống mới mà không đụng bảng tuyển sinh | Tạo 2 file migration mới |
| 5 | `PLAN.md` không có `.env.example`, key plaintext trong tài liệu | Thêm `.env.example` (placeholder, không giá trị thật); thay key bằng `${LLM_API_KEY}` trong `architecture.md`; `.env` trong `.gitignore` | R2: bí mật, key cũ đã lộ cần thu hồi | Tạo `.env.example`; sửa 3 file tài liệu |
| 6 | `PLAN.md` không có harness kiểm chứng | Thêm Phase 5 + `scripts/verify.ps1` + `scripts/verify.sh` + `docs/verification/` | R1: chạy thật, bằng chứng cho mọi tuyên bố "đã xong" | Tạo script kiểm chứng L0–L6 |
| 7 | `PLAN.md` không có CHANGELOG | Thêm `docs/CHANGELOG-PLAN.md` | Theo dõi thay đổi và lý do | Tạo file này |
| 8 | `PLAN.md` checkbox đã tích ([x]) | Toàn bộ checkbox ở trạng thái chưa tích ([ ]) | Chỉ tích khi có bằng chứng chạy thật | Chỉ thay văn bản PLAN |

## Giả định chờ xác nhận (R4)

| # | Câu hỏi | Giả định đang dùng | Trạng thái |
|---|---|---|---|
| Q1 | Nhiều tenant dùng chung một PostgreSQL, hay mỗi khách một deployment? | Nhiều tenant chung một DB; cần xem xét Row-Level Security theo `tenant_id` | **Đã trả lời:** nhiều tenant chung DB |
| Q2 | Nguồn dữ liệu thật khi đưa vào hệ thống khác: PDF, Word, Excel, website, hay DB có sẵn? | PDF + DOCX + XLSX | Chờ trả lời |
| Q3 | Giữ seed dữ liệu giả cho môi trường demo, hay chỉ nạp tài liệu thật? | Giữ seed, gắn nhãn `is_demo=true`, tách khỏi luồng nghiệm thu retrieval | Chờ trả lời |
| Q4 | Xác thực service-to-service .NET → Python | API key trong header `X-Internal-Api-Key`, nạp từ env | Chờ trả lời |
| Q5 | 9Router chạy ở đâu khi triển khai thật (host, server, cloud)? Key mới đã cấp chưa? Model vẫn là `chatbotai`? | `http://host.docker.internal:20128/v1`, model `chatbotai` | **Chặn preflight LLM** — chờ trả lời |
| Q6 | Plan này chỉ cho dự án tuyển sinh, hay là template dùng chung để gắn vào hệ thống tiếp theo? | Template dùng chung, tuyển sinh là bản mẫu đầu tiên | Đã trả lời |
| Q7 | Hybrid search và rerank làm ngay hay để sau? | Để sau, chỉ thiết kế sẵn chỗ cắm trong contract | Đã trả lời |
| Q8 | Người dùng cuối đã có đăng nhập chưa? (ảnh hưởng chống IDOR cho history) | Dev auth qua header `X-Tenant-Id` / `Authorization` | Chờ trả lời |
