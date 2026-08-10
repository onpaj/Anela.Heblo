import React, { useState } from "react";
import { useNavigate } from "react-router-dom";
import { Plus, Trash2 } from "lucide-react";
import toast from "react-hot-toast";
import {
  MindMapListItem,
  useCreateMindMap,
  useDeleteMindMap,
  useMindMapsList,
} from "../../../../api/hooks/useMindMaps";
import { PAGE_CONTAINER_HEIGHT } from "../../../../constants/layout";
import { useScreenView } from "../../../../telemetry/useScreenView";

const STATUS_BADGE: Record<string, { label: string; className: string }> = {
  Idle: { label: "Aktuální", className: "bg-emerald-100 text-emerald-800 dark:bg-emerald-900/30 dark:text-emerald-300" },
  Updating: { label: "Aktualizuje se…", className: "bg-amber-100 text-amber-800 dark:bg-amber-900/30 dark:text-amber-300" },
  Failed: { label: "Chyba", className: "bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-300" },
};
const DEFAULT_STATUS_BADGE = { className: "bg-gray-100 text-gray-800 dark:bg-graphite-surface-2 dark:text-graphite-muted" };

const MindMapListPage: React.FC = () => {
  useScreenView("Automation", "MindMaps");
  const navigate = useNavigate();
  const { data, isLoading, error } = useMindMapsList();
  const createMap = useCreateMindMap();
  const deleteMap = useDeleteMindMap();
  const [isCreateOpen, setIsCreateOpen] = useState(false);
  const [newName, setNewName] = useState("");
  const [newDescription, setNewDescription] = useState("");

  const closeCreateDialog = () => {
    setIsCreateOpen(false);
    setNewName("");
    setNewDescription("");
  };

  const handleCreate = async () => {
    if (!newName.trim()) return;
    try {
      const result = await createMap.mutateAsync({
        name: newName.trim(),
        description: newDescription.trim() || null,
      });
      closeCreateDialog();
      navigate(`/automation/mind-maps/${result.id}`);
    } catch {
      toast.error("Vytvoření mapy se nezdařilo");
    }
  };

  const handleDelete = async (map: MindMapListItem) => {
    if (!window.confirm(`Smazat mapu „${map.name}"? Tato akce je nevratná.`)) return;
    try {
      await deleteMap.mutateAsync(map.id);
      toast.success("Mapa smazána");
    } catch {
      toast.error("Smazání mapy se nezdařilo");
    }
  };

  const items = data?.items ?? [];

  return (
    <div className="flex flex-col w-full" style={{ height: PAGE_CONTAINER_HEIGHT }}>
      <div className="flex-shrink-0 mb-3 px-4 sm:px-6 lg:px-8">
        <div className="flex items-center justify-between">
          <h1 className="text-3xl font-bold text-gray-900 dark:text-graphite-text">Myšlenkové mapy</h1>
          <button
            type="button"
            data-testid="mindmap-create-button"
            onClick={() => setIsCreateOpen(true)}
            className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md text-sm font-medium bg-indigo-600 text-white hover:bg-indigo-700 dark:bg-graphite-accent dark:hover:bg-graphite-accent/90"
          >
            <Plus className="w-4 h-4" />
            Nová mapa
          </button>
        </div>
        <p className="mt-2 text-gray-600 dark:text-graphite-muted">Myšlenkové mapy projektů a týmů generované z přepisů schůzek</p>
      </div>

      <div className="flex-1 px-4 sm:px-6 lg:px-8 overflow-auto">
        <div className="bg-white dark:bg-graphite-surface shadow-sm dark:shadow-soft-dark rounded-lg border border-gray-200 dark:border-graphite-border overflow-hidden">
          <table className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
            <thead className="bg-gray-50 dark:bg-graphite-surface-2">
              <tr>
                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Název</th>
                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Popis</th>
                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Stav</th>
                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Schůzky</th>
                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Aktualizováno</th>
                <th className="px-4 py-2" />
              </tr>
            </thead>
            <tbody className="bg-white dark:bg-graphite-surface divide-y divide-gray-200 dark:divide-graphite-border">
              {isLoading && (
                <tr>
                  <td colSpan={6} className="px-4 py-6 text-center text-sm text-gray-500 dark:text-graphite-muted">Načítání...</td>
                </tr>
              )}
              {!isLoading && error && (
                <tr>
                  <td colSpan={6} className="px-4 py-6 text-center text-sm text-red-600 dark:text-red-400">Nepodařilo se načíst mapy</td>
                </tr>
              )}
              {!isLoading && !error && items.length === 0 && (
                <tr>
                  <td colSpan={6} className="px-4 py-6 text-center text-sm text-gray-500 dark:text-graphite-muted">Zatím žádné mapy…</td>
                </tr>
              )}
              {!isLoading && !error && items.map((map) => {
                const badge = STATUS_BADGE[map.status] ?? { label: map.status, ...DEFAULT_STATUS_BADGE };
                return (
                  <tr
                    key={map.id}
                    data-testid="mindmap-row"
                    onClick={() => navigate(`/automation/mind-maps/${map.id}`)}
                    className="cursor-pointer hover:bg-gray-50 dark:hover:bg-white/5"
                  >
                    <td className="px-4 py-2 text-sm font-medium text-indigo-600 dark:text-graphite-accent hover:underline">{map.name}</td>
                    <td className="px-4 py-2 text-sm text-gray-700 dark:text-graphite-muted">{map.description}</td>
                    <td className="px-4 py-2">
                      <span className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium ${badge.className}`}>
                        {badge.label}
                      </span>
                    </td>
                    <td className="px-4 py-2 text-sm text-gray-700 dark:text-graphite-muted">{map.meetingCount}</td>
                    <td className="px-4 py-2 text-sm text-gray-700 dark:text-graphite-muted">
                      {new Date(map.updatedAt).toLocaleDateString("cs-CZ")}
                    </td>
                    <td className="px-4 py-2 text-right">
                      <button
                        type="button"
                        title="Smazat"
                        onClick={(e) => {
                          e.stopPropagation();
                          handleDelete(map);
                        }}
                        className="inline-flex items-center p-1.5 rounded-md text-gray-500 hover:bg-red-50 hover:text-red-600 dark:text-graphite-muted dark:hover:bg-red-900/20 dark:hover:text-red-400"
                      >
                        <Trash2 className="w-4 h-4" />
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </div>

      {isCreateOpen && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/40"
          onClick={closeCreateDialog}
        >
          <div
            className="bg-white dark:bg-graphite-surface rounded-xl shadow-lg dark:shadow-soft-dark p-6 w-full max-w-md"
            onClick={(e) => e.stopPropagation()}
          >
            <h2 className="text-lg font-semibold mb-4 dark:text-graphite-text">Nová myšlenková mapa</h2>

            <div className="mb-4">
              <label htmlFor="mindmap-name" className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-1">Název</label>
              <input
                id="mindmap-name"
                type="text"
                data-testid="mindmap-name-input"
                value={newName}
                onChange={(e) => setNewName(e.target.value)}
                className="w-full px-3 py-2 rounded-md text-sm border border-gray-300 focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-indigo-500 dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text dark:placeholder-graphite-faint"
              />
            </div>

            <div className="mb-4">
              <label htmlFor="mindmap-description" className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-1">Popis</label>
              <textarea
                id="mindmap-description"
                value={newDescription}
                onChange={(e) => setNewDescription(e.target.value)}
                rows={3}
                className="w-full px-3 py-2 rounded-md text-sm border border-gray-300 focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-indigo-500 dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text dark:placeholder-graphite-faint"
              />
            </div>

            <div className="flex justify-end gap-2 mt-4">
              <button
                onClick={closeCreateDialog}
                className="px-4 py-2 text-sm rounded-lg border border-gray-300 dark:border-graphite-border hover:bg-gray-50 dark:hover:bg-white/5 dark:text-graphite-muted"
              >
                Zrušit
              </button>
              <button
                data-testid="mindmap-create-submit"
                onClick={handleCreate}
                disabled={!newName.trim() || createMap.isPending}
                className="px-4 py-2 text-sm rounded-lg bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-50"
              >
                {createMap.isPending ? "Vytvářím..." : "Vytvořit"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default MindMapListPage;
