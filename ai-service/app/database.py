import psycopg2
from psycopg2.extras import RealDictCursor
from pgvector.psycopg2 import register_vector
import hashlib
import json

from .config import settings

# Whitelist các filter key được phép trong /api/v1/retrieval/search
FILTER_WHITELIST = {
    "tenant_id",
    "doc_type",
    "document_status",
    "metadata",
}


def get_conn():
    conn = psycopg2.connect(
        dbname=settings.postgres_db,
        user=settings.postgres_user,
        password=settings.postgres_password,
        host=settings.postgres_host,
        port=settings.postgres_port,
    )
    conn.autocommit = True
    register_vector(conn)
    return conn


def content_hash(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


# ---------------------------------------------------------------------------
# Documents
# ---------------------------------------------------------------------------

def upsert_document(tenant_id: str, source: str, title: str, doc_type: str, status: str, content: str, metadata: dict) -> str:
    """Tạo hoặc cập nhật document, trả về document_id."""
    chash = content_hash(content)
    with get_conn() as conn, conn.cursor(cursor_factory=RealDictCursor) as cur:
        cur.execute(
            """
            INSERT INTO documents (tenant_id, source, title, doc_type, status, content_hash, metadata, created_at, updated_at)
            VALUES (%s::uuid, %s, %s, %s, %s, %s, %s::jsonb, NOW(), NOW())
            ON CONFLICT (tenant_id, source, title) DO UPDATE
            SET doc_type = EXCLUDED.doc_type,
                status = EXCLUDED.status,
                content_hash = EXCLUDED.content_hash,
                metadata = EXCLUDED.metadata,
                updated_at = NOW()
            RETURNING id
            """,
            (
                tenant_id,
                source,
                title,
                doc_type,
                status,
                chash,
                json.dumps(metadata, ensure_ascii=False),
            ),
        )
        row = cur.fetchone()
        return str(row["id"])


def get_document(document_id: str):
    with get_conn() as conn, conn.cursor(cursor_factory=RealDictCursor) as cur:
        cur.execute(
            "SELECT * FROM documents WHERE id = %s::uuid",
            (document_id,),
        )
        return cur.fetchone()


def delete_document(document_id: str):
    with get_conn() as conn, conn.cursor() as cur:
        cur.execute("DELETE FROM knowledge_chunks WHERE document_id = %s::uuid", (document_id,))
        cur.execute("DELETE FROM documents WHERE id = %s::uuid", (document_id,))


# ---------------------------------------------------------------------------
# Knowledge chunks
# ---------------------------------------------------------------------------

def upsert_knowledge_chunk(
    tenant_id: str,
    document_id: str,
    chunk_index: int,
    content: str,
    embedding: list[float],
    metadata: dict,
    embedding_model: str = "BAAI/bge-m3",
    embedding_version: str = "1",
    lang: str = "vi",
):
    chash = content_hash(content)
    with get_conn() as conn, conn.cursor() as cur:
        cur.execute(
            """
            INSERT INTO knowledge_chunks
                (tenant_id, document_id, chunk_index, content, embedding, content_hash,
                 embedding_model, embedding_version, lang, metadata, created_at)
            VALUES (%s::uuid, %s::uuid, %s, %s, %s::vector, %s, %s, %s, %s, %s::jsonb, NOW())
            ON CONFLICT (tenant_id, document_id, chunk_index) DO UPDATE
            SET content = EXCLUDED.content,
                embedding = EXCLUDED.embedding,
                content_hash = EXCLUDED.content_hash,
                embedding_model = EXCLUDED.embedding_model,
                embedding_version = EXCLUDED.embedding_version,
                lang = EXCLUDED.lang,
                metadata = EXCLUDED.metadata,
                created_at = NOW()
            """,
            (
                tenant_id,
                document_id,
                chunk_index,
                content,
                embedding,
                chash,
                embedding_model,
                embedding_version,
                lang,
                json.dumps(metadata, ensure_ascii=False),
            ),
        )


def delete_chunks_by_document(document_id: str):
    with get_conn() as conn, conn.cursor() as cur:
        cur.execute("DELETE FROM knowledge_chunks WHERE document_id = %s::uuid", (document_id,))


# ---------------------------------------------------------------------------
# Retrieval (POST /api/v1/retrieval/search)
# ---------------------------------------------------------------------------

def validate_filters(filters: dict) -> None:
    """Kiểm tra filter key nằm trong whitelist. Ném ValueError nếu không."""
    if not isinstance(filters, dict):
        raise ValueError("filters phải là object")
    for key in filters:
        if key not in FILTER_WHITELIST:
            raise ValueError(f"Filter key không được phép: {key}")
    if "tenant_id" not in filters:
        raise ValueError("tenant_id là bắt buộc trong filters")


def build_where_clause(filters: dict):
    """Xây dựng WHERE clause và params từ filter đã validate."""
    conditions = []
    params = []

    tenant_id = filters.get("tenant_id")
    conditions.append("kc.tenant_id = %s::uuid")
    params.append(tenant_id)

    if "doc_type" in filters:
        doc_types = filters["doc_type"]
        if isinstance(doc_types, str):
            doc_types = [doc_types]
        placeholders = ",".join(["%s"] * len(doc_types))
        conditions.append(f"d.doc_type IN ({placeholders})")
        params.extend(doc_types)

    if "document_status" in filters:
        conditions.append("d.status = %s")
        params.append(filters["document_status"])

    if "metadata" in filters:
        md = filters["metadata"]
        for mk, mv in md.items():
            conditions.append(f"kc.metadata ->> %s = %s")
            params.extend([mk, str(mv)])

    where = " AND ".join(conditions)
    return where, params


def search_similar_chunks(
    query_embedding: list[float],
    filters: dict,
    top_k: int = 5,
    min_score: float = 0.0,
):
    """Tìm kiếm vector pgvector với filter tenant_id và whitelist."""
    validate_filters(filters)
    where, params = build_where_clause(filters)

    # top_k giới hạn tối đa 20
    if top_k > 20:
        top_k = 20
    if top_k < 1:
        top_k = 1

    sql = f"""
        SELECT
            kc.id AS chunk_id,
            kc.document_id,
            kc.content,
            kc.metadata,
            d.title,
            d.source,
            d.doc_type,
            1 - (kc.embedding <=> %s::vector) AS score
        FROM knowledge_chunks kc
        JOIN documents d ON d.id = kc.document_id
        WHERE kc.embedding IS NOT NULL
          AND {where}
          AND (1 - (kc.embedding <=> %s::vector)) >= %s
        ORDER BY kc.embedding <=> %s::vector
        LIMIT %s
    """
    all_params = [query_embedding] + params + [query_embedding, min_score, query_embedding, top_k]

    with get_conn() as conn, conn.cursor(cursor_factory=RealDictCursor) as cur:
        cur.execute(sql, tuple(all_params))
        rows = cur.fetchall()

    results = []
    for r in rows:
        results.append({
            "chunk_id": str(r["chunk_id"]),
            "document_id": str(r["document_id"]),
            "content": r["content"],
            "score": round(float(r["score"]), 6),
            "metadata": {
                "title": r["title"],
                "source": r["source"],
                "doc_type": r["doc_type"],
                **(r["metadata"] or {}),
            },
        })
    return results


# ---------------------------------------------------------------------------
# Ingestion jobs
# ---------------------------------------------------------------------------

def create_ingestion_job(tenant_id: str, document_id: str | None = None) -> str:
    with get_conn() as conn, conn.cursor(cursor_factory=RealDictCursor) as cur:
        cur.execute(
            """
            INSERT INTO ingestion_jobs (tenant_id, document_id, status, started_at, created_at)
            VALUES (%s::uuid, %s::uuid, 'pending', NOW(), NOW())
            RETURNING id
            """,
            (tenant_id, document_id),
        )
        row = cur.fetchone()
        return str(row["id"])


def update_ingestion_job(job_id: str, status: str, error_message: str | None = None, finished_at=None):
    with get_conn() as conn, conn.cursor() as cur:
        cur.execute(
            """
            UPDATE ingestion_jobs
            SET status = %s, error_message = %s, finished_at = COALESCE(%s, finished_at)
            WHERE id = %s::uuid
            """,
            (status, error_message, finished_at, job_id),
        )


def get_ingestion_job(job_id: str):
    with get_conn() as conn, conn.cursor(cursor_factory=RealDictCursor) as cur:
        cur.execute("SELECT * FROM ingestion_jobs WHERE id = %s::uuid", (job_id,))
        return cur.fetchone()


# ---------------------------------------------------------------------------
# Chat (giữ nguyên, nhưng thêm tenant_id vào session)
# ---------------------------------------------------------------------------

def get_or_create_session(tenant_id: str, user_id: str, title: str | None = None) -> str:
    with get_conn() as conn, conn.cursor(cursor_factory=RealDictCursor) as cur:
        cur.execute(
            """
            INSERT INTO chat_sessions (tenant_id, user_id, title, created_at, updated_at)
            VALUES (%s::uuid, %s, %s, NOW(), NOW())
            RETURNING id
            """,
            (tenant_id, user_id, title or "Cuộc trò chuyện mới"),
        )
        row = cur.fetchone()
        return str(row["id"])


def save_message(session_id: str, role: str, content: str, citations: list | None = None, metadata: dict | None = None):
    with get_conn() as conn, conn.cursor() as cur:
        cur.execute(
            """
            INSERT INTO chat_messages (session_id, role, content, citations, metadata, created_at)
            VALUES (%s::uuid, %s, %s, %s::jsonb, %s::jsonb, NOW())
            """,
            (
                session_id,
                role,
                content,
                json.dumps(citations or [], ensure_ascii=False),
                json.dumps(metadata or {}, ensure_ascii=False),
            ),
        )


def get_session_messages(session_id: str):
    with get_conn() as conn, conn.cursor(cursor_factory=RealDictCursor) as cur:
        cur.execute(
            """
            SELECT id, role, content, citations, metadata, created_at
            FROM chat_messages
            WHERE session_id = %s::uuid AND deleted_at IS NULL
            ORDER BY created_at ASC
            """,
            (session_id,),
        )
        return cur.fetchall()


def check_session_owner(session_id: str, tenant_id: str):
    """Trả về True nếu session thuộc về tenant_id, ngược lại False."""
    with get_conn() as conn, conn.cursor() as cur:
        cur.execute(
            "SELECT 1 FROM chat_sessions WHERE id = %s::uuid AND tenant_id = %s::uuid AND deleted_at IS NULL",
            (session_id, tenant_id),
        )
        return cur.fetchone() is not None
