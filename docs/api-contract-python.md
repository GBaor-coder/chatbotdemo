# Hợp đồng API .NET ⇄ Python (RAG Data Service)

Base URL (nội bộ Docker network): `http://ai-service:8000`
Tiền tố tất cả endpoint: `/api/v1`

## Xác thực service-to-service
- Header `X-Internal-Api-Key`: khóa chung giữa .NET backend và Python service (đọc từ biến môi trường, không log).
- Header `X-Request-Id`: ID truy vấn để trace log (tạo bởi .NET).

## POST /api/v1/retrieval/search — endpoint chính

### Request
```json
{
  "query": "string (bắt buộc)",
  "top_k": 5 (tùy chọn, mặc định 5, tối đa 20),
  "min_score": 0.0 (tùy chọn),
  "filters": {
    "tenant_id": "uuid (bắt buộc)",
    "doc_type": ["..."],
    "document_status": "published",
    "metadata": { "year": 2024 }
  }
}
```

### Response (thành công)
```json
{
  "success": true,
  "results": [
    {
      "document_id": "uuid",
      "chunk_id": "uuid",
      "content": "string",
      "score": 0.87,
      "metadata": { "title": "string", "source": "string", "page": 1 }
    }
  ],
  "meta": {
    "embedding_model": "BAAI/bge-m3",
    "embedding_version": "string",
    "dim": 1024,
    "embed_ms": 120,
    "query_ms": 45,
    "returned": 3
  }
}
```

### Response (lỗi)
```json
{
  "success": false,
  "error": { "code": "validation_error", "message": "tenant_id bắt buộc" },
  "request_id": "uuid"
}
```

### Quy tắc
- `tenant_id` bắt buộc; thiếu → HTTP 422.
- Filter không nằm trong whitelist → HTTP 422 (không bỏ qua im lặng).
- Python không mở rộng phạm vi so với filter nhận được.
- `score` = cosine similarity trong [0,1] (`1 − distance cosine`).
- Không có kết quả → `success: true, results: []` (không phải lỗi).

## GET /api/v1/health
Trả `200 OK` khi process sống.

## GET /api/v1/ready
Trả `200 OK` chỉ khi model bge-m3 đã load và kết nối DB thành công.

## Ingestion (dùng nội bộ/admin pipeline)
- `POST /ingestion/documents` → nhận file hoặc nội dung, trả `jobId` (bất đồng bộ).
- `GET /ingestion/jobs/{jobId}` → trạng thái job.
- `DELETE /ingestion/documents/{id}`.
- `POST /ingestion/reembed`.

## Ranh giới
- Python **không** gọi LLM, **không** dựng prompt, **không** quyết định guardrail, **không** stream SSE.
- .NET gọi duy nhất `/api/v1/retrieval/search` (các endpoint ingestion chỉ dùng pipeline nội bộ).
