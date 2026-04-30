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

const ACTION_TYPE_OPTIONS = [
  { value: 0,  label: "Sociální sítě", backendName: "SocialMedia" },
  { value: 1,  label: "Blog",          backendName: "Blog" },
  { value: 2,  label: "Newsletter",    backendName: "Newsletter" },
  { value: 3,  label: "PR",            backendName: "PR" },
  { value: 4,  label: "Akce",          backendName: "Event" },
  { value: 99, label: "Porada",        backendName: "Meeting" },
];

const FOLDER_TYPE_OPTIONS = [
  { value: 0, label: "Obrázky", backendName: "General" },
  { value: 1, label: "Texty", backendName: "Seasonal" },
  { value: 2, label: "Videa", backendName: "ProductLine" },
  { value: 3, label: "Grafika", backendName: "Campaign" },
  { value: 99, label: "Ostatní", backendName: "Other" },
];

const resolveOptionValue = (
  options: Array<{ value: number; backendName: string }>,
  raw: string | number | undefined,
): number => {
  if (raw == null) return options[0]?.value ?? 0;
  const numeric = Number(raw);
  const byValue = options.find((o) => !isNaN(numeric) && o.value === numeric);
  if (byValue) return byValue.value;
  const byName = options.find((o) => o.backendName === String(raw));
  return byName?.value ?? options[0]?.value ?? 0;
};

interface FolderLinkInput {
  path: string;
  label: string;
  folderType: number;
}

interface FormState {
  title: string;
  detail: string;
  actionType: number;
  dateFrom: string;
  dateTo: string;
  associatedProducts: string[];
  folderLinks: FolderLinkInput[];
}

const EMPTY_FORM: FormState = {
  title: "",
  detail: "",
  actionType: 0,
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
        actionType: resolveOptionValue(ACTION_TYPE_OPTIONS, existingAction.actionType),
        dateFrom: toDateInputValue(existingAction.dateFrom),
        dateTo: toDateInputValue(existingAction.dateTo),
        associatedProducts: existingAction.associatedProducts ?? [],
        folderLinks: (existingAction.folderLinks ?? []).map((fl) => ({
          path: fl.path ?? "",
          label: fl.label ?? "",
          folderType: resolveOptionValue(FOLDER_TYPE_OPTIONS, fl.folderType),
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
      { path: "", label: "", folderType: 0 },
    ]);

  const updateFolderLink = (
    i: number,
    field: keyof FolderLinkInput,
    value: string | number,
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
        .map((fl) => ({
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
      <div className="bg-white rounded-xl shadow-2xl w-full max-w-3xl max-h-[90vh] flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200">
          <h2 className="text-lg font-semibold text-gray-900">
            {isEdit ? "Upravit akci" : "Nová marketingová akce"}
          </h2>
          <button
            onClick={onClose}
            className="p-1 hover:bg-gray-100 rounded-lg transition-colors"
          >
            <X className="h-5 w-5 text-gray-500" />
          </button>
        </div>

        {/* Body */}
        <form
          id={FORM_ID}
          onSubmit={handleSubmit}
          className="flex-1 overflow-y-auto px-6 py-4 space-y-4"
        >
          {error && (
            <div className="text-sm text-red-600 bg-red-50 border border-red-200 rounded-md px-3 py-2">
              {error}
            </div>
          )}

          {/* Title */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Název *
            </label>
            <input
              required
              value={form.title}
              onChange={(e) => set("title", e.target.value)}
              className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
            />
          </div>

          {/* ActionType + dates row */}
          <div className="grid grid-cols-3 gap-3">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Typ *
              </label>
              <select
                value={form.actionType}
                onChange={(e) => set("actionType", Number(e.target.value))}
                className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
              >
                {ACTION_TYPE_OPTIONS.map((o) => (
                  <option key={o.value} value={o.value}>
                    {o.label}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Od *
              </label>
              <input
                required
                type="date"
                value={form.dateFrom}
                onChange={(e) => set("dateFrom", e.target.value)}
                className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Do *
              </label>
              <input
                required
                type="date"
                value={form.dateTo}
                min={form.dateFrom}
                onChange={(e) => set("dateTo", e.target.value)}
                className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
              />
            </div>
          </div>

          {/* Detail */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Popis
            </label>
            <textarea
              value={form.detail}
              onChange={(e) => set("detail", e.target.value)}
              rows={7}
              className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 resize-none"
            />
          </div>

          {/* Products */}
          <div>
            <div className="flex items-center justify-between mb-2">
              <label className="block text-sm font-medium text-gray-700">
                Přiřazené produkty
              </label>
              <div className="flex items-center gap-2">
                <button
                  type="button"
                  onClick={() => setUseTextInput(false)}
                  className={`inline-flex items-center px-3 py-1 text-xs font-medium rounded-md transition-colors ${
                    !useTextInput
                      ? "bg-indigo-100 text-indigo-800 border border-indigo-200"
                      : "bg-gray-100 text-gray-700 border border-gray-200 hover:bg-gray-200"
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
                      ? "bg-indigo-100 text-indigo-800 border border-indigo-200"
                      : "bg-gray-100 text-gray-700 border border-gray-200 hover:bg-gray-200"
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
                className="w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 text-sm"
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
                  className="inline-flex items-center px-3 py-1 rounded-full text-xs font-medium bg-indigo-100 text-indigo-800"
                >
                  {code}
                  <button
                    type="button"
                    onClick={() => removeProduct(code)}
                    className="ml-1 text-indigo-600 hover:text-indigo-800"
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
              <label className="block text-sm font-medium text-gray-700">
                Složky
              </label>
              <button
                type="button"
                onClick={addFolderLink}
                className="text-xs text-indigo-600 hover:text-indigo-800 flex items-center gap-1"
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
                    className="flex-1 border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
                  />
                  <input
                    value={fl.label}
                    onChange={(e) =>
                      updateFolderLink(i, "label", e.target.value)
                    }
                    placeholder="Popis (volitelný)"
                    className="w-32 border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
                  />
                  <select
                    value={fl.folderType}
                    onChange={(e) =>
                      updateFolderLink(i, "folderType", Number(e.target.value))
                    }
                    className="w-28 border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
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
                    className="p-2 text-gray-400 hover:text-red-500 transition-colors"
                  >
                    <Trash2 className="h-4 w-4" />
                  </button>
                </div>
              ))}
            </div>
          </div>
        </form>

        {/* Footer */}
        <div className="flex items-center justify-between px-6 py-4 border-t border-gray-200">
          <div>
            {isEdit && (
              <button
                type="button"
                onClick={handleDelete}
                disabled={isDeleting}
                className="px-4 py-2 text-sm font-medium text-red-600 hover:bg-red-50 rounded-md transition-colors disabled:opacity-50"
              >
                {isDeleting ? "Mazání..." : "Smazat"}
              </button>
            )}
          </div>
          <div className="flex gap-3">
            <button
              type="button"
              onClick={onClose}
              className="px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-100 rounded-md transition-colors"
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
