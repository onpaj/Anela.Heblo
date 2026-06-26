import { ArticleStatus } from '../../api/generated/api-client';

export const ARTICLE_STATUS_LABELS: Record<ArticleStatus, string> = {
  [ArticleStatus.Queued]: 'Ve frontě',
  [ArticleStatus.Researching]: 'Výzkum — shromažďuji fakta…',
  [ArticleStatus.Writing]: 'Psaní — generuji obsah…',
  [ArticleStatus.Generated]: 'Vygenerováno',
  [ArticleStatus.Failed]: 'Generování selhalo',
};

export const ARTICLE_STATUS_COLORS: Record<ArticleStatus, string> = {
  [ArticleStatus.Queued]: 'bg-gray-100 text-gray-700',
  [ArticleStatus.Researching]: 'bg-blue-100 text-blue-700',
  [ArticleStatus.Writing]: 'bg-purple-100 text-purple-700',
  [ArticleStatus.Generated]: 'bg-green-100 text-green-700',
  [ArticleStatus.Failed]: 'bg-red-100 text-red-700',
};
