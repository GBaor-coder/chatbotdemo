interface Chip {
  id: string;
  question: string;
}

interface Props {
  chips: Chip[];
  onSelect: (question: string) => void;
}

export default function SuggestionChips({ chips, onSelect }: Props) {
  return (
    <div className="flex flex-wrap gap-2">
      {chips.map((c) => (
        <button
          key={c.id}
          onClick={() => onSelect(c.question)}
          className="px-3 py-1.5 bg-secondary text-secondary-foreground border border-border rounded-full text-text-control font-medium hover:bg-accent hover:text-accent-foreground hover:border-primary transition-colors"
        >
          {c.question}
        </button>
      ))}
    </div>
  );
}
