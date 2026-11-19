# FIX_REPORT.md — Khắc phục phát hiện từ AUDIT_REPORT.md

Nguồn sự thật: `PLAN.md` và `.kilocode/rules/architecture.md`. Mỗi phát hiện đã được đọc code thật, xác nhận đúng/sa, sửa tối thziale, rồi chứng minh bằng test/lệnh chạy thật. Mỗi nhóm sửa một commit (thông điệp nêu mã phát hiện F1, I4, J1…).

## 1. Bảng khắc phục

| Mã phát hiện | Xác minh | Hành động | File đã sửa | Bằng chứng (test / lệnh) |
|---|---|---|---|---|
| **I4 — Critical** | ĐÚNG. Không có test project .NET (`backend-dotnet.Tests` chỉ có `obj/`, không có `.csproj`); frontend không có `vitest.config.ts`/file test; e2e SignalR bị chú thích. | Tạo test project xUnit (có `.csproj`, tham chiếu `BackendDotnet.csproj`); viết 8 test .NET; thêm `vitest.config.ts` + 3 test hook `useChatConnection`; port e2e `chat.test.ts` sang client `@microsoft/signalr` trong Node và mở lại các kịch bản. | `backend-dotnet.Tests/backend-dotnet.Tests.csproj` (mới)<br>`backend-dotnet.Tests/ChatOrchestratorTests.cs` (mới)<br>`backend-dotnet.Tests/GuardrailTests.cs` (mới)<br>`backend-dotnet.Tests/TenantContextProviderTests.cs` (mới)<br>`backend-dotnet.Tests/ChatHubTests.cs` (mới)<br>`frontend-react/vitest.config.ts` (mới)<br>`frontend-react/src/hooks/useChatConnection.test.ts` (mới)<br>`tests/e2e/tests/api/chat.test.ts` (sửa)<br>`tests/e2e/README.md` (sửa)<br>`tests/e2e/package.json` (thêm `@microsoft/signalr`) | `dotnet test backend-dotnet.Tests` → **18 passed, 0 failed**.<br>`npm run test` (frontend) → **3 passed, 0 failed**.<br>E2E chat.test.ts: 4 kịch bản SignalR (full chat, ngoài phạm vi/không gọi LLM, dừng giữa chừng, chưa xác thực), đánh dấu `skip` khi stack không chạy. |
| **F1 — Medium** | ĐÚNG. `Guardrail.cs:13-15` chứa chuỗi từ chối hard-coded làm fallback khi `GUARDRAIL_REFUSAL_MESSAGE` thiếu. | Bỏ hard-coded fallback; ném `InvalidOperationException` (fail fast) khi thiếu config. Ghi nhận: config hiện là env var toàn cục, chưa đọc từ bảng `tenants.config` theo tenant (xem mục "cần quyết định"). | `backend-dotnet/Services/Guardrail.cs` | `GuardrailTests.Constructor_Throws_WhenRefusalMessageMissing` x pass.<br>`dotnet build` 0 warning/0 error. |
| **J1 — Medium/High** | ĐÚNG. `ChatOrchestrator` dùng `static ConcurrentDictionary<string, SemaphoreSlim>`, entry chưa bao giờ bị xóa → rò rỉ bộ nhớ theo số session; giới hạn 1 stream/session chỉ đúng trong 1 instance. | Thay bằng `ConcurrentDictionary<string, object>` dùng `TryAdd`/`TryRemove` (nguyên tử, chỉ từ chối stream thứ hai, không xếp hàng); xóa entry trong `finally` (sau hoàn thành/hủy/lỗi). Không dùng Redis. Ghi vào PLAN.md rằng giới hạn này chỉ đúng trên 1 instance; nhiều instance cần backplane/khóa phân tán. | `backend-dotnet/Services/ChatOrchestrator.cs`<br>`PLAN.md` | `ChatOrchestratorTests.ConcurrentStreams_SameSession_ExactlyOneRejectedWithError` pass (1/2 luồng bị lỗi "đang chạy").<br>`ChatOrchestratorTests.StreamCompletes_SessionEntryRemoved_CanStreamAgain` pass (entry được dọn, stream lại cùng session thành công). |
| **J3 — High (ranh giới)** | ĐÚNG. `ai-service/app/database.py:280-332` định nghĩa `get_or_create_session`, `save_message`, `get_session_messages`, `check_session_owner` thao tác `chat_sessions`/`chat_messages` (thuộc .NET); `main.py` import nhưng không gọi endpoint nào. | Xóa 4 hàm chat trong `database.py` và các import tương ứng trong `main.py`. Thêm kiểm tra grep trong `verify.ps1` (L0) để cảnh báo nếu Python tham chiếu `chat_sessions`/`chat_messages`. | `ai-service/app/database.py`<br>`ai-service/app/main.py`<br>`scripts/verify.ps1` | `grep chat_sessions\|chat_messages` trên `ai-service/` → **không còn match**.<br>`verify.ps1` L0 check mới: `ai-service không tham chiếu bảng chat`. |
| **J4 — Medium** | ĐÚNG. `Guardrail` mặc định `_minScore = 0.45`; `ChatOrchestrator` gọi retrieval với `0.3` hard-coded. | Đưa cả hai thành cấu hình có tên rõ ràng, một nguồn duy nhất cho mỗi ngưỡng, không còn số hard-code trong orchestrator: `RETRIEVAL_MIN_SCORE` (mặc định 0.3, lưới rộng) và `GUARDRAIL_MIN_SCORE` (mặc định 0.45, ngặt). Ghi ý nghĩa từng ngưỡng vào PLAN.md. | `backend-dotnet/Services/ChatOrchestrator.cs`<br>`PLAN.md` | `ChatOrchestratorTests.RetrievalUsesConfiguredMinScore_NotHardcoded03` pass (gọi với 0.7, không phải 0.3). |
| **J5 — Medium** | ĐÚNG. `ChatOrchestrator.cs:123` trả `$"Lỗi truy xuất ngữ cảnh: {retrievalError}"` có thể lộ chi tiết nội bộ. | Thay bằng thông điệp an toàn cố định cho client; log chi tiết lỗi server-side (không log token/key). | `backend-dotnet/Services/ChatOrchestrator.cs` | `ChatOrchestratorTests.RetrievalError_ReturnsSafeMessage_NotRawError` pass (sự kiện `error` không chứa chuỗi lỗi gốc). |
| **H/A4 — Low** | ĐÚNG. `scripts/verify.ps1` L4/L5 gọi `POST /api/chat/stream` (SSE endpoint đã xóa) → script hỏng. | Thay L4 bằng chạy e2e SignalR `tests/e2e/tests/api/chat.test.ts` qua playwright khi stack sẵn sàng; L5 trỏ về `dotnet test` (L0) + e2e. Thêm `dotnet test` vào L0. Sửa nhắc "error SSE" thành "event error". | `scripts/verify.ps1` | L0 chạy `dotnet test` xanh (18 passed). |
| **C12 — Unverified** | KHÔNG CẦN SỬA. `frontend-react/nginx.conf` đã cấu hình `/hubs/chat` với `Upgrade $http_upgrade`, `Connection "upgrade"`, `proxy_buffering off` → WebSocket qua reverse proxy đã đúng. | Ghi nhận là đã áp dụng, không cần thay đổi code. | — | `nginx.conf` đã có header Upgrade/Connection + `proxy_buffering off` (xem file). |

## 2. Kết quả chạy thật (tổng hợp)

- `dotnet build backend-dotnet` → **0 warning, 0 error**.
- `dotnet build backend-dotnet.Tests` → **0 warning, 0 error**.
- `dotnet test backend-dotnet.Tests` → **18 passed, 0 failed**.
- `npm run build` (frontend) → thành công, `dist/` sinh ra.
- `npm run test` (frontend vitest) → **3 passed, 0 failed**.
- Grep `text/event-stream\|EventSource\|fetchEventSource\|/api/chat/stream` trên `backend-dotnet/` và `frontend-react/` → **không còn** (chỉ còn ở docs/audit lịch sử và test fixture 9Router hợp pháp).
- Grep `chat_sessions\|chat_messages` trên `ai-service/` → **không còn** (J3 dọn sạch).
- Quét key plaintext (`sk-`, `password=`, `Bearer `) trong file tracked (`PLAN.md`, `architecture.md`, `README.md`, `docker-compose.yml`) → **không có khóa thực** (`.env` là gitignored, không tracked).

## 3. Việc còn lại / cần người dùng quyết định

1. **F1 — Cấu hình từ chối chưa thực sự theo tenant.** Code hiện đọc `GUARDRAIL_REFUSAL_MESSAGE` từ env var toàn cục, fail fast nếu thiếu. Plan yêu cầu câu từ chối + tên miền ("Nhà trường") **theo tenant** (bảng `tenants.config jsonb` đã có trong `database/init.sql`, nhưng code chưa đọc). Đọc từ DB qua Dapper không cần thêm schema/migration (bảng đã có), nhưng là thay đổi hành vi cấu hình. **Cần người dùng xác định** có bật đọc từ DB ngay hay để làm bước sau.
2. **I5 — Chưa chạy stack đầy đủ.** `docker compose up --build`, tải model `BAAI/bge-m3` (~1GB), gọi 9Router thật, migration, xóa volume là tác vụ cần sự đồng ý của người dùng (không tự chạy). Lệnh và kết quả kỳ vọng liệt kê ở mục 4.
3. **Key cũ đã lộ trong lịch sử git** (`sk-57a432014c1cc01c-68jk6c-d3cdfdbd` trong `.env` lokal, gitignored) — quy trình thu hồi (revoke) là ngoài scope sprint này, đã ghi nhớ (architecture.md:45).

## 4. Lệnh để người dùng tự chạy (cần đồng ý)

```bash
# 1. Khởi động stack đầy đủ
docker compose up --build -d

# 2. (Tuỳ chọn) fake-llm + publish cổng 8000 cho e2e
docker compose -f docker-compose.test.yml up -d

# 3. Đợi /health và /ready xanh, rồi chạy:
dotnet test backend-dotnet.Tests\backend-dotnet.Tests.csproj -c Release
cd frontend-react && npm run test
cd tests/e2e && npm install && npx playwright install chromium && npx playwright test --project api
```

Kết quả kỳ vọng: `dotnet test` 18 passed; `npm run test` 3 passed; e2e SignalR chat.test.ts 4 kịch bản xanh khi stack + fake-llm đang chạy.

## 5. Chưa kiểm chứng được (ly do)

- **Luồng chat end-to-end thật qua SignalR + LLM thật (9Router + model bge-m3)** — cần `docker compose up --build` + tải model + 9Router thật; tác vụ đánh dấu "không tự chạy" (xem mục 4).
- **E2E test thực tế (`playwright test --project api`)** — cần stack + Playwright browser (chromium) đã cài; đánh dấu `skip` trong test khi stack không chạy.
- **C12 WebSocket qua reverse proxy production** — `nginx.conf` đã cấu hình đúng (`Upgrade`/`Connection`, `proxy_buffering off`), nhưng chưa chạy thật trên production để xác nhận không downgrade long polling.

---
Tất cả sửa đổi đã được commit theo nhóm (mỗi commit có mã phát hiện trong thông điệp). Xem `git log` để tra cứu.
