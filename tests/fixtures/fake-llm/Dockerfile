FROM python:3.12-slim
WORKDIR /app
COPY tests/fixtures/fake-llm/server.py /app/server.py
EXPOSE 20128
CMD ["python", "/app/server.py"]
