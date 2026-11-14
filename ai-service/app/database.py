import psycopg2
from psycopg2.extras import RealDictCursor
from pgvector.psycopg2 import register_vector

from .config import settings


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


def search_similar_chunks(embedding: list[float], limit: int = 5):
    with get_conn() as conn, conn.cursor(cursor_factory=RealDictCursor) as cur:
        cur.execute(
            """
            SELECT id, source, title, content, metadata,
                   1 - (embedding <=> %s::vector) AS similarity
            FROM knowledge_chunks
            WHERE embedding IS NOT NULL
            ORDER BY embedding <=> %s::vector
            LIMIT %s
            """,
            (embedding, embedding, limit),
        )
        return cur.fetchall()


def upsert_knowledge_chunk(source: str, title: str, content: str, embedding: list[float], metadata: dict):
    with get_conn() as conn, conn.cursor() as cur:
        cur.execute(
            """
            INSERT INTO knowledge_chunks (source, title, content, embedding, metadata)
            VALUES (%s, %s, %s, %s::vector, %s::jsonb)
            ON CONFLICT (source, title) DO UPDATE
            SET content = EXCLUDED.content,
                embedding = EXCLUDED.embedding,
                metadata = EXCLUDED.metadata,
                created_at = NOW()
            """,
            (
                source,
                title,
                content,
                embedding,
                __import__("json").dumps(metadata, ensure_ascii=False),
            ),
        )


def get_or_create_session(user_id: str, title: str | None = None) -> str:
    with get_conn() as conn, conn.cursor(cursor_factory=RealDictCursor) as cur:
        cur.execute(
            """
            INSERT INTO chat_sessions (user_id, title, created_at, updated_at)
            VALUES (%s, %s, NOW(), NOW())
            RETURNING id
            """,
            (user_id, title or "Cuộc trò chuyện mới"),
        )
        row = cur.fetchone()
        return str(row["id"])


def save_message(session_id: str, role: str, content: str, metadata: dict | None = None):
    with get_conn() as conn, conn.cursor() as cur:
        cur.execute(
            """
            INSERT INTO chat_messages (session_id, role, content, metadata, created_at)
            VALUES (%s, %s, %s, %s::jsonb, NOW())
            """,
            (
                session_id,
                role,
                content,
                __import__("json").dumps(metadata or {}, ensure_ascii=False),
            ),
        )


def get_session_messages(session_id: str):
    with get_conn() as conn, conn.cursor(cursor_factory=RealDictCursor) as cur:
        cur.execute(
            """
            SELECT id, role, content, metadata, created_at
            FROM chat_messages
            WHERE session_id = %s AND deleted_at IS NULL
            ORDER BY created_at ASC
            """,
            (session_id,),
        )
        return cur.fetchall()
