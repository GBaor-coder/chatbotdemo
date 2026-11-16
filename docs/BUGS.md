# Danh sách bug (BUGS.md)

## Đã giải quyết

| ID | Mức độ | Mô tả | Nguyên nhân gốc | Cách sửa | Test chặn tái phát | Trạng thái |
|---|---|---|---|---|---|---|
| BUG-01 | P2 | Guardrail hardcode `min_score = 0.3` thay vì đọc từ config tenant | Hardcode trong `Guardrail.ShouldRefuse` | Đọc `GUARDRAIL_MIN_SCORE` từ config, mặc định 0.45 | `guardrail: off-topic question returns refusal and LLM not called` | Đã sửa |
| BUG-02 | P2 | `GET /api/chat/history/{sessionId}` lỗi 500 do Dapper không cast UUID sang string | Dapper ép Guid → string fail | Thêm `CAST(... AS text)` trong SQL | `GET /api/chat/history/{sessionId} returns messages in ASC order` | Đã sửa |
| BUG-03 | P2 | Python `/api/v1/retrieval/search` lỗi 422 trả định dạng `detail` của FastAPI thay vì cấu trúc thống nhất | Ném `HTTPException` thay vì trả `JSONResponse` | Trả `JSONResponse` với `{success:false, error:{code,message}, request_id}` | `missing tenant_id returns 422`, `filter outside whitelist returns 422` | Đã sửa |
| BUG-04 | P2 | `top_k` có giới hạn `le=20` trong Pydantic gây lỗi validation khi gửi > 20 | Pydantic field constraint | Gỡ `le=20`, clamp trong code Python (tối đa 20) | `top_k above max is clamped to 20` | Đã sửa |
| BUG-05 | P1 | `.NET` không kiểm tra xác thực trên `POST /api/chat/stream` → cho phép request không token | Thiếu kiểm tra `ITenantContext` trong controller | Trả 401 nếu `TenantId` rỗng | `security: no token returns 401` | Đã sửa |
| BUG-06 | P2 | Fake-llm endpoint `/__reset` trả 404 do chỉ xử lý GET | `do_POST` không xử lý `/__reset` | Thêm xử lý POST `/__reset` trong fake-llm server | `guardrail: ... LLM not called` (counter reset deterministik) | Đã sửa |

## Chưa giải quyết

Không có.
