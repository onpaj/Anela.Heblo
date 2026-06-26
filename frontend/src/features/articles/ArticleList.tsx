import { Loader2 } from 'lucide-react';
import { ArticleListItem, IN_PROGRESS_STATUSES } from '../../api/hooks/useArticles';
import { ArticleStatus } from '../../api/generated/api-client';

interface ArticleListProps {
  items: ArticleListItem[];
  isLoading: boolean;
  selectedId: string | null;
  onSelect: (id: string) => void;
}

const STATUS_LABELS: Record<ArticleStatus, string> = {
  [ArticleStatus.Queued]: 'Ve frontě',
  [ArticleStatus.Researching]: 'Výzkum',
  [ArticleStatus.Writing]: 'Psaní',
  [ArticleStatus.Generated]: 'Vygenerováno',
  [ArticleStatus.Failed]: 'Chyba',
};

const STATUS_COLORS: Record<ArticleStatus, string> = {
  [ArticleStatus.Queued]: 'bg-gray-100 text-gray-700 dark:bg-graphite-surface-2 dark:text-graphite-muted',
  [ArticleStatus.Researching]: 'bg-blue-100 text-blue-700 dark:bg-graphite-surface-2 dark:text-graphite-accent',
  [ArticleStatus.Writing]: 'bg-purple-100 text-purple-700 dark:bg-graphite-surface-2 dark:text-graphite-accent-strong',
  [ArticleStatus.Generated]: 'bg-green-100 text-green-700 dark:bg-graphite-surface-2 dark:text-graphite-text',
  [ArticleStatus.Failed]: 'bg-red-100 text-red-700 dark:bg-red-950/40 dark:text-red-400',
};

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
      <p className="text-sm text-gray-500 dark:text-graphite-faint py-4 text-center">
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
            className={`w-full text-left px-3 py-3 hover:bg-gray-50 dark:hover:bg-graphite-hover transition-colors ${
              selectedId === item.id ? 'bg-blue-50 dark:bg-graphite-surface' : ''
            }`}
          >
            <div className="flex items-start justify-between gap-2">
              <div className="flex-1 min-w-0">
                <p className="text-sm font-medium text-gray-900 dark:text-graphite-text truncate">
                  {item.title ?? item.topic}
                </p>
                {item.title && (
                  <p className="text-xs text-gray-500 dark:text-graphite-faint truncate">{item.topic}</p>
                )}
                <p className="text-xs text-gray-400 dark:text-graphite-faint mt-1">{formatDate(item.createdAt)}</p>
              </div>
              <span
                className={`shrink-0 inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium ${STATUS_COLORS[item.status]}`}
              >
                {IN_PROGRESS_STATUSES.has(item.status) && (
                  <Loader2 className="w-3 h-3 animate-spin" />
                )}
                {STATUS_LABELS[item.status]}
              </span>
            </div>
          </button>
        </li>
      ))}
    </ul>
  );
}
