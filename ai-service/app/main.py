from fastapi import FastAPI, HTTPException, Request, UploadFile, File, Form
from fastapi.responses import JSONResponse
from fastapi.exceptions import RequestValidationError
from pydantic import BaseModel, Field, ConfigDict
from typing import Optional, Any
import uuid
import time
import logging
import asyncio
from concurrent.futures import ThreadPoolExecutor

from .config import settings
from .database import (
    get_conn,
    content_hash,
    upsert_document,
    get_document,
    delete_document,
    upsert_knowledge_chunk,
    delete_chunks_by_document,
    search_similar_chunks,
    validate_filters,
    create_ingestion_job,
    update_ingestion_job,
    get_ingestion_job,
)
from .embeddings import get_model, embed_text

logger = logging.getLogger(__name__)

app = FastAPI(title=settings.app_name, version=settings.app_version)


@app.exception_handler(RequestValidationError)
async def validation_exception_handler(request: Request, exc: RequestValidationError):
    request_id = request.headers.get("X-Request-Id", str(uuid.uuid4()))
    return JSONResponse(
        status_code=422,
        content={
            "success": False,
            "error": {"code": "validation_error", "message": str(exc)},
            "request_id": request_id,
        },
    )

_model_ready = False
_db_ready = False
executor = ThreadPoolExecutor(max_workers=4)


# ---------------------------------------------------------------------------
# Startup / readiness
# ---------------------------------------------------------------------------

@app.on_event("startup")
async def on_startup():
    global _model_ready, _db_ready
    try:
        get_model()
        _model_ready = True
        logger.info("Embedding model loaded.")
    except Exception as exc:
        logger.exception("Failed to load embedding model: %s", exc)
        _model_ready = False

    try:
        with get_conn() as conn:
            with conn.cursor() as cur:
                cur.execute("SELECT 1")
        _db_ready = True
        logger.info("Database connection OK.")
    except Exception as exc:
        logger.exception("Database connection failed: %s", exc)
        _db_ready = False


# ---------------------------------------------------------------------------
# Auth dependency
# ---------------------------------------------------------------------------

def verify_internal_api_key(request: Request):
    api_key = request.headers.get("X-Internal-Api-Key", "")
    if not api_key or api_key != settings.x_internal_api_key:
        raise HTTPException(status_code=401, detail="Invalid or missing X-Internal-Api-Key")


def get_request_id(request: Request) -> str:
    return request.headers.get("X-Request-Id", str(uuid.uuid4()))


# ---------------------------------------------------------------------------
# Health / Ready
# ---------------------------------------------------------------------------

@app.get("/api/v1/health")
async def health():
    return {"status": "ok", "service": settings.app_name, "version": settings.app_version}


@app.get("/api/v1/ready")
async def ready():
    model_ok = _model_ready
    db_ok = _db_ready
    if model_ok and db_ok:
        return {"status": "ready", "model": settings.embedding_model, "dim": settings.pgvector_dim}
    return JSONResponse(
        status_code=503,
        content={"status": "not ready", "model_loaded": model_ok, "db_connected": db_ok},
    )


# ---------------------------------------------------------------------------
# Retrieval search
# ---------------------------------------------------------------------------

class SearchFilters(BaseModel):
    model_config = ConfigDict(extra="forbid")
    tenant_id: Optional[str] = Field(default=None, description="Bắt buộc, do .NET xác định từ danh tính")
    doc_type: Optional[list[str] | str] = None
    document_status: Optional[str] = None
    metadata: Optional[dict[str, Any]] = None


class SearchRequest(BaseModel):
    query: str
    top_k: int = Field(default=5, ge=1)
    min_score: float = Field(default=0.0, ge=0.0, le=1.0)
    filters: SearchFilters


class ChunkResult(BaseModel):
    chunk_id: str
    document_id: str
    content: str
    score: float
    metadata: dict


class SearchMeta(BaseModel):
    embedding_model: str
    embedding_version: str
    dim: int
    embed_ms: int
    query_ms: int
    returned: int


class SearchResponse(BaseModel):
    success: bool
    results: list[ChunkResult]
    meta: SearchMeta


@app.post("/api/v1/retrieval/search", response_model=SearchResponse)
async def retrieval_search(req: SearchRequest, request: Request):
    request_id = get_request_id(request)
    verify_internal_api_key(request)

    t0 = time.perf_counter()
    # Embed query trong executor để không chặn event loop
    loop = asyncio.get_event_loop()
    query_embedding = await loop.run_in_executor(executor, embed_text, req.query)
    t1 = time.perf_counter()
    embed_ms = int((t1 - t0) * 1000)

    filters = req.filters.model_dump(exclude_unset=True)
    try:
        validate_filters(filters)
    except ValueError as exc:
        return JSONResponse(
            status_code=422,
            content={
                "success": False,
                "error": {"code": "validation_error", "message": str(exc)},
                "request_id": request_id,
            },
        )

    t2 = time.perf_counter()
    clamped_top_k = min(max(req.top_k, 1), 20)
    results = search_similar_chunks(
        query_embedding=query_embedding,
        filters=filters,
        top_k=clamped_top_k,
        min_score=req.min_score,
    )
    t3 = time.perf_counter()
    query_ms = int((t3 - t2) * 1000)

    return {
        "success": True,
        "results": results,
        "meta": {
            "embedding_model": settings.embedding_model,
            "embedding_version": "1",
            "dim": settings.pgvector_dim,
            "embed_ms": embed_ms,
            "query_ms": query_ms,
            "returned": len(results),
        },
        "request_id": request_id,
    }


# ---------------------------------------------------------------------------
# Ingestion
# ---------------------------------------------------------------------------

class IngestDocumentRequest(BaseModel):
    tenant_id: str
    source: str
    title: str
    doc_type: str = "text"
    status: str = "published"
    content: str
    metadata: dict = {}


class IngestDocumentResponse(BaseModel):
    job_id: str
    status: str
    message: str


@app.post("/ingestion/documents", response_model=IngestDocumentResponse)
async def ingest_document(req: IngestDocumentRequest, request: Request):
    request_id = get_request_id(request)
    verify_internal_api_key(request)

    job_id = create_ingestion_job(req.tenant_id)

    # Xử lý ingestion bất đồng bộ (chạy trong background task)
    async def _process():
        try:
            update_ingestion_job(job_id, "processing")
            doc_id = upsert_document(
                tenant_id=req.tenant_id,
                source=req.source,
                title=req.title,
                doc_type=req.doc_type,
                status=req.status,
                content=req.content,
                metadata=req.metadata,
            )
            # Chunking đơn giản: chia theo đoạn (mỗi 500 ký tự một chunk)
            chunks = _simple_chunk(req.content, chunk_size=500)
            model = get_model()
            for i, chunk_text in enumerate(chunks):
                embedding = model.encode(chunk_text, normalize_embeddings=True)
                upsert_knowledge_chunk(
                    tenant_id=req.tenant_id,
                    document_id=doc_id,
                    chunk_index=i,
                    content=chunk_text,
                    embedding=[float(x) for x in embedding],
                    metadata=req.metadata,
                    embedding_model=settings.embedding_model,
                    embedding_version="1",
                    lang="vi",
                )
            update_ingestion_job(job_id, "completed")
        except Exception as exc:
            logger.exception("Ingestion job %s failed", job_id)
            update_ingestion_job(job_id, "failed", error_message=str(exc))

    asyncio.create_task(_process())

    return {
        "job_id": job_id,
        "status": "pending",
        "message": "Ingestion job created, processing asynchronously.",
    }


@app.get("/ingestion/jobs/{job_id}")
async def get_job(job_id: str, request: Request):
    request_id = get_request_id(request)
    verify_internal_api_key(request)
    job = get_ingestion_job(job_id)
    if not job:
        raise HTTPException(status_code=404, detail="Job not found")
    return {
        "job_id": str(job["id"]),
        "tenant_id": str(job["tenant_id"]),
        "document_id": str(job["document_id"]) if job["document_id"] else None,
        "status": job["status"],
        "started_at": job["started_at"].isoformat() if job["started_at"] else None,
        "finished_at": job["finished_at"].isoformat() if job["finished_at"] else None,
        "error_message": job["error_message"],
        "created_at": job["created_at"].isoformat() if job["created_at"] else None,
    }


@app.delete("/ingestion/documents/{document_id}")
async def delete_doc(document_id: str, request: Request):
    request_id = get_request_id(request)
    verify_internal_api_key(request)
    delete_document(document_id)
    return {"success": True, "message": f"Document {document_id} and its chunks deleted."}


@app.post("/ingestion/reembed")
async def reembed(request: Request):
    request_id = get_request_id(request)
    verify_internal_api_key(request)
    # Placeholder: re-embed tất cả chunks với embedding_model mới
    return {"success": True, "message": "Re-embed triggered (placeholder)."}


@app.post("/embed")
async def embed_text_endpoint(request: Request, text: str = Form(...)):
    request_id = get_request_id(request)
    verify_internal_api_key(request)
    loop = asyncio.get_event_loop()
    embedding = await loop.run_in_executor(executor, embed_text, text)
    return {
        "embedding": embedding,
        "dim": len(embedding),
        "model": settings.embedding_model,
        "request_id": request_id,
    }


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def _simple_chunk(text: str, chunk_size: int = 500) -> list[str]:
    """Chunking đơn giản: chia văn bản thành các đoạn không quá chunk_size ký tự."""
    if not text:
        return []
    words = text.split()
    chunks = []
    current = []
    current_len = 0
    for w in words:
        if current_len + len(w) + 1 > chunk_size and current:
            chunks.append(" ".join(current))
            current = [w]
            current_len = len(w)
        else:
            current.append(w)
            current_len += len(w) + 1
    if current:
        chunks.append(" ".join(current))
    return chunks
