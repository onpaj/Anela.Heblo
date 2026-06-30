import React, { useState, useEffect } from "react";
import { X, Plus, Trash2, Search, Type } from "lucide-react";
import type { MarketingActionDto } from "../list/MarketingActionGrid";
import {
  useCreateMarketingAction,
  useUpdateMarketingAction,
  useDeleteMarketingAction,
} from "../../../api/hooks/useMarketingCalendar";
import { CatalogAutocomplete } from "../../common/CatalogAutocomplete";
import {
  catalogItemToProductCode,
  PRODUCT_TYPE_FILTERS,
} from "../../common/CatalogAutocompleteAdapters";
import {
  MarketingActionType,
  MarketingFolderType,
  MarketingFolderLinkRequest,
} from "../../../api/generated/api-client";

const ACTION_TYPE_OPTIONS: ReadonlyArray<{ value: MarketingActionType; label: string }> = [
  { value: MarketingActionType.SocialMedia, label: "Sociální sítě" },
  { value: MarketingActionType.Blog,        label: "Blog" },
  { value: MarketingActionType.Newsletter,  label: "Newsletter" },
  { value: MarketingActionType.PR,          label: "PR" },
  { value: MarketingActionType.Event,       label: "Akce" },
  { value: MarketingActionType.Meeting,     label: "Porada" },
];

const FOLDER_TYPE_OPTIONS: ReadonlyArray<{ value: MarketingFolderType; label: string }> = [
  { value: MarketingFolderType.General,     label: "Obrázky" },
  { value: MarketingFolderType.Seasonal,    label: "Texty" },
  { value: MarketingFolderType.ProductLine, label: "Videa" },
  { value: MarketingFolderType.Campaign,    label: "Grafika" },
  { value: MarketingFolderType.Other,       label: "Ostatní" },
];

const resolveActionType = (raw: string | undefined): MarketingActionType => {
  if (raw && (Object.values(MarketingActionType) as string[]).includes(raw)) {
    return raw as MarketingActionType;
  }
  return MarketingActionType.SocialMedia;
};

const resolveFolderType = (raw: string | undefined): MarketingFolderType => {
  if (raw && (Object.values(MarketingFolderType) as string[]).includes(raw)) {
    return raw as MarketingFolderType;
  }
  return MarketingFolderType.General;
};

interface FolderLinkInput {
  path: string;
  label: string;
  folderType: MarketingFolderType;
}

interface FormState {
  title: string;
  detail: string;
  actionType: MarketingActionType;
  dateFrom: string;
  dateTo: string;
  associatedProducts: string[];
  folderLinks: FolderLinkInput[];
}

const EMPTY_FORM: FormState = {
  title: "",
  detail: "",
  actionType: MarketingActionType.SocialMedia,
  dateFrom: "",
  dateTo: "",
  associatedProducts: [],
  folderLinks: [],
};

interface MarketingActionModalProps {
  isOpen: boolean;
  onClose: () => void;
  existingAction?: MarketingActionDto | null;
  prefillDates?: { dateFrom: string; dateTo: string } | null;
}

const toDateInputValue = (d: string | Date | undefined): string => {
  if (!d) return "";
  if (d instanceof Date) {
    const year = d.getFullYear();
    const month = String(d.getMonth() + 1).padStart(2, "0");
    const day = String(d.getDate()).padStart(2, "0");
    return `${year}-${month}-${day}`;
  }
  return String(d).substring(0, 10);
};

const FORM_ID = "marketing-action-form";

const MarketingActionModal: React.FC<MarketingActionModalProps> = ({
  isOpen,
  onClose,
  existingAction,
  prefillDates,
}) => {
  const [form, setForm] = useState<FormState>(EMPTY_FORM);
  const [error, setError] = useState<string | null>(null);
  const [currentProduct, setCurrentProduct] = useState<string | null>(null);
  const [useTextInput, setUseTextInput] = useState(false);
  const [currentTextProduct, setCurrentTextProduct] = useState("");

  const createMutation = useCreateMarketingAction();
  const updateMutation = useUpdateMarketingAction();
  const deleteMutation = useDeleteMarketingAction();

  const isEdit = !!existingAction;

  useEffect(() => {
    if (existingAction) {
      setForm({
        title: existingAction.title ?? "",
        detail: existingAction.detail ?? "",
        actionType: resolveActionType(existingAction.actionType),
        dateFrom: toDateInputValue(existingAction.dateFrom),
        dateTo: toDateInputValue(existingAction.dateTo),
        associatedProducts: existingAction.associatedProducts ?? [],
        folderLinks: (existingAction.folderLinks ?? []).map((fl) => ({
          path: fl.path ?? "",
          label: fl.label ?? "",
          folderType: resolveFolderType(fl.folderType),
        })),
      });
    } else if (prefillDates) {
      setForm({ ...EMPTY_FORM, dateFrom: prefillDates.dateFrom, dateTo: prefillDates.dateTo });
    } else {
      setForm(EMPTY_FORM);
    }
    setError(null);
  }, [existingAction, prefillDates, isOpen]);

  if (!isOpen) return null;

  const set = <K extends keyof FormState>(key: K, value: FormState[K]) =>
    setForm((prev) => ({ ...prev, [key]: value }));

  const handleProductSelect = (code: string | null) => {
    if (code && !form.associatedProducts.includes(code)) {
      set("associatedProducts", [...form.associatedProducts, code]);
    }
    setCurrentProduct(null);
  };

  const handleTextProductSubmit = () => {
    const code = currentTextProduct.trim().toUpperCase();
    if (code && !form.associatedProducts.includes(code)) {
      set("associatedProducts", [...form.associatedProducts, code]);
    }
    setCurrentTextProduct("");
  };

  const removeProduct = (code: string) =>
    set(
      "associatedProducts",
      form.associatedProducts.filter((p) => p !== code),
    );

  const addFolderLink = () =>
    set("folderLinks", [
      ...form.folderLinks,
      { path: "", label: "", folderType: MarketingFolderType.General },
    ]);

  const updateFolderLink = (
    i: number,
    field: keyof FolderLinkInput,
    value: string | MarketingFolderType,
  ) =>
    set(
      "folderLinks",
      form.folderLinks.map((fl, idx) =>
        idx === i ? { ...fl, [field]: value } : fl,
      ),
    );

  const removeFolderLink = (i: number) =>
    set(
      "folderLinks",
      form.folderLinks.filter((_, idx) => idx !== i),
    );

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);

    const payload = {
      title: form.title.trim(),
      description: form.detail.trim() || undefined,
      actionType: form.actionType,
      startDate: new Date(form.dateFrom),
      endDate: form.dateTo ? new Date(form.dateTo) : undefined,
      associatedProducts: form.associatedProducts,
      folderLinks: form.folderLinks
        .filter((fl) => fl.path.trim())
        .map((fl) => new MarketingFolderLinkRequest({
          folderKey: fl.path.trim(),
          folderType: fl.folderType,
        })),
    };

    try {
      if (isEdit && existingAction?.id != null) {
        await updateMutation.mutateAsync({ id: existingAction.id, request: payload });
      } else {
        await createMutation.mutateAsync(payload);
      }
      onClose();
    } catch {
      setError("Nepodařilo se uložit akci. Zkuste to znovu.");
    }
  };

  const handleDelete = async () => {
    if (!existingAction?.id) return;
    if (!window.confirm(`Opravdu smazat akci "${existingAction.title}"?`))
      return;
    try {
      await deleteMutation.mutateAsync(existingAction.id);
      onClose();
    } catch {
      setError("Nepodařilo se smazat akci.");
    }
  };

  const isSaving = createMutation.isPending || updateMutation.isPending;
  const isDeleting = deleteMutation.isPending;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4">
      <div className="bg-white dark:bg-graphite-surface rounded-xl shadow-2xl dark:shadow-soft-dark w-full max-w-3xl max-h-[90vh] flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200 dark:border-graphite-border">
          <h2 className="text-lg font-semibold text-gray-900 dark:text-graphite-text">
            {isEdit ? "Upravit akci" : "Nová marketingová akce"}
          </h2>
          <button
            onClick={onClose}
            className="p-1 hover:bg-gray-100 dark:hover:bg-white/5 rounded-lg transition-colors"
          >
            <X className="h-5 w-5 text-gray-500 dark:text-graphite-muted" />
          </button>
        </div>

        {/* Body */}
        <form
          id={FORM_ID}
          onSubmit={handleSubmit}
          className="flex-1 overflow-y-auto px-6 py-4 space-y-4"
        >
          {error && (
            <div className="text-sm text-red-600 dark:text-red-400 bg-red-50 dark:bg-red-900/30 border border-red-200 dark:border-red-700 rounded-md px-3 py-2">
              {error}
            </div>
          )}

          {/* Title */}
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-1">
              Název *
            </label>
            <input
              required
              value={form.title}
              onChange={(e) => set("title", e.target.value)}
              className="w-full border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
            />
          </div>

          {/* ActionType + dates row */}
          <div className="grid grid-cols-3 gap-3">
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-1">
                Typ *
              </label>
              <select
                value={form.actionType}
                onChange={(e) => set("actionType", e.target.value as MarketingActionType)}
                className="w-full border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
              >
                {ACTION_TYPE_OPTIONS.map((o) => (
                  <option key={o.value} value={o.value}>
                    {o.label}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-1">
                Od *
              </label>
              <input
                required
                type="date"
                value={form.dateFrom}
                onChange={(e) => set("dateFrom", e.target.value)}
                className="w-full border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-1">
                Do *
              </label>
              <input
                required
                type="date"
                value={form.dateTo}
                min={form.dateFrom}
                onChange={(e) => set("dateTo", e.target.value)}
                className="w-full border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
              />
            </div>
          </div>

          {/* Detail */}
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-1">
              Popis
            </label>
            <textarea
              value={form.detail}
              onChange={(e) => set("detail", e.target.value)}
              rows={7}
              className="w-full border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 resize-none"
            />
          </div>

          {/* Products */}
          <div>
            <div className="flex items-center justify-between mb-2">
              <label className="block text-sm font-medium text-gray-700 dark:text-graphite-muted">
                Přiřazené produkty
              </label>
              <div className="flex items-center gap-2">
                <button
                  type="button"
                  onClick={() => setUseTextInput(false)}
                  className={`inline-flex items-center px-3 py-1 text-xs font-medium rounded-md transition-colors ${
                    !useTextInput
                      ? "bg-indigo-100 dark:bg-graphite-accent/10 text-indigo-800 dark:text-graphite-accent border border-indigo-200 dark:border-graphite-accent"
                      : "bg-gray-100 dark:bg-graphite-surface-2 text-gray-700 dark:text-graphite-muted border border-gray-200 dark:border-graphite-border hover:bg-gray-200 dark:hover:bg-white/5"
                  }`}
                >
                  <Search className="h-3 w-3 mr-1" />
                  Autocomplete
                </button>
                <button
                  type="button"
                  onClick={() => setUseTextInput(true)}
                  className={`inline-flex items-center px-3 py-1 text-xs font-medium rounded-md transition-colors ${
                    useTextInput
                      ? "bg-indigo-100 dark:bg-graphite-accent/10 text-indigo-800 dark:text-graphite-accent border border-indigo-200 dark:border-graphite-accent"
                      : "bg-gray-100 dark:bg-graphite-surface-2 text-gray-700 dark:text-graphite-muted border border-gray-200 dark:border-graphite-border hover:bg-gray-200 dark:hover:bg-white/5"
                  }`}
                >
                  <Type className="h-3 w-3 mr-1" />
                  Text
                </button>
              </div>
            </div>

            {useTextInput ? (
              <input
                type="text"
                value={currentTextProduct}
                onChange={(e) => setCurrentTextProduct(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === "Enter") {
                    e.preventDefault();
                    handleTextProductSubmit();
                  }
                }}
                onBlur={handleTextProductSubmit}
                placeholder="Zadejte produktový kód nebo prefix..."
                className="w-full px-3 py-2 border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md shadow-sm dark:shadow-soft-dark focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 text-sm"
              />
            ) : (
              <CatalogAutocomplete<string>
                value={currentProduct}
                onSelect={handleProductSelect}
                placeholder="Začněte psát název nebo kód produktu..."
                productTypes={PRODUCT_TYPE_FILTERS.ALL}
                itemAdapter={catalogItemToProductCode}
                displayValue={(value) => value}
                size="md"
              />
            )}

            <div className="flex flex-wrap gap-2 mt-3">
              {form.associatedProducts.map((code) => (
                <span
                  key={code}
                  className="inline-flex items-center px-3 py-1 rounded-full text-xs font-medium bg-indigo-100 dark:bg-graphite-accent/10 text-indigo-800 dark:text-graphite-accent"
                >
                  {code}
                  <button
                    type="button"
                    onClick={() => removeProduct(code)}
                    className="ml-1 text-indigo-600 dark:text-graphite-accent hover:text-indigo-800"
                  >
                    <X className="h-3 w-3" />
                  </button>
                </span>
              ))}
            </div>
          </div>

          {/* Folder links */}
          <div>
            <div className="flex items-center justify-between mb-1">
              <label className="block text-sm font-medium text-gray-700 dark:text-graphite-muted">
                Složky
              </label>
              <button
                type="button"
                onClick={addFolderLink}
                className="text-xs text-indigo-600 dark:text-graphite-accent hover:text-indigo-800 flex items-center gap-1"
              >
                <Plus className="h-3 w-3" /> Přidat složku
              </button>
            </div>
            <div className="space-y-2">
              {form.folderLinks.map((fl, i) => (
                <div key={i} className="flex gap-2 items-start">
                  <input
                    value={fl.path}
                    onChange={(e) => updateFolderLink(i, "path", e.target.value)}
                    placeholder="Cesta ke složce"
                    className="flex-1 border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
                  />
                  <input
                    value={fl.label}
                    onChange={(e) =>
                      updateFolderLink(i, "label", e.target.value)
                    }
                    placeholder="Popis (volitelný)"
                    className="w-32 border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
                  />
                  <select
                    value={fl.folderType}
                    onChange={(e) =>
                      updateFolderLink(i, "folderType", e.target.value as MarketingFolderType)
                    }
                    className="w-28 border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
                  >
                    {FOLDER_TYPE_OPTIONS.map((o) => (
                      <option key={o.value} value={o.value}>
                        {o.label}
                      </option>
                    ))}
                  </select>
                  <button
                    type="button"
                    onClick={() => removeFolderLink(i)}
                    className="p-2 text-gray-400 dark:text-graphite-faint hover:text-red-500 transition-colors"
                  >
                    <Trash2 className="h-4 w-4" />
                  </button>
                </div>
              ))}
            </div>
          </div>
        </form>

        {/* Footer */}
        <div className="flex items-center justify-between px-6 py-4 border-t border-gray-200 dark:border-graphite-border">
          <div>
            {isEdit && (
              <button
                type="button"
                onClick={handleDelete}
                disabled={isDeleting}
                className="px-4 py-2 text-sm font-medium text-red-600 dark:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/30 rounded-md transition-colors disabled:opacity-50"
              >
                {isDeleting ? "Mazání..." : "Smazat"}
              </button>
            )}
          </div>
          <div className="flex gap-3">
            <button
              type="button"
              onClick={onClose}
              className="px-4 py-2 text-sm font-medium text-gray-700 dark:text-graphite-muted hover:bg-gray-100 dark:hover:bg-white/5 rounded-md transition-colors"
            >
              Zrušit
            </button>
            <button
              type="submit"
              form={FORM_ID}
              disabled={isSaving}
              className="px-4 py-2 text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 rounded-md transition-colors disabled:opacity-50"
            >
              {isSaving ? "Ukládání..." : isEdit ? "Uložit" : "Vytvořit"}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};

export default MarketingActionModal;
