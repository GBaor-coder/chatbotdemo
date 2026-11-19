export interface ChatMessage {
  id: string;
  role: "user" | "assistant";
  content: string;
  sources?: Source[];
  createdAt?: string;
}

export interface Source {
  id?: string;
  title?: string;
  source?: string;
  similarity?: number;
}

export type ChatEvent =
  | { type: "token"; content: string; sessionId?: string }
  | { type: "citations"; sources?: Source[]; sessionId?: string }
  | { type: "done"; sessionId: string; done: boolean }
  | { type: "error"; error: string; sessionId?: string };
