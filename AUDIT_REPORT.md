# AUDIT_REPORT.md — Kiểm duyệt độc lập: Admissions Chatbot AI

**Tiêu chí đối chiếu:** `PLAN.md` (gốc sự thật) và `.kilocode/rules/architecture.md` (harness rules).  
**Thay đổi cần kiểm tra:** realtime streaming chuyển từ SSE sang ASP.NET Core SignalR, giữ nguyên mọi quy tắc còn lại.  
**Quy tắc:** không sửa code; chỉ tạo file này. Mỗi kết luận dựa trên code/cấu hình/lệnh tự chạy. Không tin lời kể agent trước.

---

## 1. Kết luận (1 đoạn)

Hệ thống **ĐÚNG MỘT PHẦN** so với plan. Việc chuyển stream từ SSE sang SignalR (`ChatHub` tại `/hubs/chat`, `IAsyncEnumerable<ChatEvent>`, `CancellationToken`) đã được thực hiện đúng và nền tảng ranh giới (.NET nhận tenant từ claims, Dapper+Npgsql, Python chỉ RAG, frontend chỉ gọi .NET) được giữ nguyên. Tuy nhiên, còn 4 vấn đề quan trọng cần xử lý trước khi coi là hoàn tất:

1. **Thiếu kiểm thử tự động (I4 — FAIL):** Không tồn tại unit test .NET (`backend-dotnet.Tests` rỗng, không có `.csproj`), không có vitest/test file trong `frontend-react/src`, thiếu test guardrail không gọi LLM, hub từ chối kết nối chưa xác thực, tenant lấy từ claims, hủy stream.
2. **Guardrail có câu từ chối hard-coded làm fallback (F1 — PARTIAL):** `Guardrail.cs:13-15` chứa chuỗi từ chối cứng trong code khi `GUARDRAIL_REFUSAL_MESSAGE` thiếu, vi phạm "câu từ chối cấu hình theo tenant, không hard-code".
3. **CancellationToken không truyền xuống Dapper cho structured query (C8 — PARTIAL):** `AdmissionStructuredQueryService.QueryAsync` nhận `ct` nhưng không truyền vào `conn.QueryAsync`, vi phạm yêu cầu "thực sự truyền xuống các truy vấn Dapper".
4. **Python ai-service bleed ranh giới chat (J):** `ai-service/app/database.py:280-332` định nghĩa `save_message`, `get_session_messages`, `check_session_owner`, `get_or_create_session` thao tác bảng `chat_sessions`/`chat_messages` (rãnh giới của .NET), mặc dù chưa có endpoint gọi các hàm này.

---

## 2. Bảng tóm tắt

| Mục | Trạng thái | Ghi chú ngắn |
|-----|-----------|--------------|
| A1 | PASS | SSE chỉ còn nhắc lịch sử (PLAN.md:177), không dùng làm cơ chế stream hiện hành |
| A2 | PASS | Bảng ranh giới §4 đủ; Python cấm stream realtime (architecture.md:39, 47) |
| A3 | PASS | Redis backplane / Azure SignalR nằm mục "Mở rộng sau" (architecture.md:47-48) |
| A4 | PASS | Không mâu thuẫn giữa các mục; PLAN.md:242 & scripts/verify.ps1 tham chiếu endpoint SSE cũ là ghi chú lịch sử |
| B1 | PASS | Chỉ Dapper+Npgsql (csproj), không EF; truy vấn tham số hóa |
| B2 | PASS | Python không import `openai`, không LLM/prompt/guardrail/stream |
| B3 | PASS | .NET không truy vấn `knowledge_chunks`; vector qua `POST /api/v1/retrieval/search` |
| B4 | PASS | Frontend proxy `/api` + `/hubs/chat` đến localhost:5000; không gọi Python/DB |
| B5 | PASS | `StreamChatRequest` chỉ `sessionId?` + `message`; tenant/user từ `Context.User` claims |
| B6 | PASS | Không công nghệ ngoài danh sách; ngoại lệ duy nhất `@microsoft/signalr` |
| C1 | PASS | `ChatHub` có `[Authorize]`, map `/hubs/chat` (Program.cs:109) |
| C2 | PASS | `StreamChat` trả `IAsyncEnumerable<ChatEvent>`, có `[EnumeratorCancellation] CancellationToken` |
| C3 | PASS | `StreamChatRequest` chỉ `sessionId?`, `message` |
| C4 | PASS | Hub mỏng, nghiệp vụ trong `IChatOrchestrator` |
| C5 | PASS | JWT query string chỉ cho `/hubs/chat` qua `OnMessageReceived` (Program.cs:35) |
| C6 | PASS | `tenant_id`/`userId` từ `Context.User` claims (ChatHub.cs:25-30) |
| C7 | PASS | Validate độ dài message, quyền sở hữu session, giới hạn 1 stream/session (semaphore) |
| C8 | PARTIAL | `ct` truyền xuống retrieval & LLM, nhưng **không** truyền xuống Dapper trong structured query |
| C9 | PASS | Lỗi bắt trả event `error`, không lộ stack trace; `EnableDetailedErrors = IsDevelopment` |
| C10 | PASS | CORS origin cụ thể + `AllowCredentials`, không dùng `*` |
| C11 | PASS | KeepAliveInterval 15s, ClientTimeoutInterval 30s, MaxReceiveMessageSize 1MB |
| C12 | UNVERIFIED | Cấu hình reverse proxy/nginx WebSocket upgrade chưa kiểm tra (chưa inspect Dockerfile frontend) |
| D1 | PASS | Dùng `@microsoft/signalr`, `HubConnection`, `accessTokenFactory`, `withAutomaticReconnect` |
| D2 | PASS | `connection.stream(...).subscribe()`; nút Dừng gọi `subscription.dispose()` |
| D3 | PASS | `ChatEvent` frontend khớp backend (`token\|citations\|done\|error`) |
| D4 | PASS | Xử lý stream đứt: báo lỗi, `isStreaming=false`, người dùng có thể gửi lại |
| D5 | PASS | Không có `EventSource`/`fetch-event-source`/`text/event-stream` trong code backend/frontend |
| E1 | PASS | React kết nối `/hubs/chat`, gọi `StreamChat` |
| E2 | PASS | .NET xác thực JWT query string `/hubs/chat`, tenant từ claims |
| E3 | PASS | Lưu user message qua Dapper trước truy vấn có cấu trúc |
| E4-E6 | PASS | Structured query trước, retrieval bổ trợ, guardrail từ chối không gọi LLM khi thiếu context |
| E7-E9 | PASS | Prompt dựng ở .NET, gọi LLM qua 9Router, yield `ChatEvent` qua hub |
| E10 | PASS | Python embed, pgvector cosine, filter, trả top-k |
| E11 | PASS | Lưu assistant + citations chỉ khi stream hoàn tất; không lưu bản dở dang khi hủy |
| E12 | PASS | React render stream và citations |
| F1 | PARTIAL | Câu từ chối đọc config nhưng có hard-coded fallback (Guardrail.cs:13-15) |
| F2 | PASS | Tên miền/câu từ chối cấu hình theo tenant |
| F3 | PASS | Structured query là luồng chính cho số liệu; semantic chỉ bổ trợ |
| G1 | PASS | `LLM_API_KEY` từ `.env`/env; không có plaintext trong file đã commit (`.env` gitignore, không tracked) |
| G2 | PASS | `.env` nằm trong `.gitignore` (line 1) |
| G3 | PASS | Không log token/key; lưu ý: lỗi Python được surfaced vào event `error` (ChatOrchestrator.cs:122-125) |
| G4 | PASS | Ghi nhớ: key cũ từng lộ lịch sử repo cần thu hồi (không tự làm) |
| H | PASS (với phân loại) | Không SSE còn sót trong code chạy thật; chỉ test fixture 9Router + doc/script lịch sử |
| I1 | PASS | `dotnet build` thành công, 0 warning, 0 error |
| I2 | PASS | `tsc && vite build` thành công |
| I3 | PARTIAL | Không có test .NET; frontend vitest declared nhưng không có config/test file; chỉ có e2e cần stack |
| I4 | FAIL | Thiếu test guardrail không gọi LLM, hub từ chối chưa auth, tenant từ claims, hủy stream |
| I5 | UNVERIFIED | Chưa chạy stack đầy đủ (Docker, tải model bge-m3, gọi 9Router thật) |
| J | — | Rủi ro: semaphore in-memory không scale, Dict chưa dọn, Python chat DB functions, min_score lệch, raw lỗi Python surfaced |

---

## 3. Chi tiết các mục FAIL / PARTIAL

### I4 — FAIL (Critical về chất lượng, High về khâu kiểm chứng)
**Vì sao lệch plan:** Plan Phase 5 (I4) yêu cầu test: guardrail không gọi LLM khi thiếu context; hub từ chối kết nối chưa xác thực; tenant lấy từ claims; hủy stream.  
**Bằng chứng:**
- `backend-dotnet.Tests/` chỉ có thư mục `obj`, **không có `.csproj` và không có file `.cs`** → không tồn tại unit test .NET.
- `frontend-react/` không có `vitest.config.ts`, không có file `*.test.*`/`*.spec.*` trong `src`; `package.json` khai báo `"test": "vitest run"` nhưng không thể chạy test thực sự.
- `tests/e2e/tests/api/chat.test.ts:32-45` ghi nhận luồng SignalR **không được port sang SignalR** vì Playwright `APIRequestContext` chỉ hỗ trợ HTTP; các kiểm tra guardrail/truy vấn có cấu trúc/bảo mật/hủy stream nằm ở trạng thái chú thích, không chạy.
**Mức độ:** Critical (không có bằng chứng chạy thật cho luồng chính và bảo mật).  
**Đề hướng sửa:** Thêm xUnit/NUnit test cho `ChatOrchestrator` (guardrail từ chối, hủy `CancellationToken` không lưu assistant), test hub `[Authorize]` từ chối kết nối không token, test `TenantContextProvider` lấy tenant từ claims; thêm vitest cho hook `useChatConnection` (dừng stream, xử lý lỗi).

### F1 — PARTIAL (Medium)
**Vì sao lệch plan:** Quy tắc "câu từ chối cấu hình theo tenant, không hard-code trong code".  
**Bằng chứng:** `backend-dotnet/Services/Guardrail.cs:13-15`:
```csharp
_refusalMessage = config["GUARDRAIL_REFUSAL_MESSAGE"]
    ?? "Xin lỗi, hiện tại tôi chỉ hỗ trợ giải đáp các thông tin liên quan đến tuyển sinh của Nhà trường ... phương thức xét tuyển nhé!";
```
Khi env `GUARDRAIL_REFUSAL_MESSAGE` thiếu, code dùng chuỗi cứng.  
**Mức độ:** Medium.  
**Đề hướng sửa:** Loại bỏ hard-coded fallback; yêu cầu `GUARDRAIL_REFUSAL_MESSAGE` phải có cấu hình theo tenant, hoặc trả lỗi cấu hình nếu thiếu.

### C8 — PARTIAL (Medium)
**Vì sao lệch plan:** C8 yêu cầu `CancellationToken` "thực sự được truyền xuống lời gọi 9Router và các truy vấn Dapper (theo dõi chuỗi gọi, không chỉ thấy tham số có mặt)".  
**Bằng chứng:**
- Retrieval OK: `ChatOrchestrator.cs:113` truyền `ct` vào `_retrieval.SearchAsync(..., ct)` → `RetrievalClient.cs:43` truyền `ct` vào `_http.SendAsync(req, ..., ct)`.
- LLM OK: `LlmClient.cs:25` nhận `ct`, truyền vào `_http.SendAsync(req, ..., ct)` (line 49), `ReadAsStreamAsync(ct)` (line 62), `ReadLineAsync(ct)` (line 65).
- Structured query **KHÔNG OK**: `ChatOrchestrator.cs:101` gọi `_structured.QueryAsync(message, tenantId, ct)`, nhưng `AdmissionStructuredQueryService.QueryAsync` (line 12) nhận `ct` rồi **không truyền vào** `conn.QueryAsync(sql, parameters)` (line 86, 127-128) trong các method con `QueryAdmissionScore`, `QueryTuition`, `QueryAdmissionMethods`. Do đó `ct` chỉ có mặt ở signature, không đi xuống Dapper.
**Mức độ:** Medium.  
**Đề hướng sửa:** Truyền `ct` vào mỗi `conn.QueryAsync`/`conn.ExecuteAsync` trong `AdmissionStructuredQueryService` và `ChatService` (hoặc dùng `WithCancellation`).

### J — Rủi ro ngoài danh mục (không gán PASS/FAIL, ghi lại)
1. **Giới hạn 1 stream/session chỉ hoạt động trong một process:** `ChatOrchestrator.cs:12` dùng `static ConcurrentDictionary<string, SemaphoreSlim>` in-memory. Khi scale nhiều instance backend-dotnet (không có Redis backplane — nằm mục mở rộng sau), giới hạn 1 stream/session không được thi đính toàn cục; từ điển cũng không bao giờ bị dọn entries cũ (rò rỉ bộ nhớ theo số session).  
   **Bằng chứng:** `ChatOrchestrator.cs:70-80`, `finally` release (line 200-204) nhưng không xóa key khỏi `_sessionStreams`.
2. **`CancellationToken` không được truyền vào wrapper Polly retry:** `ChatOrchestrator.cs:113` dùng `PythonRetryPolicy.ExecuteAsync(async () => await _retrieval.SearchAsync(..., ct))` — overload `ExecuteAsync(Func<Task<T>>)` không nhận `ct`, nhưng `ct` vẫn được closure truyền vào `SearchAsync` rồi đến `HttpClient.SendAsync(..., ct)` ở `RetrievalClient.cs:43`. Cho nên chain gọi thực sự vẫn tôn chối.  
   **Bằng chứng:** `ChatOrchestrator.cs:113`, `RetrievalClient.cs:43`.
3. **Python bleed ranh giới chat:** `ai-service/app/database.py:280-332` định nghĩa `get_or_create_session`, `save_message`, `get_session_messages`, `check_session_owner` thao tác bảng `chat_sessions`/`chat_messages` (thuộc .NET theo ranh giới §4). Các hàm này **không có endpoint gọi** trong `main.py`, nhưng có mặt trong code Python → rủi ro vi ranh giới nếu sau này được kích hoạt.  
   **Bằng chứng:** `database.py:280-332`; `main.py` không gọi chúng.
4. **`min_score` lệch giữa cấu hình và gọi retrieval:** `Guardrail.cs:15` default `_minScore = 0.45`, nhưng `ChatOrchestrator.cs:114` gọi `_retrieval.SearchAsync(..., 5, 0.3, ...)` hard-coded `0.3`.  
   **Bằng chứng:** `Guardrail.cs:15`, `ChatOrchestrator.cs:114`.
5. **Raw lỗi Python surfaced đến client:** `ChatOrchestrator.cs:122-125` trả event `error` có nội dung `$"Lỗi truy xuất ngữ cảnh: {retrievalError}"` có thể chứa chi tiết nội bộ. Không lộ secret nhưng không phải thông điệp an toàn hoàn toàn.  
   **Bằng chứng:** `ChatOrchestrator.cs:122-125`.

---

## 4. Các mục UNVERIFIED

| Mục | Lý do | Cách người dùng tự kiểm chứng |
|-----|-------|------------------------------|
| C12 | Chưa inspect Dockerfile/nginx của `frontend-react` cho production; chưa xác nhận WebSocket upgrade qua reverse proxy | Chạy `docker compose up --build` rồi mở trình duyệt đến `frontend-react`, đảm bảo SignalR WebSocket kết nối (không downgrade long polling) |
| I5 | Cần Docker + tải model `BAAI/bge-m3` (~1GB) + 9Router thật; chạy stack sẽ thay đổi trạng thái infra local | Chạy `docker compose up --build`, sau đó: (a) chat đầy đủ, (b) bấm Dừng giữa chừng xác nhận không lưu assistant dở dang, (c) hỏi câu ngoài phạm vi xác nhận câu từ chối cố định, (d) tắt `ai-service` xác nhận .NET trả lỗi qua SignalR không treo |

---

## 5. Phân loại dấu vết SSE còn sót (mục H)

Tìm kiếm toàn repo (loại `node_modules`, `bin`, `obj`, `.git`) với `text/event-stream`, `EventSource`, `fetchEventSource`, `fetch-event-source`, `/api/chat/stream`, `Content-Type: text/event-stream`:

| File:Dòng | Nội dung | Phân loại |
|-----------|----------|-----------|
| `tests/fixtures/fake-llm/server.py:52` | `self.send_header("Content-Type", "text/event-stream")` | **Test fixture giả 9Router** (LLM gateway hợp pháp dùng SSE). Không phải stream frontend↔backend. Không vi phạm D5. |
| `PLAN.md:177` | "...chỉ khác kênh truyền tải (SignalR thay vì `text/event-stream`)" | Ghi chú lịch sử, cho phép (A1). |
| `PLAN.md:242` | `POST /api/chat/stream` qua .NET ... | Tham chiếu endpoint SSE cũ trong docs verification (lịch sử). |
| `scripts/verify.ps1:270,300` | `POST http://localhost:5000/api/chat/stream` | Script verify cũ tham chiếu endpoint SSE đã xóa. |
| `docs\BASELINE.md:34,41,58` | Mô tả kiến trúc SSE cũ | Tài liệu lịch sử. |
| `docs\verification\BUG_LOG.md:5`, `docs\BUGS.md:11`, `docs\analysis\current-state.md:11` | Tham chiếu SSE cũ | Tài liệu / nhật ký lỗi lịch sử. |
| `tests\e2e\tests\api\chat.test.ts:34` | "KHÔNG còn qua `POST /api/chat/stream` (SSE đã bị xóa hoàn toàn)" | Ghi chú code xác nhận đã xóa SSE. |
| `backend-dotnet/`, `frontend-react/` | — | **Không có** `EventSource`/`fetch-event-source`/`text/event-stream` (grep xác nhận). |

**Kết luận H:** Không còn SSE dùng thật trong luồng chat frontend↔backend. D5 PASS.

---

## 6. Lệnh đã chạy (để chạy lại)

| Lệnh | Mục đích | Kết quả tóm tắt |
|------|----------|-----------------|
| `dotnet --version` | Kiểm tra SDK | `10.0.404` — có sẵn |
| `node --version` / `npm --version` | Kiểm tra Node | `v26.12.1` / `10.9.2` — có sẵn |
| `docker --version` | Kiểm tra Docker | `29.9.3` — có sẵn |
| `dotnet build "D:\demo-chatbot-ai\backend-dotnet\BackendDotnet.csproj" -c Release --nologo` | I1 build .NET | **Thành công**, 0 Warning, 0 Error |
| `npm run build` (trong `frontend-react`) | I2 tsc + vite build | **Thành công**, `dist/` sinh ra |
| `git status --short` | Kiểm tra tracked files | `clean — nothing to commit`; `.env` và `.env.example` **không được tracked** |
| `git ls-files .env .env.example` | Xác nhận env không commit | Không trả về file nào → không có plaintext key trong repo |

**Lệnh chưa chạy (yêu cầu stack/infra):** `docker compose up --build`, `npm run test` (frontend vitest — thiếu config/test file), `dotnet test` (không có test project), Playwright e2e (`tests/e2e`, `frontend-react/tests`) — cần các service đang chạy.

---

*Báo cáo được tạo bởi reviewer/QA độc lập. Không sửa code; việc khắc phục do người dùng quyết định sau khi đọc.*
