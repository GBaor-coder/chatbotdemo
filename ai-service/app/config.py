from pydantic import Field
from pydantic_settings import BaseSettings


class Settings(BaseSettings):
    app_name: str = "ai-service"
    app_version: str = "1.0.0"

    postgres_user: str = Field(default="postgres", alias="POSTGRES_USER")
    postgres_password: str = Field(default="postgres", alias="POSTGRES_PASSWORD")
    postgres_db: str = Field(default="chatbot", alias="POSTGRES_DB")
    postgres_host: str = Field(default="db", alias="POSTGRES_HOST")
    postgres_port: int = Field(default=5432, alias="POSTGRES_PORT")

    pgvector_dim: int = Field(default=1024, alias="PGVECTOR_DIM")

    embedding_model: str = Field(default="BAAI/bge-m3", alias="EMBEDDING_MODEL")
    embedding_device: str = Field(default="cpu", alias="EMBEDDING_DEVICE")

    llm_base_url: str = Field(
        default="http://host.docker.internal:20128/v1", alias="LLM_BASE_URL"
    )
    llm_api_key: str = Field(
        default="sk-57a432014c1cc01c-6juq1i-fe7dbfbd", alias="LLM_API_KEY"
    )
    llm_model: str = Field(default="chatbotai", alias="LLM_MODEL")

    gateway_url: str = Field(default="http://backend-dotnet:5000", alias="GATEWAY_URL")

    log_level: str = Field(default="INFO", alias="LOG_LEVEL")

    class Config:
        env_file = ".env"
        env_file_encoding = "utf-8"


settings = Settings()
