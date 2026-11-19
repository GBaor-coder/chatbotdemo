# Tests E2E (Playwright)

Cách chạy:
```bash
# 1. Khởi động hệ thống (mặc định, không publish 5432/8000 ra host)
docker compose up --build -d

# 2. Chạy fake LLM (profile test) + publish port 8000 cho test
docker compose -f docker-compose.test.yml up -d

# 3. Đợi Python /api/v1/ready và .NET /health xanh

# 4. Cài dependencies (lần đầu)
cd tests/e2e
npm install

# 5. Cài Playwright browser (lần đầu)
npx playwright install chromium

# 6. Chạy toàn bộ test
npx playwright test

# Chỉ chạy nhóm API
npx playwright test --project api

# Chỉ chạy nhóm UI
npx playwright test --project ui

# Báo cáo HTML
npx playwright show-report
```

Biến môi trường cần thiết:
- `PYTHON_BASE_URL`: mặc định `http://localhost:8000`
- `DOTNET_BASE_URL`: mặc định `http://localhost:5000`
- `X_INTERNAL_API_KEY`: mặc định `dev-internal-key-change-me` (phải khớp docker-compose)
- `E2E_BASE_URL`: URL frontend, mặc định `http://localhost:5173`
- `FAKE_LLM_CALLS_URL`: mặc định `http://localhost:20129/__calls`
- `FAKE_LLM_RESET_URL`: mặc định `http://localhost:20129/__reset`

Cấu trúc:
- `tests/api/`: nhóm A (Python RAG Data Service) và nhóm B (.NET Gateway). Nhóm B hiện dùng client `@microsoft/signalr` chạy trong Node (WebSocket) thay vì `APIRequestContext` (chỉ HTTP), vì luồng chat stream qua SignalR Hub `/hubs/chat`.
- `tests/ui/`: nhóm C (React UI).
- `global-setup.ts`: đợi service xanh, chạy seed qua pipeline ingestion.
- `playwright.config.ts`: retries:0, trace/screenshot/video retain-on-failure, reporter html+list.

Biến môi trường cần thiết:
- `PYTHON_BASE_URL`: mặc định `http://localhost:8000`
- `DOTNET_BASE_URL`: mặc định `http://localhost:5000`
- `X_INTERNAL_API_KEY`: mặc định `dev-internal-key-change-me` (phải khớp docker-compose)
- `E2E_BASE_URL`: URL frontend, mặc định `http://localhost:5173`
- `FAKE_LLM_CALLS_URL`: mặc định `http://localhost:20129/__calls`
- `FAKE_LLM_RESET_URL`: mặc định `http://localhost:20129/__reset`

Lưu ý: các kiểm tra SignalR trong `tests/api/chat.test.ts` yêu cầu stack (`docker compose` + `docker-compose.test.yml` cho fake-llm) đang chạy. Nếu stack chưa có, các kiểm tra này sẽ bị đánh dấu `skip` (kiểm tra `/health` khi nạp file).

Ranh giới kiểm thử:
- Trình duyệt KHÔNG được gọi trực tiếp cổng 8000 (Python) hay 5432 (Postgres).
- Mọi request API từ UI chỉ tới origin frontend (5173) hoặc gateway .NET (5000).
