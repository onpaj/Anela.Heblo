import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Search, Filter, AlertCircle, Loader2 } from 'lucide-react';
import { 
  useCatalogQuery, 
  ProductType, 
  type CatalogItemDto, 
  type StockDto, 
  type PropertiesDto 
} from '../../api/hooks/useCatalog';

const productTypeLabels: Record<ProductType, string> = {
  [ProductType.Product]: 'Produkt',
  [ProductType.Goods]: 'Zboží',
  [ProductType.Material]: 'Materiál',
  [ProductType.SemiProduct]: 'Polotovar',
  [ProductType.UNDEFINED]: 'Nedefinováno',
};

const CatalogList: React.FC = () => {
  const { t } = useTranslation();
  
  // Filter states
  const [productNameFilter, setProductNameFilter] = useState('');
  const [productCodeFilter, setProductCodeFilter] = useState('');
  const [productTypeFilter, setProductTypeFilter] = useState<ProductType | ''>('');

  // Use the actual API call
  const { data, isLoading: loading, error } = useCatalogQuery(
    productNameFilter,
    productCodeFilter,
    productTypeFilter
  );

  const filteredItems = data?.items || [];

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2">
          <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
          <div className="text-gray-500">Načítání katalogu...</div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2 text-red-600">
          <AlertCircle className="h-5 w-5" />
          <div>Chyba při načítání katalogu: {error.message}</div>
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold text-gray-900">Seznam produktů</h1>
        <p className="mt-1 text-sm text-gray-500">
          Přehled všech produktů v katalogu
        </p>
      </div>

      {/* Filters */}
      <div className="bg-white shadow rounded-lg p-6">
        <div className="flex items-center mb-4">
          <Filter className="h-5 w-5 text-gray-400 mr-2" />
          <h2 className="text-lg font-medium text-gray-900">Filtry</h2>
        </div>
        
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
          <div>
            <label htmlFor="productName" className="block text-sm font-medium text-gray-700">
              Název produktu
            </label>
            <div className="mt-1 relative rounded-md shadow-sm">
              <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                <Search className="h-4 w-4 text-gray-400" />
              </div>
              <input
                type="text"
                id="productName"
                value={productNameFilter}
                onChange={(e) => setProductNameFilter(e.target.value)}
                className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 sm:text-sm border-gray-300 rounded-md"
                placeholder="Hledat podle názvu..."
              />
            </div>
          </div>

          <div>
            <label htmlFor="productCode" className="block text-sm font-medium text-gray-700">
              Kód produktu
            </label>
            <div className="mt-1 relative rounded-md shadow-sm">
              <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                <Search className="h-4 w-4 text-gray-400" />
              </div>
              <input
                type="text"
                id="productCode"
                value={productCodeFilter}
                onChange={(e) => setProductCodeFilter(e.target.value)}
                className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 sm:text-sm border-gray-300 rounded-md"
                placeholder="Hledat podle kódu..."
              />
            </div>
          </div>

          <div>
            <label htmlFor="productType" className="block text-sm font-medium text-gray-700">
              Typ produktu
            </label>
            <select
              id="productType"
              value={productTypeFilter}
              onChange={(e) => setProductTypeFilter(e.target.value === '' ? '' : Number(e.target.value) as ProductType)}
              className="mt-1 block w-full pl-3 pr-10 py-2 text-base border-gray-300 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm rounded-md"
            >
              <option value="">Všechny typy</option>
              {Object.entries(productTypeLabels).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </div>
        </div>
      </div>

      {/* Data Grid */}
      <div className="bg-white shadow rounded-lg overflow-hidden">
        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Kód produktu
                </th>
                <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Název produktu
                </th>
                <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Typ
                </th>
                <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Sklad
                </th>
                <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Vlastnosti
                </th>
                <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Umístění
                </th>
                <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  MOQ
                </th>
                <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  MMQ
                </th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {filteredItems.map((item) => (
                <tr key={item.productCode} className="hover:bg-gray-50">
                  <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                    {item.productCode}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                    {item.productName}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-800">
                      {productTypeLabels[item.type]}
                    </span>
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    <div className="space-y-1">
                      <div className="flex justify-between">
                        <span className="text-gray-500">E-shop:</span>
                        <span className="font-medium">{item.stock.eshop}</span>
                      </div>
                      <div className="flex justify-between">
                        <span className="text-gray-500">ERP:</span>
                        <span className="font-medium">{item.stock.erp}</span>
                      </div>
                      <div className="flex justify-between">
                        <span className="text-gray-500">Transport:</span>
                        <span className="font-medium">{item.stock.transport}</span>
                      </div>
                      <div className="flex justify-between">
                        <span className="text-gray-500">Rezerva:</span>
                        <span className="font-medium">{item.stock.reserve}</span>
                      </div>
                      <div className="flex justify-between border-t pt-1">
                        <span className="text-gray-500">Dostupné:</span>
                        <span className="font-bold text-green-600">{item.stock.available}</span>
                      </div>
                    </div>
                  </td>
                  <td className="px-6 py-4 text-sm text-gray-500">
                    <div className="space-y-1">
                      <div className="flex justify-between">
                        <span className="text-gray-500">Opt. dny:</span>
                        <span className="font-medium">{item.properties.optimalStockDaysSetup}</span>
                      </div>
                      <div className="flex justify-between">
                        <span className="text-gray-500">Min. sklad:</span>
                        <span className="font-medium">{item.properties.stockMinSetup}</span>
                      </div>
                      <div className="flex justify-between">
                        <span className="text-gray-500">Velikost dávky:</span>
                        <span className="font-medium">{item.properties.batchSize}</span>
                      </div>
                      {item.properties.seasonMonths.length > 0 && (
                        <div className="flex justify-between">
                          <span className="text-gray-500">Sezóna:</span>
                          <span className="font-medium">{item.properties.seasonMonths.join(', ')}</span>
                        </div>
                      )}
                    </div>
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {item.location}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {item.minimalOrderQuantity}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {item.minimalManufactureQuantity}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        
        {filteredItems.length === 0 && (
          <div className="text-center py-8">
            <p className="text-gray-500">Žádné produkty nebyly nalezeny.</p>
          </div>
        )}
      </div>
    </div>
  );
};

export default CatalogList;