from fastapi import FastAPI, HTTPException
from fastapi.responses import StreamingResponse
from pydantic import BaseModel
import json
import logging

from .config import settings
from .database import get_or_create_session, save_message, get_session_messages
from .rag_engine import retrieve_context, build_prompt, stream_llm_tokens, GUARDRAIL_REFUSAL
from .seed_data import seed

logger = logging.getLogger(__name__)

app = FastAPI(title=settings.app_name, version=settings.app_version)


@app.on_event("startup")
async def on_startup():
    try:
        seed()
        logger.info("Seed data loaded and embedded on startup.")
    except Exception as exc:
        logger.exception("Startup seed failed: %s", exc)


class SeedResponse(BaseModel):
    status: str
    message: str


class RagRequest(BaseModel):
    message: str
    session_id: str | None = None
    user_id: str = "anonymous"


class RagResponse(BaseModel):
    session_id: str
    answer: str
    sources: list[dict] = []


@app.get("/health")
async def health():
    return {"status": "ok", "service": settings.app_name}


@app.post("/ingest/seed", response_model=SeedResponse)
async def ingest_seed():
    try:
        seed()
        return SeedResponse(status="ok", message="Seed data loaded and embedded into pgvector.")
    except Exception as exc:
        logger.exception("seed failed")
        raise HTTPException(status_code=500, detail=str(exc))


@app.post("/rag/stream")
async def rag_stream(req: RagRequest):
    session_id = req.session_id or get_or_create_session(req.user_id, req.message[:60])

    # Lưu tin nhắn người dùng
    save_message(session_id, "user", req.message)

    # Truy xuất ngữ cảnh và xây dựng prompt
    contexts = retrieve_context(req.message)
    prompt = build_prompt(req.message, contexts)

    async def event_stream():
        full_answer = []
        try:
            async for token in stream_llm_tokens(prompt):
                full_answer.append(token)
                yield f"data: {json.dumps({'type': 'token', 'content': token}, ensure_ascii=False)}\n\n"

            answer = "".join(full_answer)

            # Kiểm tra guardrail: nếu câu trả lời là từ chối cứng, không gán citations
            if answer.strip() == GUARDRAIL_REFUSAL.strip():
                sources = []
            else:
                sources = [
                    {
                        "id": str(c.get("id", "")),
                        "title": c.get("title", ""),
                        "source": c.get("source", ""),
                        "similarity": round(float(c.get("similarity", 0.0)), 4),
                    }
                    for c in contexts
                ]

            yield f"data: {json.dumps({'type': 'citations', 'sources': sources}, ensure_ascii=False)}\n\n"
            yield f"data: {json.dumps({'type': 'done', 'session_id': session_id}, ensure_ascii=False)}\n\n"

            # Lưu câu trả lời hoàn chỉnh của bot
            save_message(session_id, "assistant", answer, metadata={"sources": sources})

        except Exception as exc:
            logger.exception("rag stream error")
            yield f"data: {json.dumps({'type': 'error', 'message': str(exc)}, ensure_ascii=False)}\n\n"

    return StreamingResponse(event_stream(), media_type="text/event-stream")
