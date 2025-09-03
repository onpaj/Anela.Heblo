import React from 'react';
import { Package, Tag, Trash2 } from 'lucide-react';
import { TransportBoxItemsProps } from './TransportBoxTypes';

const TransportBoxItems: React.FC<TransportBoxItemsProps> = ({
  transportBox,
  isFormEditable,
  formatDate,
  handleRemoveItem,
  productInput,
  setProductInput,
  quantityInput,
  setQuantityInput,
  selectedProduct,
  setSelectedProduct,
  handleAddItem,
  autocompleteData,
  autocompleteLoading,
}) => {
  return (
    <div>
      {/* Add Item Section - only for Opened state */}
      {isFormEditable('items') && (
        <div className="bg-gray-50 p-4 mb-6 rounded-lg">
          <h3 className="text-lg font-medium text-gray-900 mb-4 flex items-center gap-2">
            <Package className="h-5 w-5" />
            Přidat položku
          </h3>
          
          <div className="grid grid-cols-1 md:grid-cols-12 gap-3">
            <div className="md:col-span-8">
              <label className="block text-xs font-medium text-gray-700 mb-1">Produkt/Zboží</label>
              <div className="relative" data-autocomplete-container>
                <input
                  type="text"
                  value={selectedProduct ? selectedProduct.productName : productInput}
                  onChange={(e) => {
                    setProductInput(e.target.value);
                    if (selectedProduct && e.target.value !== selectedProduct.productName) {
                      setSelectedProduct(null);
                    }
                  }}
                  placeholder="Začněte psát pro vyhledání..."
                  className="w-full px-3 py-2 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-green-500 focus:border-transparent"
                />
                
                {/* Autocomplete dropdown */}
                {productInput && !selectedProduct && autocompleteData?.items && autocompleteData.items.length > 0 && (
                  <div className="absolute z-50 w-full mt-1 bg-white border border-gray-300 rounded-md shadow-lg max-h-60 overflow-auto">
                    <div className="py-1">
                      {autocompleteData.items.map((item: any) => (
                        <button
                          key={item.productCode}
                          onClick={() => {
                            setSelectedProduct(item);
                            setProductInput(item.productName || '');
                          }}
                          className="w-full px-3 py-2 text-left hover:bg-gray-50 focus:outline-none focus:bg-gray-50"
                        >
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
                        </button>
                      ))}
                    </div>
                  </div>
                )}
                
                {/* Loading indicator */}
                {autocompleteLoading && productInput && (
                  <div className="absolute z-50 w-full mt-1 bg-white border border-gray-300 rounded-md shadow-lg">
                    <div className="px-3 py-2 text-sm text-gray-500">
                      Načítám...
                    </div>
                  </div>
                )}
                
                {/* No results */}
                {productInput && !autocompleteLoading && autocompleteData?.items && autocompleteData.items.length === 0 && !selectedProduct && (
                  <div className="absolute z-50 w-full mt-1 bg-white border border-gray-300 rounded-md shadow-lg">
                    <div className="px-3 py-2 text-sm text-gray-500">
                      Žádné položky nenalezeny
                    </div>
                  </div>
                )}
              </div>
            </div>
            <div className="md:col-span-2">
              <label className="block text-xs font-medium text-gray-700 mb-1">Množství</label>
              <input
                type="number"
                value={quantityInput}
                onChange={(e) => setQuantityInput(e.target.value)}
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
        <div className="overflow-auto" style={{ minHeight: '300px', maxHeight: '50vh' }}>
          <table className="w-full table-fixed divide-y divide-gray-200">
            <thead className="bg-gray-50 sticky top-0 z-10">
              <tr>
                <th className="w-32 px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Kód produktu
                </th>
                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Název produktu
                </th>
                <th className="w-20 px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Množství
                </th>
                <th className="w-28 px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Přidáno
                </th>
                <th className="w-20 px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Přidal
                </th>
                {isFormEditable('items') && (
                  <th className="w-16 px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Akce
                  </th>
                )}
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {transportBox.items.map((item) => (
                <tr key={item.id} className="hover:bg-gray-50">
                  <td className="px-4 py-4 text-sm font-medium text-gray-900 font-mono">
                    {item.productCode}
                  </td>
                  <td className="px-4 py-4 text-sm text-gray-900">
                    <div className="truncate" title={item.productName || '-'}>
                      {item.productName || '-'}
                    </div>
                  </td>
                  <td className="px-4 py-4 text-sm text-gray-900 text-right">
                    {item.amount}
                  </td>
                  <td className="px-4 py-4 text-sm text-gray-900">
                    <div className="text-xs">
                      {formatDate(item.dateAdded)}
                    </div>
                  </td>
                  <td className="px-4 py-4 text-sm text-gray-900">
                    <div className="truncate text-xs" title={item.userAdded || '-'}>
                      {item.userAdded || '-'}
                    </div>
                  </td>
                  {isFormEditable('items') && (
                    <td className="px-4 py-4 text-right">
                      <button
                        onClick={() => item.id && handleRemoveItem(item.id)}
                        disabled={!item.id}
                        className="text-red-600 hover:text-red-900 p-1 rounded hover:bg-red-50 disabled:opacity-50 disabled:cursor-not-allowed"
                        title="Odebrat položku"
                      >
                        <Trash2 className="h-4 w-4" />
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