import { useState, useCallback, useEffect } from "react";
import { ChatMessage } from "@/types";

export function useChatStream() {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [isStreaming, setIsStreaming] = useState(false);
  const [sessionId, setSessionId] = useState<string | undefined>();

  useEffect(() => {
    const saved = sessionStorage.getItem("admission_session_id");
    if (saved) setSessionId(saved);
  }, []);

  const sendMessage = useCallback(
    async (text: string): Promise<void> => {
      if (!text.trim()) return;

      const userMsg: ChatMessage = {
        id: crypto.randomUUID(),
        role: "user",
        content: text,
        createdAt: new Date().toISOString(),
      };
      setMessages((prev) => [...prev, userMsg]);
      setIsStreaming(true);

      let assistantContent = "";
      let assistantId = crypto.randomUUID();
      let sources: Array<{ id?: string; title?: string; source?: string; similarity?: number }> = [];
      let resolvedSessionId = sessionId;

      setMessages((prev) => [
        ...prev,
        { id: assistantId, role: "assistant", content: "", createdAt: new Date().toISOString() },
      ]);

      try {
        const res = await fetch("/api/chat/stream", {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "Authorization": "Bearer dev-token",
          },
          body: JSON.stringify({
            message: text,
            sessionId: sessionId,
          }),
        });

        if (!res.body) throw new Error("No response body");

        const reader = res.body.getReader();
        const decoder = new TextDecoder();
        let buffer = "";

        while (true) {
          const { done, value } = await reader.read();
          if (done) break;
          buffer += decoder.decode(value, { stream: true });

          const lines = buffer.split("\n");
          buffer = lines.pop() ?? "";

          for (const line of lines) {
            if (line.startsWith("data: ")) {
              try {
                const parsed = JSON.parse(line.slice(6)) as {
                  type: string;
                  content?: string;
                  sources?: Array<{ id?: string; title?: string; source?: string; similarity?: number }>;
                  sessionId?: string;
                  done?: boolean;
                  error?: string;
                };

                if (parsed.type === "token" && parsed.content) {
                  assistantContent += parsed.content;
                  setMessages((prev) =>
                    prev.map((m) =>
                      m.id === assistantId ? { ...m, content: assistantContent } : m
                    )
                  );
                } else if (parsed.type === "citations") {
                  sources = parsed.sources ?? [];
                } else if (parsed.type === "done") {
                  resolvedSessionId = parsed.sessionId ?? resolvedSessionId;
                  if (resolvedSessionId) {
                    setSessionId(resolvedSessionId);
                    sessionStorage.setItem("admission_session_id", resolvedSessionId);
                  }
                } else if (parsed.type === "error") {
                  assistantContent += `\n\nLỗi: ${parsed.error}`;
                  setMessages((prev) =>
                    prev.map((m) =>
                      m.id === assistantId ? { ...m, content: assistantContent } : m
                    )
                  );
                }
              } catch {
                // ignore malformed SSE lines
              }
            }
          }
        }

        // Final update with sources
        setMessages((prev) =>
          prev.map((m) =>
            m.id === assistantId
              ? { ...m, content: assistantContent, sources }
              : m
          )
        );
      } catch (err) {
        setMessages((prev) =>
          prev.map((m) =>
            m.id === assistantId
              ? { ...m, content: assistantContent || "Không thể kết nối đến máy chủ." }
              : m
          )
        );
      } finally {
        setIsStreaming(false);
      }
    },
    [sessionId]
  );

  return { messages, isStreaming, sessionId, sendMessage };
}
