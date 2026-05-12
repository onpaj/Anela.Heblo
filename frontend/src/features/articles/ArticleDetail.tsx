import { Loader2 } from 'lucide-react';
import { ArticleDetail as ArticleDetailType, useGetArticleQuery, IN_PROGRESS_STATUSES } from '../../api/hooks/useArticles';
import { ArticleStatus } from '../../api/generated/api-client';
import ArticleSourceList from './ArticleSourceList';
import ArticleFeedbackSection from './ArticleFeedbackSection';
import ArticleDebugPanel from './ArticleDebugPanel';

interface ArticleDetailProps {
  articleId: string;
}

const STATUS_LABELS: Record<ArticleStatus, string> = {
  [ArticleStatus.Queued]: 'Ve frontě',
  [ArticleStatus.Researching]: 'Výzkum — shromažďuji fakta…',
  [ArticleStatus.Writing]: 'Psaní — generuji obsah…',
  [ArticleStatus.Generated]: 'Vygenerováno',
  [ArticleStatus.Failed]: 'Generování selhalo',
};

const STATUS_COLORS: Record<ArticleStatus, string> = {
  [ArticleStatus.Queued]: 'bg-gray-100 text-gray-700',
  [ArticleStatus.Researching]: 'bg-blue-100 text-blue-700',
  [ArticleStatus.Writing]: 'bg-purple-100 text-purple-700',
  [ArticleStatus.Generated]: 'bg-green-100 text-green-700',
  [ArticleStatus.Failed]: 'bg-red-100 text-red-700',
};

function HtmlContent({ html }: { html: string }) {
  const srcdoc = `<!DOCTYPE html><html><head><meta charset="utf-8"><style>
    body{font-family:system-ui,sans-serif;line-height:1.6;color:#1f2937;padding:1rem;margin:0}
    h1,h2,h3{color:#111827}p{margin:0 0 1em}ul,ol{padding-left:1.5em}
    a{color:#2563eb}
  </style></head><body>${html}</body></html>`;

  return (
    <iframe
      srcDoc={srcdoc}
      sandbox="allow-same-origin"
      className="w-full border-0 rounded"
      style={{ minHeight: '500px' }}
      onLoad={(e) => {
        const iframe = e.currentTarget;
        const body = iframe.contentDocument?.body;
        if (body) {
          iframe.style.height = `${body.scrollHeight + 32}px`;
        }
      }}
      title="Obsah článku"
    />
  );
}

function InProgressView({ article }: { article: ArticleDetailType }) {
  return (
    <div className="flex flex-col items-center justify-center py-16 gap-4 text-gray-500">
      <Loader2 className="w-8 h-8 animate-spin text-blue-500" />
      <p className="text-sm">{STATUS_LABELS[article.status]}</p>
    </div>
  );
}

function ArticleView({ article }: { article: ArticleDetailType }) {
  return (
    <div>
      <div className="mb-4">
        {article.title && (
          <h2 className="text-xl font-semibold text-gray-900 mb-1">{article.title}</h2>
        )}
        <p className="text-sm text-gray-500">{article.topic}</p>
        <div className="flex flex-wrap gap-2 mt-2 text-xs text-gray-500">
          <span>{article.scope}</span>
          <span>·</span>
          <span>{article.length}</span>
          {article.useKnowledgeBase && <span>· Znalostní báze</span>}
          {article.useWebSearch && <span>· Webové vyhledávání</span>}
        </div>
      </div>

      {article.htmlContent && <HtmlContent html={article.htmlContent} />}
      <ArticleSourceList sources={article.sources} />
      <ArticleFeedbackSection article={article} />
    </div>
  );
}

export default function ArticleDetail({ articleId }: ArticleDetailProps) {
  const { data: article, isLoading, error } = useGetArticleQuery(articleId);

  if (isLoading) {
    return (
      <div className="flex justify-center py-8">
        <Loader2 className="w-6 h-6 animate-spin text-gray-400" />
      </div>
    );
  }

  if (error || !article) {
    return <p className="text-sm text-red-600 py-4">Článek se nepodařilo načíst.</p>;
  }

  return (
    <div>
      <div className="flex items-center gap-2 mb-4">
        <span
          className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium ${STATUS_COLORS[article.status]}`}
        >
          {IN_PROGRESS_STATUSES.has(article.status) && (
            <Loader2 className="w-3 h-3 animate-spin" />
          )}
          {STATUS_LABELS[article.status]}
        </span>
      </div>

      {article.status === ArticleStatus.Failed && article.errorMessage && (
        <div className="mb-4 rounded bg-red-50 border border-red-200 p-3 text-sm text-red-700">
          {article.errorMessage}
        </div>
      )}

      {IN_PROGRESS_STATUSES.has(article.status) && <InProgressView article={article} />}
      {article.status === ArticleStatus.Generated && <ArticleView article={article} />}
      {!IN_PROGRESS_STATUSES.has(article.status) && <ArticleDebugPanel articleId={article.id} />}
    </div>
  );
}
