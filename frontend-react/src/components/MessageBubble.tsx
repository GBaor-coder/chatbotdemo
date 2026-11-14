import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";

interface Props {
  message: string;
}

export function MessageBubble({ message }: Props) {
  return (
    <div className="whitespace-pre-wrap break-words text-text-base leading-relaxed">
      <ReactMarkdown remarkPlugins={[remarkGfm]}>{message}</ReactMarkdown>
    </div>
  );
}
