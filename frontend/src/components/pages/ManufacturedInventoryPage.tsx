import React, { useState, useEffect } from "react";
import {
  Search,
  AlertCircle,
  Loader2,
  ChevronLeft,
  ChevronRight,
  ChevronDown,
  ChevronUp,
  Package,
  Pencil,
  Trash2,
  Plus,
  Check,
  X,
} from "lucide-react";
import {
  useManufacturedProductInventoryQuery,
  useCreateManufacturedProductInventoryItem,
  useUpdateManufacturedProductInventoryItem,
  useDeleteManufacturedProductInventoryItem,
  ManufacturedProductInventoryItem,
  InventoryChangeType,
  CreateManufacturedInventoryItemInput,
} from "../../api/hooks/useManufacturedProductInventory";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";
import { useScreenView } from '../../telemetry/useScreenView';

const PAGE_SIZE = 20;

const changeTypeLabels: Record<InventoryChangeType, string> = {
  [InventoryChangeType.InitialWriteDown]: "Počáteční odpis",
  [InventoryChangeType.ConsumedByTransportBox]: "Spotřeba (box)",
  [InventoryChangeType.RestoredFromTransportBox]: "Vrácení (box)",
  [InventoryChangeType.ManualAdjustment]: "Ruční úprava",
  [InventoryChangeType.ManualRemoval]: "Ruční odebrání",
  [InventoryChangeType.ManualAddition]: "Ruční přidání",
};

const formatDate = (dateStr?: string): string => {
  if (!dateStr) return "—";
  return new Date(dateStr).toLocaleDateString("cs-CZ");
};

const formatDateTime = (dateStr?: string): string => {
  if (!dateStr) return "—";
  return new Date(dateStr).toLocaleString("cs-CZ");
};

interface AddItemModalProps {
  onClose: () => void;
  onSubmit: (input: CreateManufacturedInventoryItemInput) => void;
  isSubmitting: boolean;
}

const AddItemModal: React.FC<AddItemModalProps> = ({ onClose, onSubmit, isSubmitting }) => {
  const [form, setForm] = useState<CreateManufacturedInventoryItemInput>({
    productCode: "",
    productName: "",
    amount: 0,
    lotNumber: "",
    expirationDate: "",
    manufactureOrderId: undefined,
  });

  const handleChange = (field: keyof CreateManufacturedInventoryItemInput, value: string | number | undefined) => {
    setForm((prev) => ({ ...prev, [field]: value }));
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    onSubmit({
      ...form,
      lotNumber: form.lotNumber || undefined,
      expirationDate: form.expirationDate || undefined,
      manufactureOrderId: form.manufactureOrderId || undefined,
    });
  };

  return (
    <div className="fixed inset-0 bg-gray-600 bg-opacity-50 z-50 flex items-center justify-center">
      <div className="bg-white dark:bg-graphite-surface rounded-lg shadow-xl dark:shadow-soft-dark w-full max-w-md p-6">
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-lg font-semibold text-gray-900 dark:text-graphite-text">Přidat položku na sklad</h2>
          <button onClick={onClose} className="text-gray-400 dark:text-graphite-faint hover:text-gray-600">
            <X className="h-5 w-5" />
          </button>
        </div>

        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-1">Kód produktu *</label>
            <input
              type="text"
              required
              value={form.productCode}
              onChange={(e) => handleChange("productCode", e.target.value)}
              className="block w-full border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md sm:text-sm focus:ring-indigo-500 focus:border-indigo-500"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-1">Název produktu *</label>
            <input
              type="text"
              required
              value={form.productName}
              onChange={(e) => handleChange("productName", e.target.value)}
              className="block w-full border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md sm:text-sm focus:ring-indigo-500 focus:border-indigo-500"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-1">Množství *</label>
            <input
              type="number"
              required
              min={0}
              value={form.amount}
              onChange={(e) => handleChange("amount", Number(e.target.value))}
              className="block w-full border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md sm:text-sm focus:ring-indigo-500 focus:border-indigo-500"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-1">Č. šarže</label>
            <input
              type="text"
              value={form.lotNumber ?? ""}
              onChange={(e) => handleChange("lotNumber", e.target.value)}
              className="block w-full border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md sm:text-sm focus:ring-indigo-500 focus:border-indigo-500"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-1">Expirační datum</label>
            <input
              type="date"
              value={form.expirationDate ?? ""}
              onChange={(e) => handleChange("expirationDate", e.target.value)}
              className="block w-full border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md sm:text-sm focus:ring-indigo-500 focus:border-indigo-500"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-1">ID výrobní zakázky</label>
            <input
              type="number"
              value={form.manufactureOrderId ?? ""}
              onChange={(e) => handleChange("manufactureOrderId", e.target.value ? Number(e.target.value) : undefined)}
              className="block w-full border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md sm:text-sm focus:ring-indigo-500 focus:border-indigo-500"
            />
          </div>

          <div className="flex justify-end gap-3 pt-2">
            <button
              type="button"
              onClick={onClose}
              className="px-4 py-2 text-sm font-medium text-gray-700 dark:text-graphite-muted bg-white dark:bg-graphite-surface border border-gray-300 dark:border-graphite-border rounded-md hover:bg-gray-50 dark:hover:bg-white/5"
            >
              Zrušit
            </button>
            <button
              type="submit"
              disabled={isSubmitting}
              className="px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-md hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              {isSubmitting ? "Ukládám..." : "Přidat"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};

interface InlineEditCellProps {
  item: ManufacturedProductInventoryItem;
  onSave: (id: number, newAmount: number) => void;
}

const InlineEditCell: React.FC<InlineEditCellProps> = ({ item, onSave }) => {
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState(item.amount);

  useEffect(() => {
    if (!editing) {
      setValue(item.amount);
    }
  }, [item.amount, editing]);

  const handleSave = () => {
    onSave(item.id, value);
    setEditing(false);
  };

  const handleCancel = () => {
    setValue(item.amount);
    setEditing(false);
  };

  if (editing) {
    return (
      <div className="flex items-center gap-1">
        <input
          type="number"
          min={0}
          value={value}
          onChange={(e) => setValue(Number(e.target.value))}
          className="w-20 border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text rounded sm:text-sm focus:ring-indigo-500 focus:border-indigo-500"
          autoFocus
        />
        <button onClick={handleSave} className="text-green-600 dark:text-emerald-400 hover:text-green-800" title="Uložit">
          <Check className="h-4 w-4" />
        </button>
        <button onClick={handleCancel} className="text-gray-400 dark:text-graphite-faint hover:text-gray-600" title="Zrušit">
          <X className="h-4 w-4" />
        </button>
      </div>
    );
  }

  return (
    <div className="flex items-center gap-2">
      <span className="inline-flex items-center px-3 py-1 rounded-full text-sm font-semibold bg-green-100 dark:bg-emerald-900/30 text-green-800 dark:text-emerald-300">
        {item.amount}
      </span>
      <button onClick={() => setEditing(true)} className="text-gray-400 dark:text-graphite-faint hover:text-indigo-600 dark:hover:text-graphite-accent" title="Upravit množství">
        <Pencil className="h-3.5 w-3.5" />
      </button>
    </div>
  );
};

interface LogPanelProps {
  item: ManufacturedProductInventoryItem;
}

const LogPanel: React.FC<LogPanelProps> = ({ item }) => {
  if (item.log.length === 0) {
    return <p className="text-sm text-gray-500 dark:text-graphite-muted px-4 py-2">Žádné záznamy.</p>;
  }

  return (
    <table className="min-w-full text-xs text-gray-700 dark:text-graphite-muted">
      <thead>
        <tr className="bg-gray-100 dark:bg-graphite-surface-2">
          <th className="px-3 py-1 text-left font-medium">Datum</th>
          <th className="px-3 py-1 text-left font-medium">Typ</th>
          <th className="px-3 py-1 text-right font-medium">Změna</th>
          <th className="px-3 py-1 text-right font-medium">Stav po</th>
          <th className="px-3 py-1 text-left font-medium">Poznámka</th>
          <th className="px-3 py-1 text-left font-medium">Uživatel</th>
        </tr>
      </thead>
      <tbody className="divide-y divide-gray-100 dark:divide-graphite-border">
        {item.log.map((entry) => (
          <tr key={entry.id}>
            <td className="px-3 py-1 whitespace-nowrap">{formatDateTime(entry.timestamp)}</td>
            <td className="px-3 py-1 whitespace-nowrap">{changeTypeLabels[entry.changeType] ?? entry.changeType}</td>
            <td className={`px-3 py-1 text-right whitespace-nowrap font-medium ${entry.amountDelta >= 0 ? "text-green-700 dark:text-emerald-400" : "text-red-700 dark:text-red-400"}`}>
              {entry.amountDelta >= 0 ? "+" : ""}{entry.amountDelta}
            </td>
            <td className="px-3 py-1 text-right whitespace-nowrap">{entry.amountAfter}</td>
            <td className="px-3 py-1">{entry.note ?? "—"}</td>
            <td className="px-3 py-1 whitespace-nowrap">{entry.user}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
};

const ManufacturedInventoryPage: React.FC = () => {
  useScreenView('Manufacturing', 'ManufacturedProductInventory');
  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");
  const [onlyWithStock, setOnlyWithStock] = useState(true);
  const [page, setPage] = useState(1);
  const [expandedRows, setExpandedRows] = useState<Set<number>>(new Set());
  const [isAddModalOpen, setIsAddModalOpen] = useState(false);
  const [confirmDeleteId, setConfirmDeleteId] = useState<number | null>(null);

  const filters = { search, onlyWithStock, page, pageSize: PAGE_SIZE };

  const { data, isLoading, error } = useManufacturedProductInventoryQuery(filters);
  const createMutation = useCreateManufacturedProductInventoryItem();
  const updateMutation = useUpdateManufacturedProductInventoryItem();
  const deleteMutation = useDeleteManufacturedProductInventoryItem();

  const items = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;
  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));

  const applySearch = () => {
    setSearch(searchInput);
    setPage(1);
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "Enter") applySearch();
  };

  const toggleRow = (id: number) => {
    setExpandedRows((prev) =>
      prev.has(id)
        ? new Set(Array.from(prev).filter((existingId) => existingId !== id))
        : new Set([...Array.from(prev), id]),
    );
  };

  const handleUpdate = (id: number, newAmount: number) => {
    updateMutation.mutate({ id, newAmount });
  };

  const handleDeleteConfirm = (id: number) => {
    deleteMutation.mutate({ id }, { onSuccess: () => setConfirmDeleteId(null) });
  };

  const handleCreate = (input: CreateManufacturedInventoryItemInput) => {
    createMutation.mutate(input, {
      onSuccess: () => setIsAddModalOpen(false),
    });
  };

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2">
          <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
          <span className="text-gray-500 dark:text-graphite-muted">Načítání skladu výroby...</span>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2 text-red-600 dark:text-red-400">
          <AlertCircle className="h-5 w-5" />
          <span>Chyba při načítání: {(error as Error).message}</span>
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col w-full" style={{ height: PAGE_CONTAINER_HEIGHT }}>
      {/* Header */}
      <div className="flex-shrink-0 mb-3">
        <div className="flex items-center justify-between">
          <div className="flex items-center space-x-2">
            <Package className="h-6 w-6 text-indigo-600 dark:text-graphite-accent" />
            <h1 className="text-lg font-semibold text-gray-900 dark:text-graphite-text">Sklad výroby</h1>
          </div>
          <button
            onClick={() => setIsAddModalOpen(true)}
            className="flex items-center gap-1 bg-indigo-600 hover:bg-indigo-700 text-white font-medium py-2 px-4 rounded-md transition-colors text-sm"
          >
            <Plus className="h-4 w-4" />
            Přidat položku
          </button>
        </div>
      </div>

      {/* Filters */}
      <div className="flex-shrink-0 bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg p-4 mb-4">
        <div className="flex items-center gap-3 flex-wrap">
          <div className="relative flex-1 max-w-xs">
            <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
              <Search className="h-4 w-4 text-gray-400 dark:text-graphite-faint" />
            </div>
            <input
              type="text"
              value={searchInput}
              onChange={(e) => setSearchInput(e.target.value)}
              onKeyDown={handleKeyDown}
              placeholder="Hledat..."
              className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 sm:text-sm border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md"
            />
          </div>

          <label className="flex items-center gap-2 text-sm text-gray-700 dark:text-graphite-muted cursor-pointer">
            <input
              type="checkbox"
              checked={onlyWithStock}
              onChange={(e) => { setOnlyWithStock(e.target.checked); setPage(1); }}
              className="rounded border-gray-300 dark:border-graphite-border text-indigo-600 focus:ring-indigo-500"
            />
            Pouze na skladě
          </label>

          <button
            onClick={applySearch}
            className="bg-indigo-600 hover:bg-indigo-700 text-white font-medium py-2 px-4 rounded-md transition-colors text-sm"
          >
            Filtrovat
          </button>
        </div>
      </div>

      {/* Table */}
      <div className="flex-1 bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg overflow-hidden flex flex-col min-h-0">
        <div className="flex-1 overflow-auto">
          <table className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
            <thead className="bg-gray-50 dark:bg-graphite-surface-2 sticky top-0 z-10">
              <tr>
                <th className="px-4 py-4 text-left text-sm font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Kód produktu</th>
                <th className="px-4 py-4 text-left text-sm font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Název produktu</th>
                <th className="px-4 py-4 text-left text-sm font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Č. šarže</th>
                <th className="px-4 py-4 text-left text-sm font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Expirace</th>
                <th className="px-4 py-4 text-left text-sm font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Množství</th>
                <th className="px-4 py-4 text-left text-sm font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Naposledy změnil</th>
                <th className="px-4 py-4 text-left text-sm font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Akce</th>
              </tr>
            </thead>
            <tbody className="bg-white dark:bg-graphite-surface divide-y divide-gray-200 dark:divide-graphite-border">
              {items.map((item) => {
                const isExpanded = expandedRows.has(item.id);
                return (
                  <React.Fragment key={item.id}>
                    <tr className="hover:bg-gray-50 dark:hover:bg-white/5 transition-colors">
                      <td className="px-4 py-3 text-sm font-medium text-gray-900 dark:text-graphite-text whitespace-nowrap">{item.productCode}</td>
                      <td className="px-4 py-3 text-sm text-gray-900 dark:text-graphite-text">{item.productName}</td>
                      <td className="px-4 py-3 text-sm text-gray-600 dark:text-graphite-muted whitespace-nowrap">{item.lotNumber ?? "—"}</td>
                      <td className="px-4 py-3 text-sm text-gray-600 dark:text-graphite-muted whitespace-nowrap">{formatDate(item.expirationDate)}</td>
                      <td className="px-4 py-3 whitespace-nowrap">
                        <InlineEditCell item={item} onSave={handleUpdate} />
                      </td>
                      <td className="px-4 py-3 text-sm text-gray-500 dark:text-graphite-muted whitespace-nowrap">
                        {item.lastModifiedBy
                          ? `${item.lastModifiedBy} (${formatDate(item.lastModifiedAt)})`
                          : `${item.createdBy} (${formatDate(item.createdAt)})`}
                      </td>
                      <td className="px-4 py-3 whitespace-nowrap">
                        <div className="flex items-center gap-2">
                          <button
                            onClick={() => toggleRow(item.id)}
                            className="flex items-center gap-1 text-xs text-indigo-600 dark:text-graphite-accent hover:text-indigo-800 font-medium"
                            title="Historie změn"
                          >
                            Historie
                            {isExpanded ? <ChevronUp className="h-3 w-3" /> : <ChevronDown className="h-3 w-3" />}
                          </button>
                          {confirmDeleteId === item.id ? (
                            <div className="flex items-center gap-1">
                              <button
                                onClick={() => setConfirmDeleteId(null)}
                                className="text-xs text-gray-500 dark:text-graphite-muted hover:text-gray-700 font-medium"
                              >
                                Zrušit
                              </button>
                              <button
                                onClick={() => handleDeleteConfirm(item.id)}
                                disabled={deleteMutation.isPending}
                                className="text-xs text-red-600 dark:text-red-400 hover:text-red-800 font-medium disabled:opacity-50"
                              >
                                Potvrdit smazání
                              </button>
                            </div>
                          ) : (
                            <button
                              onClick={() => setConfirmDeleteId(item.id)}
                              className="text-red-400 dark:text-red-400 hover:text-red-600"
                              title="Smazat"
                            >
                              <Trash2 className="h-4 w-4" />
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                    {isExpanded && (
                      <tr>
                        <td colSpan={7} className="bg-gray-50 dark:bg-graphite-surface-2 border-b border-gray-200 dark:border-graphite-border">
                          <LogPanel item={item} />
                        </td>
                      </tr>
                    )}
                  </React.Fragment>
                );
              })}
            </tbody>
          </table>

          {items.length === 0 && (
            <div className="text-center py-8">
              <Package className="h-12 w-12 text-gray-400 dark:text-graphite-faint mx-auto mb-4" />
              <p className="text-gray-500 dark:text-graphite-muted">Žádné položky nebyly nalezeny.</p>
            </div>
          )}
        </div>
      </div>

      {/* Pagination */}
      {totalCount > 0 && (
        <div className="flex-shrink-0 bg-white dark:bg-graphite-surface px-3 py-2 flex items-center justify-between border-t border-gray-200 dark:border-graphite-border text-xs">
          <p className="text-xs text-gray-600 dark:text-graphite-muted">
            {Math.min((page - 1) * PAGE_SIZE + 1, totalCount)}–{Math.min(page * PAGE_SIZE, totalCount)} z {totalCount}
          </p>
          <nav className="relative z-0 inline-flex rounded shadow-sm dark:shadow-soft-dark -space-x-px" aria-label="Pagination">
            <button
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              disabled={page <= 1}
              className="relative inline-flex items-center px-1 py-1 rounded-l border border-gray-300 dark:border-graphite-border bg-white dark:bg-graphite-surface text-xs font-medium text-gray-500 dark:text-graphite-muted hover:bg-gray-50 dark:hover:bg-white/5 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <ChevronLeft className="h-3 w-3" />
            </button>
            {Array.from({ length: Math.min(totalPages, 5) }, (_, i) => {
              let pageNum: number;
              if (totalPages <= 5) pageNum = i + 1;
              else if (page <= 3) pageNum = i + 1;
              else if (page >= totalPages - 2) pageNum = totalPages - 4 + i;
              else pageNum = page - 2 + i;
              return (
                <button
                  key={pageNum}
                  onClick={() => setPage(pageNum)}
                  className={`relative inline-flex items-center px-2 py-1 border text-xs font-medium ${
                    pageNum === page
                      ? "z-10 bg-indigo-50 dark:bg-graphite-accent/10 border-indigo-500 dark:border-graphite-accent text-indigo-600 dark:text-graphite-accent"
                      : "bg-white dark:bg-graphite-surface border-gray-300 dark:border-graphite-border text-gray-500 dark:text-graphite-muted hover:bg-gray-50 dark:hover:bg-white/5"
                  }`}
                >
                  {pageNum}
                </button>
              );
            })}
            <button
              onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
              disabled={page >= totalPages}
              className="relative inline-flex items-center px-1 py-1 rounded-r border border-gray-300 dark:border-graphite-border bg-white dark:bg-graphite-surface text-xs font-medium text-gray-500 dark:text-graphite-muted hover:bg-gray-50 dark:hover:bg-white/5 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <ChevronRight className="h-3 w-3" />
            </button>
          </nav>
        </div>
      )}

      {isAddModalOpen && (
        <AddItemModal
          onClose={() => setIsAddModalOpen(false)}
          onSubmit={handleCreate}
          isSubmitting={createMutation.isPending}
        />
      )}
    </div>
  );
};

export default ManufacturedInventoryPage;
