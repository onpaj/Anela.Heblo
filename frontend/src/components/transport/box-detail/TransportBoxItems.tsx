import React, { useState, useEffect } from "react";
import { Package, Tag, Trash2, RotateCcw, AlertCircle, Loader, FlaskConical } from "lucide-react";
import { TransportBoxItemsProps } from "./TransportBoxTypes";
import { CatalogAutocomplete } from "../../common/CatalogAutocomplete";
import { ProductType } from "../../../api/generated/api-client";
import { useManufacturedProductInventoryQuery } from "../../../api/hooks/useManufacturedProductInventory";
import type { ManufacturedProductInventoryItem } from "../../../api/hooks/useManufacturedProductInventory";

type ActiveAddTab = "catalog" | "manufactured";

interface ManufacturedRowProps {
  item: ManufacturedProductInventoryItem;
  onAdd: (item: ManufacturedProductInventoryItem, amount: number) => void;
  onOverdraft: (item: ManufacturedProductInventoryItem, amount: number) => void;
  defaultAmount?: number;
}

const ManufacturedRow: React.FC<ManufacturedRowProps> = ({ item, onAdd, onOverdraft, defaultAmount }) => {
  const initialAmount = defaultAmount ? String(defaultAmount) : "";
  const [rowAmount, setRowAmount] = useState(initialAmount);
  const [isDirty, setIsDirty] = useState(false);
  const [rowError, setRowError] = useState<string | null>(null);

  const handleSubmit = () => {
    const parsed = parseFloat(rowAmount);
    if (!rowAmount || isNaN(parsed) || parsed <= 0) {
      setRowError("Zadejte kladné číslo");
      return;
    }
    if (parsed > item.amount) {
      onOverdraft(item, parsed);
      return;
    }
    setRowError(null);
    onAdd(item, parsed);
  };

  return (
    <div
      onClick={handleSubmit}
      className="flex items-center gap-3 px-3 py-2 border-b border-gray-100 last:border-b-0 hover:bg-green-50 cursor-pointer transition-colors"
    >
      <div className="flex-1 min-w-0">
        <div className="text-sm font-medium text-gray-900 truncate">{item.productName}</div>
        <div className="text-xs text-gray-500 flex flex-wrap gap-x-3 mt-0.5">
          <span className="font-mono">{item.productCode}</span>
          {item.lotNumber && <span>Lot: {item.lotNumber}</span>}
          {item.expirationDate && <span>Exp: {item.expirationDate}</span>}
          <span className="font-semibold text-green-700">Sklad: {item.amount}</span>
        </div>
        {rowError && (
          <div className="flex items-center gap-1 mt-1 text-xs text-red-600">
            <AlertCircle className="h-3 w-3 flex-shrink-0" />
            {rowError}
          </div>
        )}
      </div>
      <div className="flex items-center gap-2 flex-shrink-0" onClick={(e) => e.stopPropagation()}>
        <input
          type="number"
          value={rowAmount}
          onChange={(e) => {
            setRowAmount(e.target.value);
            setIsDirty(true);
            setRowError(null);
          }}
          step="0.01"
          min="0.01"
          placeholder="0"
          className="w-20 px-2 py-1 text-sm border border-gray-300 rounded focus:outline-none focus:ring-2 focus:ring-green-500 focus:border-transparent"
        />
        {isDirty && (
          <button
            type="button"
            onClick={handleSubmit}
            disabled={!rowAmount || parseFloat(rowAmount) <= 0}
            className="px-2 py-1 text-sm font-medium text-white bg-green-600 rounded hover:bg-green-700 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            Přidat
          </button>
        )}
      </div>
    </div>
  );
};

const TransportBoxItems: React.FC<TransportBoxItemsProps> = ({
  transportBox,
  isFormEditable,
  formatDate,
  handleRemoveItem,
  quantityInput,
  setQuantityInput,
  selectedProduct,
  setSelectedProduct,
  handleAddItem,
  handleAddManufacturedItem,
  lastAddedItem,
  handleQuickAdd,
  lastManufacturedItems,
}) => {
  const [activeAddTab, setActiveAddTab] = useState<ActiveAddTab>("manufactured");
  const [manufacturedSearch, setManufacturedSearch] = useState("");
  const [failedImages, setFailedImages] = useState<Set<number>>(new Set());

  useEffect(() => {
    setFailedImages(new Set());
  }, [transportBox.items]);

  const [overdraftPending, setOverdraftPending] = useState<{
    item: ManufacturedProductInventoryItem;
    amount: number;
  } | null>(null);

  const { data: inventoryData, isLoading: inventoryLoading, error: inventoryError } =
    useManufacturedProductInventoryQuery({ onlyWithStock: true });

  const filteredInventory = (inventoryData?.items ?? []).filter((item) => {
    if (!manufacturedSearch.trim()) return true;
    const q = manufacturedSearch.toLowerCase();
    return (
      item.productName.toLowerCase().includes(q) ||
      item.productCode.toLowerCase().includes(q) ||
      (item.lotNumber ?? "").toLowerCase().includes(q)
    );
  });


  return (
    <div>
      {/* Add Item Section - only for Opened state */}
      {isFormEditable("items") && (
        <div className="bg-gray-50 p-4 mb-6 rounded-lg">
          {/* Tab Switcher */}
          <div className="flex gap-1 mb-4 border-b border-gray-200">
            <button
              type="button"
              onClick={() => setActiveAddTab("manufactured")}
              className={`flex items-center gap-1.5 px-4 py-2 text-sm font-medium border-b-2 transition-colors ${
                activeAddTab === "manufactured"
                  ? "border-green-500 text-green-700"
                  : "border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300"
              }`}
            >
              <FlaskConical className="h-4 w-4" />
              Vyrobené produkty
            </button>
            <button
              type="button"
              onClick={() => setActiveAddTab("catalog")}
              className={`flex items-center gap-1.5 px-4 py-2 text-sm font-medium border-b-2 transition-colors ${
                activeAddTab === "catalog"
                  ? "border-indigo-500 text-indigo-700"
                  : "border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300"
              }`}
            >
              <Package className="h-4 w-4" />
              Katalog
            </button>
          </div>

          {/* Manufactured Tab */}
          {activeAddTab === "manufactured" && (
            <div>
              <input
                type="text"
                value={manufacturedSearch}
                onChange={(e) => setManufacturedSearch(e.target.value)}
                placeholder="Hledat produkt, kód nebo lot..."
                className="w-full mb-3 px-3 py-2 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-green-500 focus:border-transparent"
              />

              {inventoryLoading && (
                <div className="flex items-center justify-center py-6 text-gray-500 text-sm gap-2">
                  <Loader className="h-4 w-4 animate-spin" />
                  Načítám zásoby...
                </div>
              )}

              {inventoryError && (
                <div className="flex items-center gap-2 py-4 text-red-600 text-sm">
                  <AlertCircle className="h-4 w-4 flex-shrink-0" />
                  Chyba při načítání zásob
                </div>
              )}

              {!inventoryLoading && !inventoryError && filteredInventory.length === 0 && (
                <div className="text-center py-6 text-sm text-gray-500">
                  {manufacturedSearch.trim()
                    ? "Žádné výsledky pro zadaný filtr"
                    : "Žádné zásoby vyrobených produktů"}
                </div>
              )}

              {!inventoryLoading && !inventoryError && filteredInventory.length > 0 && (
                <div className="border border-gray-200 rounded-md bg-white max-h-64 overflow-y-auto">
                  {filteredInventory.map((item) => {
                    const lastEntry = lastManufacturedItems.find(
                      (e) =>
                        e.productCode === item.productCode &&
                        (e.lotNumber ?? "") === (item.lotNumber ?? ""),
                    );
                    const defaultAmount = lastEntry
                      ? Math.min(lastEntry.addedAmount, item.amount)
                      : undefined;
                    return (
                      <ManufacturedRow
                        key={item.id}
                        item={item}
                        defaultAmount={defaultAmount}
                        onAdd={(inventoryItem, amount) =>
                          handleAddManufacturedItem({ item: inventoryItem, amount })
                        }
                        onOverdraft={(inventoryItem, amount) =>
                          setOverdraftPending({ item: inventoryItem, amount })
                        }
                      />
                    );
                  })}
                </div>
              )}
            </div>
          )}

          {/* Catalog Tab */}
          {activeAddTab === "catalog" && (
            <>
              <div className="grid grid-cols-1 md:grid-cols-12 gap-3">
                <div className="md:col-span-8">
                  <label className="block text-xs font-medium text-gray-700 mb-1">
                    Produkt/Zboží
                  </label>
                  <CatalogAutocomplete
                    value={selectedProduct}
                    onSelect={setSelectedProduct}
                    placeholder="Začněte psát pro vyhledání..."
                    productTypes={[ProductType.Product, ProductType.Goods]}
                    size="sm"
                    clearable
                    renderItem={(item) => (
                      <div className="flex items-center space-x-3">
                        <Package className="h-4 w-4 text-gray-400 flex-shrink-0" />
                        <div className="min-w-0 flex-1">
                          <div className="text-gray-900 font-medium truncate">
                            {item.productName}
                          </div>
                          <div className="text-xs text-gray-500 mt-1">
                            <span className="font-mono">{item.productCode}</span> •
                            Sklad: {item.stock?.available || 0}
                          </div>
                        </div>
                      </div>
                    )}
                  />
                </div>
                <div className="md:col-span-2">
                  <label className="block text-xs font-medium text-gray-700 mb-1">
                    Množství
                  </label>
                  <input
                    type="number"
                    value={quantityInput}
                    onChange={(e) => setQuantityInput(e.target.value)}
                    onKeyDown={(e) => {
                      if (e.key === "Enter") {
                        e.preventDefault();
                        if (
                          selectedProduct &&
                          quantityInput &&
                          parseFloat(quantityInput) > 0
                        ) {
                          handleAddItem();
                        }
                      }
                    }}
                    step="0.01"
                    min="0"
                    placeholder="0"
                    className="w-full px-3 py-2 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-green-500 focus:border-transparent"
                  />
                </div>
                <div className="md:col-span-2 flex items-end">
                  <button
                    type="button"
                    onClick={handleAddItem}
                    disabled={
                      !selectedProduct ||
                      !quantityInput ||
                      parseFloat(quantityInput) <= 0
                    }
                    className="w-full px-3 py-2 text-sm font-medium text-white bg-green-600 rounded-md hover:bg-green-700 disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    Přidat
                  </button>
                </div>
              </div>

              {/* Quick Add Last Item - one line under form */}
              {lastAddedItem && (
                <div className="mt-3 pt-3 border-t border-gray-200">
                  <div className="flex items-center justify-between text-sm">
                    <div className="flex items-center gap-2 text-emerald-700">
                      <RotateCcw className="h-4 w-4" />
                      <span>
                        <strong>{lastAddedItem.productName}</strong> (
                        {lastAddedItem.productCode}) • {lastAddedItem.amount}
                      </span>
                    </div>
                    <button
                      type="button"
                      onClick={handleQuickAdd}
                      className="px-3 py-1 text-sm font-medium text-white bg-emerald-600 rounded hover:bg-emerald-700 flex items-center gap-1"
                    >
                      <RotateCcw className="h-3 w-3" />
                      Opakovat
                    </button>
                  </div>
                </div>
              )}

              <p className="mt-2 text-xs text-gray-600">
                Začněte psát název nebo kód produktu/zboží pro vyhledání. Po výběru
                položky zadejte množství pro přidání do boxu.
                {selectedProduct && (
                  <span className="text-green-600 ml-1">
                    ✓ Vybrán: {selectedProduct.productName} (
                    {selectedProduct.productCode})
                  </span>
                )}
              </p>
            </>
          )}
        </div>
      )}

      {/* Items List */}
      {transportBox.items && transportBox.items.length > 0 ? (
        <div
          className="overflow-auto"
          style={{ minHeight: "200px", maxHeight: "40vh" }}
        >
          <table className="w-full table-fixed divide-y divide-gray-200">
            <thead className="bg-gray-50 sticky top-0 z-10">
              <tr>
                <th className="w-24 px-2 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Kód
                </th>
                <th className="px-2 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Název
                </th>
                <th className="w-16 px-2 py-2 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Množ.
                </th>
                <th className="w-20 px-2 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Datum
                </th>
                <th className="w-16 px-2 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Kdo
                </th>
                {isFormEditable("items") && (
                  <th className="w-12 px-2 py-2 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Akce
                  </th>
                )}
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {transportBox.items.map((item) => (
                <tr key={item.id} className="hover:bg-gray-50">
                  <td className="px-2 py-2 text-sm font-mono text-gray-900">
                    <div className="truncate" title={item.productCode}>
                      {item.productCode}
                    </div>
                  </td>
                  <td className="px-2 py-2 text-sm text-gray-900">
                    <div className="flex items-center gap-2">
                      {item.imageUrl && !failedImages.has(item.id ?? -1) ? (
                        <img
                          src={item.imageUrl}
                          alt={item.productName || item.productCode || ""}
                          className="w-12 h-12 object-cover rounded flex-shrink-0"
                          onError={() => {
                            if (item.id !== undefined) {
                              setFailedImages((prev) => new Set(prev).add(item.id!));
                            }
                          }}
                        />
                      ) : (
                        <div
                          data-testid="product-thumbnail-placeholder"
                          className="w-12 h-12 bg-gray-200 rounded flex-shrink-0"
                        />
                      )}
                      <div className="min-w-0">
                        <div className="truncate" title={item.productName || "-"}>
                          {item.productName || "-"}
                        </div>
                        {(item.lotNumber || item.expirationDate) && (
                          <div className="text-xs text-gray-500 mt-0.5 flex gap-3">
                            {item.lotNumber && <span>Lot: {item.lotNumber}</span>}
                            {item.expirationDate && (
                              <span>Exp: {item.expirationDate.toISOString().slice(0, 10)}</span>
                            )}
                          </div>
                        )}
                      </div>
                    </div>
                  </td>
                  <td className="px-2 py-2 text-sm text-gray-900 text-right font-medium">
                    {item.amount}
                  </td>
                  <td className="px-2 py-2 text-xs text-gray-600">
                    <div className="truncate">{formatDate(item.dateAdded)}</div>
                  </td>
                  <td className="px-2 py-2 text-xs text-gray-600">
                    <div className="truncate" title={item.userAdded || "-"}>
                      {item.userAdded?.split(" ")[0] || "-"}
                    </div>
                  </td>
                  {isFormEditable("items") && (
                    <td className="px-2 py-2 text-right">
                      <button
                        onClick={() => item.id && handleRemoveItem(item.id)}
                        disabled={!item.id}
                        className="text-red-600 hover:text-red-900 p-1 rounded hover:bg-red-50 disabled:opacity-50 disabled:cursor-not-allowed"
                        title="Odebrat položku"
                      >
                        <Trash2 className="h-3 w-3" />
                      </button>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <div className="text-center py-8">
          <Tag className="mx-auto h-12 w-12 text-gray-400" />
          <h3 className="mt-2 text-sm font-medium text-gray-900">
            Žádné položky
          </h3>
          <p className="mt-1 text-sm text-gray-500">
            Tento transportní box neobsahuje žádné položky.
          </p>
        </div>
      )}

      {overdraftPending !== null && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4">
          <div className="bg-white rounded-xl shadow-2xl w-full max-w-sm p-6">
            <div className="flex items-start gap-3 mb-5">
              <AlertCircle className="h-6 w-6 text-amber-500 flex-shrink-0 mt-0.5" />
              <div>
                <p className="font-semibold text-gray-900">{overdraftPending.item.productName}</p>
                <p className="mt-1 text-sm text-gray-600">
                  Na skladě je pouze <strong>{overdraftPending.item.amount}</strong>, požadováno <strong>{overdraftPending.amount}</strong>.
                </p>
              </div>
            </div>
            <div className="flex flex-col gap-3">
              <button
                type="button"
                onClick={() => {
                  handleAddManufacturedItem({ item: overdraftPending.item, amount: overdraftPending.amount, allowNegativeStock: true });
                  setOverdraftPending(null);
                }}
                className="w-full py-4 text-base font-semibold text-white bg-amber-600 rounded-lg hover:bg-amber-700 active:bg-amber-800"
              >
                Přidat záporný stav ({overdraftPending.amount}ks, {overdraftPending.amount - overdraftPending.item.amount} chybí)
              </button>
              <button
                type="button"
                onClick={() => {
                  handleAddManufacturedItem({ item: overdraftPending.item, amount: overdraftPending.item.amount });
                  setOverdraftPending(null);
                }}
                className="w-full py-4 text-base font-semibold text-gray-800 bg-gray-100 rounded-lg hover:bg-gray-200 active:bg-gray-300"
              >
                Přidat pouze zbývající ({overdraftPending.item.amount}ks)
              </button>
              <button
                type="button"
                onClick={() => setOverdraftPending(null)}
                className="w-full py-2 text-sm text-gray-500 hover:text-gray-700"
              >
                Zrušit
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default TransportBoxItems;
