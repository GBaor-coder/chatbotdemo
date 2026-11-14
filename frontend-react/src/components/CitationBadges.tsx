interface Source {
  id?: string;
  title?: string;
  source?: string;
  similarity?: number;
}

interface Props {
  sources: Source[];
}

export function CitationBadges({ sources }: Props) {
  if (!sources || sources.length === 0) return null;

  return (
    <div className="mt-2 flex flex-wrap gap-1">
      {sources.map((s, i) => (
        <span
          key={s.id ?? i}
          className="text-text-micro bg-accent text-accent-foreground px-2 py-1 rounded border border-border font-medium"
        >
          {s.title ?? s.source ?? `Nguồn ${i + 1}`}
        </span>
      ))}
    </div>
  );
}
