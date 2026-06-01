import { useState } from 'react';
import { Loader2 } from 'lucide-react';
import { GenerateArticleRequest } from '../../api/generated/api-client';
import { useGenerateArticleMutation } from '../../api/hooks/useArticles';
import { useMarketingWriterPermission } from '../../api/hooks/useMarketingWriterPermission';

interface ArticleGenerationFormProps {
  onArticleCreated: (articleId: string) => void;
}

const SCOPE_OPTIONS = [
  { value: 'overview', label: 'Přehled' },
  { value: 'deep-dive', label: 'Hloubková analýza' },
  { value: 'how-to', label: 'Návod' },
  { value: 'comparison', label: 'Srovnání' },
];

const LENGTH_OPTIONS = [
  { value: 'brief (500w)', label: 'Krátký (~500 slov)' },
  { value: 'medium (1000w)', label: 'Střední (~1000 slov)' },
  { value: 'long (2000w)', label: 'Dlouhý (~2000 slov)' },
];

export default function ArticleGenerationForm({ onArticleCreated }: ArticleGenerationFormProps) {
  const canGenerate = useMarketingWriterPermission();
  const { mutate: generate, isPending, error } = useGenerateArticleMutation();

  const [topic, setTopic] = useState('');
  const [scope, setScope] = useState('overview');
  const [length, setLength] = useState('medium (1000w)');
  const [audience, setAudience] = useState('');
  const [angle, setAngle] = useState('');
  const [languageNote, setLanguageNote] = useState('');
  const [useKnowledgeBase, setUseKnowledgeBase] = useState(true);
  const [useWebSearch, setUseWebSearch] = useState(true);
  const [styleGuideDriveId, setStyleGuideDriveId] = useState('');
  const [styleGuideItemPath, setStyleGuideItemPath] = useState('');

  const trimmedTopic = topic.trim();
  const isValid = trimmedTopic.length >= 3 && trimmedTopic.length <= 500;

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!isValid || isPending) return;

    const request = new GenerateArticleRequest({
      topic: trimmedTopic,
      scope,
      length,
      audience: audience.trim() || undefined,
      angle: angle.trim() || undefined,
      languageNote: languageNote.trim() || undefined,
      useKnowledgeBase,
      useWebSearch,
      styleGuideDriveId: styleGuideDriveId.trim() || undefined,
      styleGuideItemPath: styleGuideItemPath.trim() || undefined,
    });

    generate(request, {
      onSuccess: (articleId) => {
        if (articleId) onArticleCreated(articleId);
      },
    });
  };

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      <div>
        <label className="block text-sm font-medium text-gray-700 mb-1">
          Téma <span className="text-red-500">*</span>
        </label>
        <input
          type="text"
          value={topic}
          onChange={(e) => setTopic(e.target.value)}
          placeholder="Např. Výhody fermentovaných surovin pro pleť"
          minLength={3}
          maxLength={500}
          required
          className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
        />
      </div>

      <div className="grid grid-cols-2 gap-3">
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Rozsah</label>
          <select
            value={scope}
            onChange={(e) => setScope(e.target.value)}
            className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          >
            {SCOPE_OPTIONS.map((opt) => (
              <option key={opt.value} value={opt.value}>{opt.label}</option>
            ))}
          </select>
        </div>

        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Délka</label>
          <select
            value={length}
            onChange={(e) => setLength(e.target.value)}
            className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          >
            {LENGTH_OPTIONS.map((opt) => (
              <option key={opt.value} value={opt.value}>{opt.label}</option>
            ))}
          </select>
        </div>
      </div>

      <div>
        <label className="block text-sm font-medium text-gray-700 mb-1">Cílová skupina</label>
        <input
          type="text"
          value={audience}
          onChange={(e) => setAudience(e.target.value)}
          placeholder="Např. zákazníci 30–50 let zajímající se o přírodní kosmetiku"
          className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
        />
      </div>

      <div>
        <label className="block text-sm font-medium text-gray-700 mb-1">Úhel pohledu</label>
        <input
          type="text"
          value={angle}
          onChange={(e) => setAngle(e.target.value)}
          placeholder="Např. zaměřit se na vědecké studie"
          className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
        />
      </div>

      <div>
        <label className="block text-sm font-medium text-gray-700 mb-1">Poznámka k tónu / jazyku</label>
        <input
          type="text"
          value={languageNote}
          onChange={(e) => setLanguageNote(e.target.value)}
          placeholder="Např. krátké věty, vyhýbat se odborným termínům"
          maxLength={500}
          className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
        />
      </div>

      <div className="flex gap-6">
        <label className="flex items-center gap-2 text-sm text-gray-700 cursor-pointer">
          <input
            type="checkbox"
            checked={useKnowledgeBase}
            onChange={(e) => setUseKnowledgeBase(e.target.checked)}
            className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
          />
          Znalostní báze
        </label>
        <label className="flex items-center gap-2 text-sm text-gray-700 cursor-pointer">
          <input
            type="checkbox"
            checked={useWebSearch}
            onChange={(e) => setUseWebSearch(e.target.checked)}
            className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
          />
          Webové vyhledávání
        </label>
      </div>

      <details className="text-sm">
        <summary className="cursor-pointer text-gray-500 select-none">Stylový průvodce (volitelné)</summary>
        <div className="mt-2 space-y-2">
          <input
            type="text"
            value={styleGuideDriveId}
            onChange={(e) => setStyleGuideDriveId(e.target.value)}
            placeholder="OneDrive Drive ID"
            className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
          <input
            type="text"
            value={styleGuideItemPath}
            onChange={(e) => setStyleGuideItemPath(e.target.value)}
            placeholder="Cesta k souboru (např. Documents/style-guide.txt)"
            className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>
      </details>

      {error && (
        <p className="text-sm text-red-600">Generování selhalo. Zkuste to prosím znovu.</p>
      )}

      {!canGenerate && (
        <p className="text-sm text-amber-600">Nemáte oprávnění generovat články.</p>
      )}

      <button
        type="submit"
        disabled={!isValid || isPending || !canGenerate}
        className="w-full bg-blue-600 text-white rounded-md px-4 py-2 text-sm font-medium hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed flex items-center justify-center gap-2"
      >
        {isPending && <Loader2 className="w-4 h-4 animate-spin" />}
        {isPending ? 'Generuji...' : 'Generovat článek'}
      </button>
    </form>
  );
}
