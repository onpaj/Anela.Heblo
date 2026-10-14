import React from 'react';
import type { QuestionTagStats } from '../../api/hooks/useKnowledgeBase';
import type { TagCount } from './ragFeedbackTypes';

const TOP_TAGS_SHOWN = 10;

interface KbTagFrequencyPanelProps {
  stats?: QuestionTagStats;
  isLoading: boolean;
  onProblemClick: (tag: string) => void;
  onProductClick: (tag: string) => void;
}

interface TagColumnProps {
  title: string;
  items: TagCount[];
  onClick?: (tag: string) => void;
}

function TagColumn({ title, items, onClick }: TagColumnProps) {
  return (
    <div className="min-w-0">
      <h3 className="text-xs font-semibold uppercase text-gray-500 dark:text-graphite-muted mb-2">{title}</h3>
      {items.length === 0 ? (
        <p className="text-sm text-gray-400 dark:text-graphite-muted">—</p>
      ) : (
        <ul className="space-y-1">
          {items.slice(0, TOP_TAGS_SHOWN).map((item) => {
            const content = (
              <>
                <span className="truncate">{item.label}</span>
                <span className="ml-2 text-gray-500 dark:text-graphite-muted tabular-nums">{item.count}</span>
              </>
            );
            return (
              <li key={item.tag}>
                {onClick ? (
                  <button type="button" onClick={() => onClick(item.tag)}
                    className="w-full flex justify-between text-left text-sm text-blue-700 hover:underline dark:text-graphite-accent">
                    {content}
                  </button>
                ) : (
                  <div className="flex justify-between text-sm text-gray-800 dark:text-graphite-text">{content}</div>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}

function KbTagFrequencyPanel({ stats, isLoading, onProblemClick, onProductClick }: KbTagFrequencyPanelProps) {
  if (isLoading) {
    return <div className="text-sm text-gray-500 dark:text-graphite-muted">Načítám štítky…</div>;
  }

  const isEmpty =
    !stats || (stats.problemTags.length === 0 && stats.productTags.length === 0 && stats.recommendedProducts.length === 0);
  if (isEmpty) {
    return <div className="text-sm text-gray-500 dark:text-graphite-muted">Zatím žádné štítky v tomto období.</div>;
  }

  return (
    <div className="grid grid-cols-1 md:grid-cols-3 gap-4 rounded-lg border border-gray-200 dark:border-graphite-border p-4">
      <TagColumn title="Nejčastější problémy" items={stats.problemTags} onClick={onProblemClick} />
      <TagColumn title="Produkty v dotazech" items={stats.productTags} onClick={onProductClick} />
      <TagColumn title="Doporučené produkty" items={stats.recommendedProducts} />
    </div>
  );
}

export default KbTagFrequencyPanel;
