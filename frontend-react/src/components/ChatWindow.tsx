import { MessageBubble } from "./MessageBubble";
import { CitationBadges } from "./CitationBadges";
import type { ChatMessage } from "@/types";

interface Props {
  messages: ChatMessage[];
  isStreaming: boolean;
}

export default function ChatWindow({ messages, isStreaming }: Props) {
  return (
    <div className="flex-1 overflow-y-auto p-4 space-y-4 md:p-6">
      {messages.length === 0 && (
        <div className="flex flex-col items-center justify-center h-full text-muted-foreground">
          <div className="text-text-base">Chào bạn! Hãy hỏi về điểm chuẩn, học phí hoặc ngành tuyển sinh.</div>
        </div>
      )}
      {messages.map((m) => (
        <div key={m.id} className={`flex ${m.role === "user" ? "justify-end" : "justify-start"}`}>
          <div
            className={`max-w-[85%] p-3 rounded-xl ${
              m.role === "user"
                ? "bg-primary text-primary-foreground shadow-sm"
                : "bg-card border border-border text-card-foreground shadow-sm"
            }`}
          >
            <MessageBubble message={m.content} />
            {m.role === "assistant" && m.sources && m.sources.length > 0 && (
              <CitationBadges sources={m.sources} />
            )}
          </div>
        </div>
      ))}
      {isStreaming && (
        <div className="flex justify-start">
          <div className="bg-card border border-border p-3 rounded-xl shadow-sm animate-pulse">
            <span className="text-text-small text-muted-foreground">Đang trả lời…</span>
          </div>
        </div>
      )}
    </div>
  );
}
