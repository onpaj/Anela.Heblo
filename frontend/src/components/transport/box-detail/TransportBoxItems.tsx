import React from 'react';
import { Package, Tag, Trash2, RotateCcw } from 'lucide-react';
import { TransportBoxItemsProps } from './TransportBoxTypes';
import { CatalogAutocomplete } from '../../common/CatalogAutocomplete';
import { ProductType } from '../../../api/generated/api-client';

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
  lastAddedItem,
  handleQuickAdd,
}) => {
  return (
    <div>
      {/* Add Item Section - only for Opened state */}
      {isFormEditable('items') && (
        <div className="bg-gray-50 p-4 mb-6 rounded-lg">
          
          <div className="grid grid-cols-1 md:grid-cols-12 gap-3">
            <div className="md:col-span-8">
              <label className="block text-xs font-medium text-gray-700 mb-1">Produkt/Zboží</label>
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
                        <span className="font-mono">{item.productCode}</span> • Sklad: {item.stock?.available || 0}
                      </div>
                    </div>
                  </div>
                )}
              />
            </div>
            <div className="md:col-span-2">
              <label className="block text-xs font-medium text-gray-700 mb-1">Množství</label>
              <input
                type="number"
                value={quantityInput}
                onChange={(e) => setQuantityInput(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') {
                    e.preventDefault();
                    if (selectedProduct && quantityInput && parseFloat(quantityInput) > 0) {
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
                disabled={!selectedProduct || !quantityInput || parseFloat(quantityInput) <= 0}
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
                    <strong>{lastAddedItem.productName}</strong> ({lastAddedItem.productCode}) • {lastAddedItem.amount}
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
            Začněte psát název nebo kód produktu/zboží pro vyhledání. Po výběru položky zadejte množství pro přidání do boxu.
            {selectedProduct && (
              <span className="text-green-600 ml-1">
                ✓ Vybrán: {selectedProduct.productName} ({selectedProduct.productCode})
              </span>
            )}
          </p>
        </div>
      )}

      {/* Items List */}
      {transportBox.items && transportBox.items.length > 0 ? (
        <div className="overflow-auto" style={{ minHeight: '200px', maxHeight: '40vh' }}>
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
                {isFormEditable('items') && (
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
                    <div className="truncate" title={item.productName || '-'}>
                      {item.productName || '-'}
                    </div>
                  </td>
                  <td className="px-2 py-2 text-sm text-gray-900 text-right font-medium">
                    {item.amount}
                  </td>
                  <td className="px-2 py-2 text-xs text-gray-600">
                    <div className="truncate">
                      {formatDate(item.dateAdded)}
                    </div>
                  </td>
                  <td className="px-2 py-2 text-xs text-gray-600">
                    <div className="truncate" title={item.userAdded || '-'}>
                      {item.userAdded?.split(' ')[0] || '-'}
                    </div>
                  </td>
                  {isFormEditable('items') && (
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
          <h3 className="mt-2 text-sm font-medium text-gray-900">Žádné položky</h3>
          <p className="mt-1 text-sm text-gray-500">
            Tento transportní box neobsahuje žádné položky.
          </p>
        </div>
      )}
    </div>
  );
};

export default TransportBoxItems;