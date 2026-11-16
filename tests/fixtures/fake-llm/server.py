"""Fake LLM server compatible with OpenAI streaming API, used for e2e tests.
Counts calls and exposes /__calls for assertions.
Run: python tests/fixtures/fake-llm/server.py
"""
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import threading
import time


CALL_COUNT = 0
CALL_LOCK = threading.Lock()
RESPONSE_TEXT = (
    "Đáp án giả từ LLM kiểm tra. "
    "Điểm chuẩn ngành Công nghệ thông tin 2024 là 26.0. "
    "Thông tin được trích dẫn từ các nguồn về điểm chuẩn tuyển sinh."
)
TOKEN_DELAY = 0.02  # giây giữa các token


class FakeLLMHandler(BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        return  # im lặng log

    def do_OPTIONS(self):
        self.send_response(200)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "POST, GET, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "*")
        self.end_headers()

    def do_POST(self):
        global CALL_COUNT
        path = self.path.split("?")[0].rstrip("/")
        if path == "/__reset":
            with CALL_LOCK:
                CALL_COUNT = 0
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Access-Control-Allow-Origin", "*")
            self.end_headers()
            self.wfile.write(b'{"status":"reset"}')
            return
        if path == "/v1/chat/completions" or path == "/chat/completions":
            with CALL_LOCK:
                CALL_COUNT += 1
            # Đọc body (không dùng)
            length = int(self.headers.get("Content-Length", 0))
            self.rfile.read(length)

            self.send_response(200)
            self.send_header("Content-Type", "text/event-stream")
            self.send_header("Cache-Control", "no-cache")
            self.end_headers()

            # Gửi mỗi từ như một token SSE
            words = RESPONSE_TEXT.split()
            for i, word in enumerate(words):
                chunk = {
                    "id": "chatcmpl-fake",
                    "object": "chat.completion.chunk",
                    "model": "chatbotai",
                    "choices": [
                        {
                            "index": 0,
                            "delta": {"content": word + (" " if i < len(words) - 1 else "")},
                            "finish_reason": None,
                        }
                    ],
                }
                self.wfile.write(f"data: {json.dumps(chunk, ensure_ascii=False)}\n\n".encode("utf-8"))
                self.wfile.flush()
                time.sleep(TOKEN_DELAY)

            # Kết thúc stream
            self.wfile.write(b"data: [DONE]\n\n")
            self.wfile.flush()
        else:
            self.send_response(404)
            self.end_headers()

    def do_GET(self):
        global CALL_COUNT
        path = self.path.split("?")[0].rstrip("/")
        if path == "/__calls":
            with CALL_LOCK:
                count = CALL_COUNT
            body = json.dumps({"calls": count}).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Access-Control-Allow-Origin", "*")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
        elif path == "/__reset":
            with CALL_LOCK:
                CALL_COUNT = 0
            self.send_response(200)
            self.send_header("Access-Control-Allow-Origin", "*")
            self.end_headers()
        else:
            self.send_response(404)
            self.end_headers()


if __name__ == "__main__":
    server = ThreadingHTTPServer(("0.0.0.0", 20128), FakeLLMHandler)
    print("Fake LLM listening on :20128")
    server.serve_forever()
