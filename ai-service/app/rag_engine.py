from .config import settings
from .database import search_similar_chunks, upsert_knowledge_chunk
from .embeddings import embed_text


GUARDRAIL_REFUSAL = (
    "Xin lỗi, hiện tại tôi chỉ hỗ trợ giải đáp các thông tin liên quan đến tuyển sinh của Nhà trường "
    "dựa trên dữ liệu chính thức. Bạn vui lòng đặt câu hỏi về ngành học, điểm chuẩn, học phí hoặc phương thức xét tuyển nhé!"
)

SYSTEM_PROMPT_TEMPLATE = """Bạn là trợ lý ảo tư vấn tuyển sinh Đại học Việt Nam.

NGUYÊN TẢ CẤN TRỌNG:
- Chỉ trả lời dựa trên NGỮ CẢNH đã được cung cấp bên dưới. Không bịa đặt, không suy luận ngoài ngữ cảnh.
- Nếu ngữ cảnh không chứa thông tin đủ để trả lời, hoặc câu hỏi nằm ngoài phạm vi tuyển sinh (thời tiết, lập trình, chính trị, ngành không có trong dữ liệu...), bạn PHẢI trử liệu bằng câu trả lời từ chối cứng sau:
  "{refusal}"
- Khi trả lời, hãy dẫn nguồn trích dẫn từ từng đoạn ngữ cảnh được cung cấp.
- Trả lời bằng tiếng Việt, ngắn gọn, rõ ràng, đúng trọng tâm câu hỏi.

NGỮ CẢNH:
{context}

CÂU HỎI: {query}

TRẢ LỜI:"""


def retrieve_context(query: str, limit: int = 5) -> list[dict]:
    embedding = embed_text(query)
    return search_similar_chunks(embedding, limit=limit)


def build_prompt(query: str, contexts: list[dict]) -> str:
    ctx_parts = []
    for i, c in enumerate(contexts):
        ctx_parts.append(
            f"[Nguồn {i+1}] {c.get('title', 'Không tên')} (nguồn: {c.get('source', 'N/A')})\n{c.get('content', '')}"
        )
    context = "\n\n".join(ctx_parts) if ctx_parts else "(Không có ngữ cảnh liên quan.)"
    return SYSTEM_PROMPT_TEMPLATE.format(
        refusal=GUARDRAIL_REFUSAL,
        context=context,
        query=query,
    )


async def stream_llm_tokens(prompt: str):
    """Stream tokens from 9Router via OpenAI-compatible SDK."""
    from openai import AsyncOpenAI

    client = AsyncOpenAI(
        base_url=settings.llm_base_url,
        api_key=settings.llm_api_key,
    )

    stream = await client.chat.completions.create(
        model=settings.llm_model,
        messages=[
            {"role": "system", "content": prompt},
            {"role": "user", "content": prompt},
        ],
        temperature=0.3,
        stream=True,
    )

    async for chunk in stream:
        if not chunk.choices:
            continue
        delta = chunk.choices[0].delta
        if delta and delta.content:
            yield delta.content
