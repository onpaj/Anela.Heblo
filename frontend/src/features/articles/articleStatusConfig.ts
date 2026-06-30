import { ArticleStatus } from '../../api/generated/api-client';

export const ARTICLE_STATUS_LABELS: Record<ArticleStatus, string> = {
  [ArticleStatus.Queued]: 'Ve frontě',
  [ArticleStatus.Researching]: 'Výzkum — shromažďuji fakta…',
  [ArticleStatus.Writing]: 'Psaní — generuji obsah…',
  [ArticleStatus.Generated]: 'Vygenerováno',
  [ArticleStatus.Failed]: 'Generování selhalo',
};

export const ARTICLE_STATUS_COLORS: Record<ArticleStatus, string> = {
  [ArticleStatus.Queued]: 'bg-gray-100 text-gray-700 dark:bg-graphite-surface-2 dark:text-graphite-muted',
  [ArticleStatus.Researching]: 'bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300',
  [ArticleStatus.Writing]: 'bg-purple-100 text-purple-700 dark:bg-purple-900/30 dark:text-purple-300',
  [ArticleStatus.Generated]: 'bg-green-100 text-green-700 dark:bg-emerald-900/30 dark:text-emerald-300',
  [ArticleStatus.Failed]: 'bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-300',
};
