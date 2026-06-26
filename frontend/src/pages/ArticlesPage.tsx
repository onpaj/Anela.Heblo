import { useState } from 'react';
import { FileText } from 'lucide-react';
import { useListArticlesQuery } from '../api/hooks/useArticles';
import ArticleGenerationForm from '../features/articles/ArticleGenerationForm';
import ArticleList from '../features/articles/ArticleList';
import ArticleDetail from '../features/articles/ArticleDetail';
import { useScreenView } from '../telemetry/useScreenView';

type Tab = 'new' | 'list';

export default function ArticlesPage() {
  const [activeTab, setActiveTab] = useState<Tab>('new');
  useScreenView('Marketing', 'Articles', activeTab === 'new' ? 'NewTab' : 'ListTab');
  const [selectedArticleId, setSelectedArticleId] = useState<string | null>(null);

  const { data: articles = [], isLoading } = useListArticlesQuery({ pageSize: 50 });

  const handleArticleCreated = (articleId: string) => {
    setSelectedArticleId(articleId);
    setActiveTab('list');
  };

  const tabs: { id: Tab; label: string }[] = [
    { id: 'new', label: 'Nový článek' },
    { id: 'list', label: `Články${articles.length > 0 ? ` (${articles.length})` : ''}` },
  ];

  return (
    <div className="p-6 space-y-4">
      <div className="flex items-center gap-3">
        <FileText className="w-6 h-6 text-blue-600 dark:text-graphite-accent" />
        <h1 className="text-2xl font-semibold text-gray-900 dark:text-graphite-text">Generátor článků</h1>
      </div>

      <div className="border-b border-gray-200 dark:border-graphite-border">
        <nav className="flex gap-6" aria-label="Tabs">
          {tabs.map((tab) => (
            <button
              key={tab.id}
              onClick={() => setActiveTab(tab.id)}
              className={`py-2 text-sm font-medium border-b-2 transition-colors ${
                activeTab === tab.id
                  ? 'border-blue-600 text-blue-600 dark:text-graphite-accent dark:border-graphite-accent'
                  : 'border-transparent text-gray-500 hover:text-gray-700 dark:text-graphite-muted'
              }`}
            >
              {tab.label}
            </button>
          ))}
        </nav>
      </div>

      <div className="pt-2">
        {activeTab === 'new' && (
          <div className="max-w-xl">
            <ArticleGenerationForm onArticleCreated={handleArticleCreated} />
          </div>
        )}

        {activeTab === 'list' && (
          <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
            <div className="border border-gray-200 dark:border-graphite-border rounded-lg overflow-hidden">
              <ArticleList
                items={articles}
                isLoading={isLoading}
                selectedId={selectedArticleId}
                onSelect={setSelectedArticleId}
              />
            </div>

            <div className="border border-gray-200 dark:border-graphite-border rounded-lg p-4 min-h-64">
              {selectedArticleId ? (
                <ArticleDetail articleId={selectedArticleId} />
              ) : (
                <p className="text-sm text-gray-400 dark:text-graphite-faint text-center py-8">
                  Vyberte článek ze seznamu
                </p>
              )}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
