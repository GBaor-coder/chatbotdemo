import { useState, useCallback, useEffect, useRef } from "react";
import {
  HubConnection,
  HubConnectionBuilder,
  LogLevel,
} from "@microsoft/signalr";
import type { ChatMessage, ChatEvent, Source } from "@/types";

let hub: HubConnection | null = null;

let accessTokenPromise: Promise<string> | null = null;

async function resolveAccessToken(): Promise<string> {
  if (accessTokenPromise) return accessTokenPromise;
  accessTokenPromise = (async () => {
    const stored =
      typeof localStorage !== "undefined"
        ? localStorage.getItem("admission_access_token")
        : null;
    if (stored) return stored;
    try {
      const res = await fetch("/api/auth/dev-token", { method: "POST" });
      if (res.ok) {
        const data = (await res.json()) as { token?: string };
        if (data?.token) {
          localStorage.setItem("admission_access_token", data.token);
          return data.token;
        }
      }
    } catch {
      // ignore
    }
    return "";
  })();
  return accessTokenPromise;
}

function ensureConnection(): HubConnection {
  if (hub) return hub;
  const connection = new HubConnectionBuilder()
    .withUrl("/hubs/chat", {
      accessTokenFactory: () => resolveAccessToken(),
    })
    .withAutomaticReconnect([0, 2000, 5000])
    .configureLogging(LogLevel.Information)
    .build();
  hub = connection;
  void connection.start().catch((err) => {
    console.error("SignalR connection failed", err);
  });
  return connection;
}

export function useChatConnection() {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [isStreaming, setIsStreaming] = useState(false);
  const [sessionId, setSessionId] = useState<string | undefined>(undefined);
  const [streamError, setStreamError] = useState<string | undefined>(undefined);
  const subscriptionRef = useRef<{ dispose: () => void } | null>(null);

  useEffect(() => {
    const saved = sessionStorage.getItem("admission_session_id");
    if (saved) setSessionId(saved);
  }, []);

  useEffect(() => {
    if (!hub) {
      ensureConnection();
    }
    return () => {
      // Giữ connection sống qua StrictMode double-mount; không dispose ở đây.
    };
  }, []);

  const stopStream = useCallback(() => {
    subscriptionRef.current?.dispose();
    subscriptionRef.current = null;
    setIsStreaming(false);
    setStreamError(undefined);
  }, []);

  const sendMessage = useCallback(
    async (text: string) => {
      if (!text.trim()) return;
      setStreamError(undefined);

      const userMsg: ChatMessage = {
        id: crypto.randomUUID(),
        role: "user",
        content: text,
        createdAt: new Date().toISOString(),
      };
      setMessages((prev) => [...prev, userMsg]);
      setIsStreaming(true);

      const assistantId = crypto.randomUUID();
      setMessages((prev) => [
        ...prev,
        {
          id: assistantId,
          role: "assistant",
          content: "",
          createdAt: new Date().toISOString(),
        },
      ]);

      let assistantContent = "";
      let sources: Source[] = [];
      let resolvedSessionId = sessionId;

      const connection = hub ?? ensureConnection();

      // Đảm bảo kết nối đã thiết lập trước khi gọi stream (tránh race với
      // connection.start() chạy ngầm sau khi mount).
      try {
        await connection.start();
      } catch {
        // start ném lỗi sẽ được bắt bởi .subscribe({error}) dưới đây.
      }

      try {
        subscriptionRef.current = connection
          .stream<ChatEvent>("StreamChat", {
            sessionId: sessionId,
            message: text,
          })
          .subscribe({
            next: (event: ChatEvent) => {
              switch (event.type) {
                case "token":
                  if (event.content) {
                    assistantContent += event.content;
                    setMessages((prev) =>
                      prev.map((m) =>
                        m.id === assistantId
                          ? { ...m, content: assistantContent }
                          : m
                      )
                    );
                  }
                  break;
                case "citations":
                  sources = event.sources ?? [];
                  break;
                case "done":
                  resolvedSessionId =
                    event.sessionId ?? resolvedSessionId;
                  if (resolvedSessionId) {
                    setSessionId(resolvedSessionId);
                    sessionStorage.setItem(
                      "admission_session_id",
                      resolvedSessionId
                    );
                  }
                  break;
                case "error":
                  assistantContent += `\n\nLỗi: ${event.error}`;
                  setMessages((prev) =>
                    prev.map((m) =>
                      m.id === assistantId
                        ? { ...m, content: assistantContent }
                        : m
                    )
                  );
                  setStreamError(event.error);
                  break;
              }
            },
            error: (err: unknown) => {
              console.error("Stream error", err);
              assistantContent += `\n\nLỗi: Không thể kết nối đến máy chủ.`;
              setMessages((prev) =>
                prev.map((m) =>
                  m.id === assistantId
                    ? { ...m, content: assistantContent }
                    : m
                )
              );
              setStreamError("Không thể kết nối đến máy chủ.");
              setIsStreaming(false);
              subscriptionRef.current = null;
            },
            complete: () => {
              setMessages((prev) =>
                prev.map((m) =>
                  m.id === assistantId
                    ? { ...m, content: assistantContent, sources }
                    : m
                )
              );
              setIsStreaming(false);
              subscriptionRef.current = null;
            },
          });
      } catch (err) {
        console.error("Failed to start stream", err);
        assistantContent = "Không thể bắt đầu stream.";
        setMessages((prev) =>
          prev.map((m) =>
            m.id === assistantId ? { ...m, content: assistantContent } : m
          )
        );
        setStreamError("Không thể bắt đầu stream.");
        setIsStreaming(false);
      }
    },
    [sessionId]
  );

  return {
    messages,
    isStreaming,
    sessionId,
    streamError,
    sendMessage,
    stopStream,
  };
}
