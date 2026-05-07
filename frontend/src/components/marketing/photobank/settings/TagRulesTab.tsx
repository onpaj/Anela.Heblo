import { useState } from 'react';
import { Loader2, Trash2 } from 'lucide-react';
import {
  useTagRules,
  useAddTagRule,
  useDeleteTagRule,
  useReapplyTagRules,
} from '../../../../api/hooks/usePhotobankSettings';

const TagRulesTab: React.FC = () => {
  const { data: rules = [], isLoading } = useTagRules();
  const addRule = useAddTagRule();
  const deleteRule = useDeleteTagRule();
  const reapplyRules = useReapplyTagRules();

  const [pathPattern, setPathPattern] = useState('');
  const [tagName, setTagName] = useState('');
  const [sortOrder, setSortOrder] = useState(0);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [deletingId, setDeletingId] = useState<number | null>(null);
  const [reapplyMessage, setReapplyMessage] = useState<string | null>(null);
  const [reapplyError, setReapplyError] = useState<string | null>(null);

  const isFormValid = pathPattern.trim() !== '' && tagName.trim() !== '';

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!isFormValid) return;
    setSubmitError(null);
    try {
      await addRule.mutateAsync({
        pathPattern: pathPattern.trim(),
        tagName: tagName.trim(),
        sortOrder,
      });
      setPathPattern('');
      setTagName('');
      setSortOrder(0);
    } catch {
      setSubmitError('Nepodařilo se přidat pravidlo. Zkuste to znovu.');
    }
  };

  const handleDelete = (id: number) => {
    setDeletingId(id);
    deleteRule.mutate(id, { onSettled: () => setDeletingId(null) });
  };

  const handleReapply = async () => {
    setReapplyMessage(null);
    setReapplyError(null);
    try {
      const result = await reapplyRules.mutateAsync();
      setReapplyMessage(`Pravidla aplikována na ${result.photosUpdated} fotek`);
      const timer = setTimeout(() => setReapplyMessage(null), 5000);
      return () => clearTimeout(timer);
    } catch {
      setReapplyError('Chyba při aplikaci pravidel');
    }
  };

  const sortedRules = [...rules].sort((a, b) => a.sortOrder - b.sortOrder);

  if (isLoading) {
    return <div className="text-sm text-gray-500">Načítání...</div>;
  }

  return (
    <div className="space-y-6">
      {sortedRules.length === 0 ? (
        <p className="text-sm text-gray-500">Žádná pravidla nejsou nakonfigurována.</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="min-w-full text-sm divide-y divide-gray-200">
            <thead>
              <tr className="text-left text-xs font-semibold text-gray-500 uppercase tracking-wider">
                <th className="py-2 pr-4">Vzor cesty</th>
                <th className="py-2 pr-4">Štítek</th>
                <th className="py-2 pr-4">Pořadí</th>
                <th className="py-2 pr-4">Aktivní</th>
                <th className="py-2"></th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {sortedRules.map((rule) => (
                <tr key={rule.id}>
                  <td className="py-2 pr-4 font-mono text-xs">{rule.pathPattern}</td>
                  <td className="py-2 pr-4">{rule.tagName}</td>
                  <td className="py-2 pr-4">{rule.sortOrder}</td>
                  <td className="py-2 pr-4">
                    <span
                      className={`px-1.5 py-0.5 rounded text-xs ${
                        rule.isActive
                          ? 'bg-green-100 text-green-800'
                          : 'bg-gray-100 text-gray-600'
                      }`}
                    >
                      {rule.isActive ? 'Ano' : 'Ne'}
                    </span>
                  </td>
                  <td className="py-2">
                    <button
                      onClick={() => handleDelete(rule.id)}
                      disabled={deletingId === rule.id}
                      className="p-1 text-gray-400 hover:text-red-500 rounded disabled:opacity-50"
                      aria-label={`Smazat pravidlo ${rule.pathPattern}`}
                    >
                      <Trash2 className="w-4 h-4" />
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <div className="flex items-center gap-3">
        <button
          onClick={handleReapply}
          disabled={reapplyRules.isPending}
          className="flex items-center gap-1.5 px-3 py-1.5 text-sm border border-gray-300 rounded-md hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {reapplyRules.isPending && <Loader2 className="w-4 h-4 animate-spin" />}
          Re-aplikovat pravidla
        </button>
        {reapplyMessage && (
          <span className="text-sm text-green-700">{reapplyMessage}</span>
        )}
        {reapplyError && (
          <span className="text-sm text-red-600">{reapplyError}</span>
        )}
      </div>

      <form onSubmit={handleSubmit} className="space-y-3 border-t border-gray-200 pt-4">
        <h3 className="text-sm font-semibold text-gray-700">Přidat pravidlo</h3>
        <div className="grid grid-cols-2 gap-3">
          <div>
            <label htmlFor="pathPattern" className="block text-xs text-gray-500 mb-1">
              Vzor cesty *
            </label>
            <input
              id="pathPattern"
              type="text"
              value={pathPattern}
              onChange={(e) => setPathPattern(e.target.value)}
              placeholder="/Fotky/Produkty/*"
              required
              className="w-full px-2 py-1.5 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-primary-blue focus:border-transparent"
            />
          </div>
          <div>
            <label htmlFor="tagName" className="block text-xs text-gray-500 mb-1">
              Štítek *
            </label>
            <input
              id="tagName"
              type="text"
              value={tagName}
              onChange={(e) => setTagName(e.target.value)}
              placeholder="produkty"
              required
              className="w-full px-2 py-1.5 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-primary-blue focus:border-transparent"
            />
          </div>
          <div>
            <label htmlFor="sortOrder" className="block text-xs text-gray-500 mb-1">
              Pořadí
            </label>
            <input
              id="sortOrder"
              type="number"
              value={sortOrder}
              onChange={(e) => setSortOrder(Number(e.target.value))}
              className="w-full px-2 py-1.5 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-primary-blue focus:border-transparent"
            />
          </div>
        </div>
        <button
          type="submit"
          disabled={addRule.isPending || !isFormValid}
          className="px-3 py-1.5 text-sm bg-primary-blue text-white rounded-md hover:opacity-90 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {addRule.isPending ? 'Přidávám...' : 'Přidat pravidlo'}
        </button>
        {submitError && (
          <p className="text-red-600 text-sm mt-2">{submitError}</p>
        )}
      </form>
    </div>
  );
};

export default TagRulesTab;
