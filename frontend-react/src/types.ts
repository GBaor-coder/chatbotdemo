export interface ChatMessage {
  id: string;
  role: "user" | "assistant";
  content: string;
  sources?: Array<{ id?: string; title?: string; source?: string; similarity?: number }>;
  createdAt?: string;
}
