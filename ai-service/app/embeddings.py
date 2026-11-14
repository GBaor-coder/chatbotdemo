from sentence_transformers import SentenceTransformer

from .config import settings


_model: SentenceTransformer | None = None


def get_model() -> SentenceTransformer:
    global _model
    if _model is None:
        _model = SentenceTransformer(settings.embedding_model, device=settings.embedding_device)
    return _model


def embed_text(text: str) -> list[float]:
    model = get_model()
    vec = model.encode(text, normalize_embeddings=True)
    return [float(x) for x in vec]
