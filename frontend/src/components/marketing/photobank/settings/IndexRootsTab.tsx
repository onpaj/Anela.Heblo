import { useState } from "react";
import { Trash2 } from "lucide-react";
import {
  useIndexRoots,
  useAddIndexRoot,
  useDeleteIndexRoot,
} from "../../../../api/hooks/usePhotobankSettings";

const IndexRootsTab: React.FC = () => {
  const { data: roots = [], isLoading } = useIndexRoots();
  const addRoot = useAddIndexRoot();
  const deleteRoot = useDeleteIndexRoot();

  const [folderPath, setFolderPath] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [driveId, setDriveId] = useState("");
  const [rootItemId, setRootItemId] = useState("");
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [deletingId, setDeletingId] = useState<number | null>(null);

  const isFormValid = folderPath.trim() !== "" && driveId.trim() !== "" && rootItemId.trim() !== "";

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!isFormValid) return;
    setSubmitError(null);
    try {
      await addRoot.mutateAsync({
        sharePointPath: folderPath.trim(),
        displayName: displayName.trim() || null,
        driveId: driveId.trim(),
        rootItemId: rootItemId.trim(),
      });
      setFolderPath("");
      setDisplayName("");
      setDriveId("");
      setRootItemId("");
    } catch {
      setSubmitError("Nepodařilo se přidat kořen. Zkuste to znovu.");
    }
  };

  const handleDelete = (id: number) => {
    setDeletingId(id);
    deleteRoot.mutate(id, { onSettled: () => setDeletingId(null) });
  };

  if (isLoading) {
    return <div className="text-sm text-gray-500">Načítání...</div>;
  }

  return (
    <div className="space-y-6">
      {roots.length === 0 ? (
        <p className="text-sm text-gray-500">Žádné kořeny nejsou nakonfigurovány.</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="min-w-full text-sm divide-y divide-gray-200">
            <thead>
              <tr className="text-left text-xs font-semibold text-gray-500 uppercase tracking-wider">
                <th className="py-2 pr-4">Cesta</th>
                <th className="py-2 pr-4">Název</th>
                <th className="py-2 pr-4">Drive ID</th>
                <th className="py-2 pr-4">Root Item ID</th>
                <th className="py-2 pr-4">Aktivní</th>
                <th className="py-2 pr-4">Poslední indexace</th>
                <th className="py-2"></th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {roots.map((root) => (
                <tr key={root.id}>
                  <td className="py-2 pr-4 font-mono text-xs">{root.sharePointPath}</td>
                  <td className="py-2 pr-4">{root.displayName ?? "—"}</td>
                  <td className="py-2 pr-4 font-mono text-xs">{root.driveId ?? "—"}</td>
                  <td className="py-2 pr-4 font-mono text-xs">{root.rootItemId ?? "—"}</td>
                  <td className="py-2 pr-4">
                    <span
                      className={`px-1.5 py-0.5 rounded text-xs ${
                        root.isActive
                          ? "bg-green-100 text-green-700"
                          : "bg-gray-100 text-gray-500"
                      }`}
                    >
                      {root.isActive ? "Ano" : "Ne"}
                    </span>
                  </td>
                  <td className="py-2 pr-4 text-xs text-gray-500">
                    {root.lastIndexedAt
                      ? new Date(root.lastIndexedAt).toLocaleDateString("cs-CZ")
                      : "Nikdy"}
                  </td>
                  <td className="py-2">
                    <button
                      onClick={() => handleDelete(root.id)}
                      disabled={deletingId === root.id}
                      className="text-gray-400 hover:text-red-500 disabled:opacity-50"
                      aria-label={`Smazat kořen ${root.sharePointPath}`}
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

      <form onSubmit={handleSubmit} className="space-y-3 border-t border-gray-200 pt-4">
        <h3 className="text-sm font-semibold text-gray-700">Přidat kořen</h3>
        <div className="grid grid-cols-2 gap-3">
          <div>
            <label htmlFor="folderPath" className="block text-xs text-gray-500 mb-1">Cesta *</label>
            <input
              id="folderPath"
              type="text"
              value={folderPath}
              onChange={(e) => setFolderPath(e.target.value)}
              placeholder="/sites/anela/Shared Documents/Fotky"
              required
              className="w-full px-2 py-1.5 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-primary-blue focus:border-transparent"
            />
          </div>
          <div>
            <label htmlFor="displayName" className="block text-xs text-gray-500 mb-1">Název (volitelný)</label>
            <input
              id="displayName"
              type="text"
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              placeholder="Fotky produktů"
              className="w-full px-2 py-1.5 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-primary-blue focus:border-transparent"
            />
          </div>
          <div>
            <label htmlFor="driveId" className="block text-xs text-gray-500 mb-1">Drive ID *</label>
            <input
              id="driveId"
              type="text"
              value={driveId}
              onChange={(e) => setDriveId(e.target.value)}
              placeholder="b!abc123..."
              required
              className="w-full px-2 py-1.5 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-primary-blue focus:border-transparent"
            />
          </div>
          <div>
            <label htmlFor="rootItemId" className="block text-xs text-gray-500 mb-1">Root Item ID *</label>
            <input
              id="rootItemId"
              type="text"
              value={rootItemId}
              onChange={(e) => setRootItemId(e.target.value)}
              placeholder="01ABCDEF..."
              required
              className="w-full px-2 py-1.5 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-primary-blue focus:border-transparent"
            />
          </div>
        </div>
        <button
          type="submit"
          disabled={addRoot.isPending || !isFormValid}
          className="px-3 py-1.5 text-sm bg-primary-blue text-white rounded-md hover:opacity-90 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {addRoot.isPending ? "Přidávám..." : "Přidat kořen"}
        </button>
        {submitError && (
          <p className="text-red-600 text-sm mt-2">{submitError}</p>
        )}
      </form>
    </div>
  );
};

export default IndexRootsTab;
