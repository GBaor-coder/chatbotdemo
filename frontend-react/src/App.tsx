import { useState, useEffect } from "react";
import { useChatStream } from "@/hooks/useChatStream";
import ChatWindow from "@/components/ChatWindow";
import SuggestionChips from "@/components/SuggestionChips";
import { Send, Menu, X } from "lucide-react";

const SUGGESTIONS: { id: string; question: string }[] = [
  { id: "s1", question: "Điểm chuẩn ngành Công nghệ thông tin 2024 là bao nhiêu?" },
  { id: "s2", question: "Học phí ngành Kinh doanh năm nay thế nào?" },
  { id: "s3", question: "Phương thức xét tuyển cho ngành Ngôn ngữ Anh như thế nào?" },
  { id: "s4", question: "Lịch trình tuyển sinh 2026 bắt đầu khi nào?" },
];

export default function App() {
  const [input, setInput] = useState("");
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const { messages, isStreaming, sendMessage } = useChatStream();

  useEffect(() => {
    setSidebarOpen(typeof window !== "undefined" && window.innerWidth >= 768);
  }, []);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!input.trim()) return;
    await sendMessage(input);
    setInput("");
    if (typeof window !== "undefined" && window.innerWidth < 768) setSidebarOpen(false);
  };

  const handleSuggestion = (question: string) => {
    setInput(question);
    handleSubmit((new Event("submit") as unknown) as React.FormEvent);
  };

  return (
    <div className="flex h-screen bg-page text-foreground">
      <aside
        className={`fixed inset-y-0 left-0 z-chrome w-80 bg-sidebar-gradient text-sidebar-foreground shadow-lg transform transition-transform duration-300 ease-in-out ${
          sidebarOpen ? "translate-x-0" : "-translate-x-full"
        } md:relative md:translate-x-0 md:fixed md:inset-y-0 md:left-0 md:w-80 md:shadow-none md:border-r md:border-sidebar-border`}
      >
        <div className="flex items-center gap-2 p-6">
          <div className="w-8 h-8 rounded-lg bg-brand-gradient flex items-center justify-center">
            <Send className="w-5 h-5 text-white" />
          </div>
          <h1 className="font-bold text-title tracking-tight">Tư vấn tuyển sinh</h1>
          {typeof window !== "undefined" && window.innerWidth < 768 && sidebarOpen && (
            <button
              onClick={() => setSidebarOpen(false)}
              className="ml-auto p-2 text-sidebar-foreground hover:opacity-80"
            >
              <X className="w-5 h-5" />
            </button>
          )}
        </div>
        <p className="px-6 text-text-small text-subtle-foreground mb-6">
          Hỏi về ngành học, điểm chuẩn, học phí hoặc phương thức xét tuyển.
        </p>
        <nav className="px-6">
          <SuggestionChips chips={SUGGESTIONS} onSelect={handleSuggestion} />
        </nav>
      </aside>

      {typeof window !== "undefined" && window.innerWidth < 768 && sidebarOpen && (
        <div
          className="fixed inset-0 z-chrome-top bg-black/40 backdrop-blur-sm"
          onClick={() => setSidebarOpen(false)}
        />
      )}

      <main className="flex-1 flex flex-col md:ml-0 md:max-w-4xl md:mx-auto w-full">
        <button
          onClick={() => setSidebarOpen(!sidebarOpen)}
          className="md:hidden p-3 text-muted-foreground hover:text-foreground"
        >
          <Menu className="w-5 h-5" />
        </button>
        <ChatWindow messages={messages} isStreaming={isStreaming} />
        <form onSubmit={handleSubmit} className="p-4 border-t border-border bg-card">
          <div className="flex gap-2">
            <input
              value={input}
              onChange={(e) => setInput(e.target.value)}
              disabled={isStreaming}
              placeholder="Nhập câu hỏi về tuyển sinh…"
              className="flex-1 px-4 py-3 border border-field-border rounded-lg text-base bg-background text-foreground focus:ring-2 focus:ring-ring focus:ring-offset-0 outline-none disabled:opacity-50 transition-colors"
            />
            <button
              type="submit"
              disabled={isStreaming || !input.trim()}
              className="px-6 py-3 bg-primary text-primary-foreground rounded-lg font-medium disabled:opacity-50 hover:opacity-90 transition-all flex items-center gap-2 shadow-sm focus:ring-2 focus:ring-ring focus:ring-offset-2"
            >
              <Send className="w-4 h-4" /> Gửi
            </button>
          </div>
        </form>
      </main>
    </div>
  );
}
