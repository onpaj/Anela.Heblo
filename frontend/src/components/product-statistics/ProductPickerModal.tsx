import React, { useEffect, useState } from "react";
import { Search, X } from "lucide-react";
import { useCatalogAutocomplete } from "../../api/hooks/useCatalogAutocomplete";
import { CatalogItemDto } from "../../api/generated/api-client";
import { SelectedProduct } from "./ProductStatisticsFilter";

/** How many matches the list shows. The endpoint returns no total, so a full page is the only "there may be more" signal. */
export const PRODUCT_PICKER_RESULT_LIMIT = 100;

const SEARCH_MIN_LENGTH = 2;
const SEARCH_DEBOUNCE_MS = 300;

export interface ProductPickerModalProps {
  isOpen: boolean;
  selectedProducts: SelectedProduct[];
  maxProducts: number;
  onConfirm: (products: SelectedProduct[]) => void;
  onClose: () => void;
}

function toSelectedProduct(item: CatalogItemDto): SelectedProduct {
  const productCode = item.productCode as string;
  return {
    productCode,
    productName: item.productName ?? productCode,
  };
}

const labelOf = (product: SelectedProduct) =>
  `${product.productName} (${product.productCode})`;

interface ProductCheckboxProps {
  product: SelectedProduct;
  isChecked: boolean;
  isDisabled: boolean;
  onToggle: (product: SelectedProduct) => void;
}

const ProductCheckbox: React.FC<ProductCheckboxProps> = ({
  product,
  isChecked,
  isDisabled,
  onToggle,
}) => (
  <label
    className={`flex items-center gap-2 px-2 py-1.5 rounded text-sm ${
      isDisabled
        ? "text-gray-400 dark:text-graphite-faint cursor-not-allowed"
        : "text-gray-800 dark:text-graphite-text hover:bg-gray-50 dark:hover:bg-white/5 cursor-pointer"
    }`}
  >
    <input
      type="checkbox"
      checked={isChecked}
      disabled={isDisabled}
      onChange={() => onToggle(product)}
      className="h-4 w-4 rounded border-gray-300 dark:border-graphite-border text-indigo-600 focus:ring-indigo-500"
    />
    <span className="truncate">{labelOf(product)}</span>
  </label>
);

const ProductPickerModal: React.FC<ProductPickerModalProps> = ({
  isOpen,
  selectedProducts,
  maxProducts,
  onConfirm,
  onClose,
}) => {
  const [searchTerm, setSearchTerm] = useState("");
  const [debouncedTerm, setDebouncedTerm] = useState("");
  // A draft: nothing reaches the chart until Potvrdit, so an abandoned pick changes nothing.
  const [draft, setDraft] = useState<SelectedProduct[]>(selectedProducts);

  // Reopening starts from the live selection rather than from whatever was left behind.
  useEffect(() => {
    if (isOpen) {
      setDraft(selectedProducts);
      setSearchTerm("");
      setDebouncedTerm("");
    }
    // selectedProducts is a fresh array on every parent render; the open transition is
    // the only moment the draft should be reseeded.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isOpen]);

  useEffect(() => {
    const timeoutId = setTimeout(
      () => setDebouncedTerm(searchTerm),
      SEARCH_DEBOUNCE_MS,
    );
    return () => clearTimeout(timeoutId);
  }, [searchTerm]);

  useEffect(() => {
    if (!isOpen) {
      return;
    }

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        onClose();
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [isOpen, onClose]);

  const { data, isLoading, isError } = useCatalogAutocomplete(
    debouncedTerm,
    PRODUCT_PICKER_RESULT_LIMIT,
  );

  if (!isOpen) {
    return null;
  }

  const hasSearchTerm = debouncedTerm.length >= SEARCH_MIN_LENGTH;
  const results = (data?.items ?? [])
    .filter((item: CatalogItemDto) => Boolean(item.productCode))
    .map(toSelectedProduct);

  const draftCodes = new Set(draft.map((product) => product.productCode));
  const resultCodes = new Set(results.map((product) => product.productCode));
  // Anything ticked but absent from the current results still needs a way out.
  const selectedElsewhere = draft.filter(
    (product) => !resultCodes.has(product.productCode),
  );

  const isAtCap = draft.length >= maxProducts;

  const toggleProduct = (product: SelectedProduct) => {
    setDraft((previous) => {
      const isSelected = previous.some(
        (item) => item.productCode === product.productCode,
      );

      if (isSelected) {
        return previous.filter(
          (item) => item.productCode !== product.productCode,
        );
      }

      if (previous.length >= maxProducts) {
        return previous;
      }

      return [...previous, product];
    });
  };

  const renderResults = () => {
    if (!hasSearchTerm) {
      return (
        <p className="px-2 py-6 text-center text-sm text-gray-500 dark:text-graphite-muted">
          Zadejte alespoň {SEARCH_MIN_LENGTH} znaky pro hledání.
        </p>
      );
    }

    if (isError) {
      return (
        <p className="px-2 py-6 text-center text-sm text-red-600 dark:text-red-400">
          Nepodařilo se načíst produkty.
        </p>
      );
    }

    if (isLoading) {
      return (
        <p className="px-2 py-6 text-center text-sm text-gray-500 dark:text-graphite-muted">
          Načítání…
        </p>
      );
    }

    if (results.length === 0) {
      return (
        <p className="px-2 py-6 text-center text-sm text-gray-500 dark:text-graphite-muted">
          Žádný produkt neodpovídá hledání.
        </p>
      );
    }

    return results.map((product) => {
      const isChecked = draftCodes.has(product.productCode);
      return (
        <ProductCheckbox
          key={product.productCode}
          product={product}
          isChecked={isChecked}
          isDisabled={!isChecked && isAtCap}
          onToggle={toggleProduct}
        />
      );
    });
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4"
      onClick={onClose}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-label="Vybrat produkty"
        className="flex flex-col w-full max-w-lg max-h-[80vh] bg-white dark:bg-graphite-surface rounded-lg shadow-xl"
        onClick={(event) => event.stopPropagation()}
      >
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-200 dark:border-graphite-border">
          <h2 className="font-semibold text-gray-900 dark:text-graphite-text">
            Vybrat produkty
          </h2>
          <button
            type="button"
            onClick={onClose}
            aria-label="Zavřít"
            className="text-gray-400 hover:text-gray-600 dark:text-graphite-muted"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        <div className="px-4 py-3 border-b border-gray-200 dark:border-graphite-border">
          <label
            htmlFor="product-picker-search"
            className="block text-sm font-medium text-gray-700 dark:text-graphite-text mb-1"
          >
            Hledat produkt
          </label>
          <div className="relative">
            <Search className="absolute left-2 top-1/2 -translate-y-1/2 h-4 w-4 text-gray-400 dark:text-graphite-faint" />
            <input
              id="product-picker-search"
              type="text"
              autoFocus
              value={searchTerm}
              onChange={(event) => setSearchTerm(event.target.value)}
              placeholder="Název nebo kód produktu…"
              className="w-full pl-8 pr-3 py-1.5 text-sm border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text rounded-md"
            />
          </div>
        </div>

        <div className="flex-1 overflow-y-auto px-2 py-2">
          {selectedElsewhere.length > 0 && (
            <div
              role="group"
              aria-label="Vybráno"
              className="mb-2 pb-2 border-b border-gray-200 dark:border-graphite-border"
            >
              {selectedElsewhere.map((product) => (
                <ProductCheckbox
                  key={product.productCode}
                  product={product}
                  isChecked
                  isDisabled={false}
                  onToggle={toggleProduct}
                />
              ))}
            </div>
          )}

          {renderResults()}

          {hasSearchTerm && results.length >= PRODUCT_PICKER_RESULT_LIMIT && (
            <p className="px-2 py-2 text-xs text-gray-500 dark:text-graphite-muted">
              Zobrazeno prvních {PRODUCT_PICKER_RESULT_LIMIT} výsledků, upřesněte
              hledání.
            </p>
          )}
        </div>

        <div className="flex items-center justify-between gap-3 px-4 py-3 border-t border-gray-200 dark:border-graphite-border">
          <div className="text-sm text-gray-500 dark:text-graphite-muted">
            <span>
              Vybráno {draft.length}/{maxProducts}
            </span>
            {isAtCap && (
              <span className="ml-2">Maximálně {maxProducts} produktů.</span>
            )}
          </div>
          <div className="flex gap-2">
            <button
              type="button"
              onClick={onClose}
              className="px-3 py-1.5 text-sm font-medium text-gray-700 dark:text-graphite-text bg-white dark:bg-graphite-surface-2 border border-gray-300 dark:border-graphite-border rounded-md hover:bg-gray-50 dark:hover:bg-graphite-hover"
            >
              Zrušit
            </button>
            <button
              type="button"
              onClick={() => onConfirm(draft)}
              className="px-3 py-1.5 text-sm font-medium text-white bg-indigo-600 rounded-md hover:bg-indigo-700"
            >
              Potvrdit
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};

export default ProductPickerModal;
