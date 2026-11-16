# Câu hỏi mở (OPEN_QUESTIONS.md)

Danh sách các điểm chưa rõ cần chủ sở hữu dự án quyết định. Các mục đã được áp dụng giả định an toàn trong code được đánh dấu `[GIẢ ĐỊNH]`.

1. **API key cũ** (`sk-...`) đã lộ trong lịch sử git — quy trình thu hồi (revoke) và cách quản lý `LLM_API_KEY` mới (`.env`, Secret Manager) là thế nào?
   - Trạng thái: **Chưa giải quyết**. Đã xóa plaintext khỏi 3 file tài liệu (`PLAN.md`, `architecture.md`, `README.md`) và `BASELINE.md`, nhưng git history vẫn chứa key cũ → cần thu hồi trên 9Router và xoay vòng key.

2. **`docker-compose.override.yml`** chưa tồn tại trong repo — ai tạo/maintain? Các profile dev cần ghi rõ trong PLAN hay README.
   - Trạng thái: **Đã tạo** file `docker-compose.override.yml` publish cổng 5432 và 8000 cho dev. Cần ghi rõ vào README/hướng dẫn vận hành.

3. **Xác thực người dùng cuối (JWT của React)** do hệ thống nào cấp? .NET tự issue hay có Identity server riêng?
   - Trạng thái: `[GIẢ ĐỊNH]`. Code hiện tại dùng cơ chế dev: header `Authorization: Bearer dev-token` hoặc `X-Tenant-Id` (xem `TenantContextProvider.cs`). Chỉ bật ngoài production, cần thay bằng JWT thực tế sau.

4. **`tenant_id` từ danh tính người dùng** ánh xạ như thế nào (claim JWT, bảng mapping)?
   - Trạng thái: `[GIẢ ĐỊNH]`. Đọc từ header `X-Tenant-Id` (hoặc `DEFAULT_TENANT_ID` từ env). Cần thay bằng mapping từ JWT claim sau khi có auth thực tế.

5. **Whitelist filter** của `POST /api/v1/retrieval/search` bao gồm chính xác các key nào? Filter `metadata` có hỗ trợ lồng nhau không?
   - Trạng thái: Đã định nghĩa whitelist `{tenant_id, doc_type, document_status, metadata}` trong `ai-service/app/database.py` (`FILTER_WHITELIST`). Filter `metadata` hỗ trợ lẳng cấp 1 (`kc.metadata ->> key = value`). Cần xác nhận mở rộng hay không.

6. **Script seed riêng** đặt ở thư mục nào (`scripts/seed/`) và chạy như thế nào trong compose (profile `seed`)?
   - Trạng thái: Đã tạo `scripts/seed.py`, chạy qua `docker compose exec ai-service python /app/seed.py` hoặc gọi API `/ingestion/documents`. Chưa có profile `seed` trong compose. Cần quyết định chạy tự động hay thủ công.

7. **HNSW vs IVFFlat**: benchmark chưa chạy, chưa chọn mặc định.
   - Trạng thái: **Chưa giải quyết**. Code dùng HNSW (`vector_cosine_ops`). Cần benchmark thực tế trước khi chính thức chọn.

8. **Row-Level Security theo `tenant_id`** có bật ngay hay để sau?
   - Trạng thái: **Chưa giải quyết**. Schema đã định sẵn cột `tenant_id` và khuyến khích RLS làm mục tùy chọn, nhưng chưa bật trong `init.sql`. Cần quyết định bật hay không.

9. **Kích thước batch embed mặc định** là bao nhiêu? Thời gian timeout cho `POST /api/v1/retrieval/search`?
   - Trạng thái: `[GIẢ ĐỊNH]`. `EMBEDDING_BATCH_SIZE=32` (env). Timeout cho gọi Python dùng Polly retry (3 lần, 500ms×n). Không có timeout cứng riêng cho search endpoint. Cần xác nhận con số production.

10. **9Router model `chatbotai`** có hỗ trợ streaming tokens thực sự không?
    - Trạng thái: **Chưa giải quyết**. Code `.NET` dùng `OpenAIClient` stream qua HTTP SSE (`LLM_BASE_URL`). Cần xác nhận 9Router hỗ trợ streaming real-time hay chỉ trả toàn bộ.
