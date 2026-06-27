import { Loader2 } from 'lucide-react';
import { ArticleListItem, IN_PROGRESS_STATUSES } from '../../api/hooks/useArticles';
import { ARTICLE_STATUS_LABELS, ARTICLE_STATUS_COLORS } from './articleStatusConfig';

interface ArticleListProps {
  items: ArticleListItem[];
  isLoading: boolean;
  selectedId: string | null;
  onSelect: (id: string) => void;
}

const DATE_FORMATTER = new Intl.DateTimeFormat('cs-CZ', {
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
});

function formatDate(iso: string): string {
  return DATE_FORMATTER.format(new Date(iso));
}

export default function ArticleList({ items, isLoading, selectedId, onSelect }: ArticleListProps) {
  if (isLoading) {
    return (
      <div className="flex justify-center py-8">
        <Loader2 className="w-6 h-6 animate-spin text-gray-400 dark:text-graphite-faint" />
      </div>
    );
  }

  if (items.length === 0) {
    return (
      <p className="text-sm text-gray-500 dark:text-graphite-muted py-4 text-center">
        Zatím žádné články. Vytvořte první pomocí formuláře.
      </p>
    );
  }

  return (
    <ul className="divide-y divide-gray-100 dark:divide-graphite-border">
      {items.map((item) => (
        <li key={item.id}>
          <button
            onClick={() => onSelect(item.id)}
            className={`w-full text-left px-3 py-3 hover:bg-gray-50 dark:hover:bg-white/5 transition-colors ${
              selectedId === item.id ? 'bg-blue-50 dark:bg-graphite-accent/10' : ''
            }`}
          >
            <div className="flex items-start justify-between gap-2">
              <div className="flex-1 min-w-0">
                <p className="text-sm font-medium text-gray-900 dark:text-graphite-text truncate">
                  {item.title ?? item.topic}
                </p>
                {item.title && (
                  <p className="text-xs text-gray-500 dark:text-graphite-muted truncate">{item.topic}</p>
                )}
                <p className="text-xs text-gray-400 dark:text-graphite-faint mt-1">{formatDate(item.createdAt)}</p>
              </div>
              <span
                className={`shrink-0 inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium ${ARTICLE_STATUS_COLORS[item.status]}`}
              >
                {IN_PROGRESS_STATUSES.has(item.status) && (
                  <Loader2 className="w-3 h-3 animate-spin" />
                )}
                {ARTICLE_STATUS_LABELS[item.status]}
              </span>
            </div>
          </button>
        </li>
      ))}
    </ul>
  );
}
