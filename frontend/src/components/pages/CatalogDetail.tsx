import React, { useState } from 'react';
import { X, Package, BarChart3, MapPin, Hash, Calendar, Layers, TrendingUp, ShoppingCart, Truck, Factory, Loader2, AlertCircle } from 'lucide-react';
import { CatalogItemDto, ProductType, useCatalogDetail, CatalogSalesRecordDto, CatalogPurchaseRecordDto, CatalogConsumedRecordDto } from '../../api/hooks/useCatalog';

interface CatalogDetailProps {
  item: CatalogItemDto | null;
  isOpen: boolean;
  onClose: () => void;
}


const productTypeLabels: Record<ProductType, string> = {
  [ProductType.Product]: 'Produkt',
  [ProductType.Goods]: 'Zboží',
  [ProductType.Material]: 'Materiál',
  [ProductType.SemiProduct]: 'Polotovar',
  [ProductType.UNDEFINED]: 'Nedefinováno',
};

const productTypeColors: Record<ProductType, string> = {
  [ProductType.Product]: 'bg-blue-100 text-blue-800',
  [ProductType.Goods]: 'bg-green-100 text-green-800',
  [ProductType.Material]: 'bg-orange-100 text-orange-800',
  [ProductType.SemiProduct]: 'bg-purple-100 text-purple-800',
  [ProductType.UNDEFINED]: 'bg-gray-100 text-gray-800',
};

const CatalogDetail: React.FC<CatalogDetailProps> = ({ item, isOpen, onClose }) => {
  const [activeTab, setActiveTab] = useState<'sales' | 'purchases' | 'consumed'>('sales');

  // Fetch detailed data from API
  const { data: detailData, isLoading: detailLoading, error: detailError } = useCatalogDetail(item?.productCode || '');

  if (!isOpen || !item) {
    return null;
  }

  const handleBackdropClick = (e: React.MouseEvent) => {
    if (e.target === e.currentTarget) {
      onClose();
    }
  };

  const formatSeasonMonths = (months: number[]) => {
    if (!months || months.length === 0) return 'Není nastaveno';
    
    const monthNames = [
      'Led', 'Úno', 'Bře', 'Dub', 'Kvě', 'Čvn',
      'Čvc', 'Srp', 'Zář', 'Říj', 'Lis', 'Pro'
    ];
    
    return months.map(m => monthNames[m - 1]).join(', ');
  };

  return (
    <div 
      className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center p-4 z-50"
      onClick={handleBackdropClick}
    >
      <div className="bg-white rounded-lg shadow-xl max-w-4xl w-full max-h-[90vh] overflow-hidden">
        {/* Header */}
        <div className="flex items-center justify-between p-6 border-b border-gray-200">
          <div className="flex items-center space-x-3">
            <Package className="h-6 w-6 text-indigo-600" />
            <div>
              <h2 className="text-xl font-semibold text-gray-900">{item.productName}</h2>
              <p className="text-sm text-gray-500">Kód: {item.productCode}</p>
            </div>
          </div>
          <button
            onClick={onClose}
            className="text-gray-400 hover:text-gray-600 transition-colors"
          >
            <X className="h-6 w-6" />
          </button>
        </div>

        {/* Content */}
        <div className="p-6 overflow-y-auto max-h-[calc(90vh-120px)]">
          {detailLoading ? (
            <div className="flex items-center justify-center h-64">
              <div className="flex items-center space-x-2">
                <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
                <div className="text-gray-500">Načítání detailů produktu...</div>
              </div>
            </div>
          ) : detailError ? (
            <div className="flex items-center justify-center h-64">
              <div className="flex items-center space-x-2 text-red-600">
                <AlertCircle className="h-5 w-5" />
                <div>Chyba při načítání detailů: {detailError.message}</div>
              </div>
            </div>
          ) : (
            <>
              <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            
            {/* Basic Information */}
            <div className="space-y-4">
              <h3 className="text-lg font-medium text-gray-900 flex items-center">
                <Hash className="h-5 w-5 mr-2 text-gray-500" />
                Základní informace
              </h3>
              
              <div className="bg-gray-50 rounded-lg p-4 space-y-3">
                <div className="flex justify-between items-center">
                  <span className="text-sm font-medium text-gray-600">Typ produktu:</span>
                  <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${productTypeColors[item.type]}`}>
                    {productTypeLabels[item.type]}
                  </span>
                </div>
                
                <div className="flex justify-between items-center">
                  <span className="text-sm font-medium text-gray-600 flex items-center">
                    <MapPin className="h-4 w-4 mr-1" />
                    Umístění:
                  </span>
                  <span className="text-sm text-gray-900">{item.location || 'Není uvedeno'}</span>
                </div>
                
                <div className="flex justify-between items-center">
                  <span className="text-sm font-medium text-gray-600">Min. objednávka:</span>
                  <span className="text-sm text-gray-900">{item.minimalOrderQuantity || 'Není uvedeno'}</span>
                </div>
                
                <div className="flex justify-between items-center">
                  <span className="text-sm font-medium text-gray-600">Min. výroba:</span>
                  <span className="text-sm text-gray-900">{item.minimalManufactureQuantity || 'Není uvedeno'}</span>
                </div>
              </div>
            </div>

            {/* Stock Information */}
            <div className="space-y-4">
              <h3 className="text-lg font-medium text-gray-900 flex items-center">
                <BarChart3 className="h-5 w-5 mr-2 text-gray-500" />
                Skladové zásoby
              </h3>
              
              <div className="bg-gray-50 rounded-lg p-4 space-y-3">
                <div className="flex justify-between items-center">
                  <span className="text-sm font-medium text-gray-600">Dostupné:</span>
                  <span className="inline-flex items-center px-3 py-1 rounded-full text-sm font-semibold bg-green-100 text-green-800">
                    {item.stock.available}
                  </span>
                </div>
                
                <div className="flex justify-between items-center">
                  <span className="text-sm font-medium text-gray-600">E-shop:</span>
                  <span className="text-sm text-gray-900">{item.stock.eshop}</span>
                </div>
                
                <div className="flex justify-between items-center">
                  <span className="text-sm font-medium text-gray-600">ERP sklad:</span>
                  <span className="text-sm text-gray-900">{item.stock.erp}</span>
                </div>
                
                <div className="flex justify-between items-center">
                  <span className="text-sm font-medium text-gray-600">Transport:</span>
                  <span className="text-sm text-gray-900">{item.stock.transport}</span>
                </div>
                
                <div className="flex justify-between items-center">
                  <span className="text-sm font-medium text-gray-600">Rezervované:</span>
                  <span className="text-sm text-gray-900">{item.stock.reserve}</span>
                </div>
              </div>
            </div>

            {/* Properties */}
            <div className="space-y-4 md:col-span-2">
              <h3 className="text-lg font-medium text-gray-900 flex items-center">
                <Layers className="h-5 w-5 mr-2 text-gray-500" />
                Vlastnosti produktu
              </h3>
              
              <div className="bg-gray-50 rounded-lg p-4">
                <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                  <div className="flex flex-col">
                    <span className="text-sm font-medium text-gray-600 flex items-center mb-1">
                      <TrendingUp className="h-4 w-4 mr-1" />
                      Optimální zásoby (dny)
                    </span>
                    <span className="text-lg font-semibold text-gray-900">
                      {item.properties.optimalStockDaysSetup || 'Není nastaveno'}
                    </span>
                  </div>
                  
                  <div className="flex flex-col">
                    <span className="text-sm font-medium text-gray-600 mb-1">
                      Minimální zásoba
                    </span>
                    <span className="text-lg font-semibold text-gray-900">
                      {item.properties.stockMinSetup || 'Není nastaveno'}
                    </span>
                  </div>
                  
                  <div className="flex flex-col">
                    <span className="text-sm font-medium text-gray-600 mb-1">
                      Velikost šarže
                    </span>
                    <span className="text-lg font-semibold text-gray-900">
                      {item.properties.batchSize || 'Není nastaveno'}
                    </span>
                  </div>
                </div>
                
                <div className="mt-4 pt-4 border-t border-gray-200">
                  <span className="text-sm font-medium text-gray-600 flex items-center mb-2">
                    <Calendar className="h-4 w-4 mr-1" />
                    Sezonní měsíce
                  </span>
                  <div className="text-sm text-gray-900 bg-white rounded px-3 py-2 border">
                    {formatSeasonMonths(item.properties.seasonMonths)}
                  </div>
                </div>
              </div>
            </div>

            {/* Historical Data Section */}
            <div className="mt-8 md:col-span-2">
              <h3 className="text-lg font-medium text-gray-900 mb-4">Historická data</h3>
              
              {/* Tab Navigation */}
              <div className="border-b border-gray-200">
                <nav className="-mb-px flex space-x-8">
                  <button
                    onClick={() => setActiveTab('sales')}
                    className={`py-2 px-1 border-b-2 font-medium text-sm ${
                      activeTab === 'sales'
                        ? 'border-indigo-500 text-indigo-600'
                        : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                    }`}
                  >
                    <div className="flex items-center">
                      <ShoppingCart className="h-4 w-4 mr-2" />
                      Prodeje ({detailData?.historicalData.salesHistory.length || 0})
                    </div>
                  </button>
                  <button
                    onClick={() => setActiveTab('purchases')}
                    className={`py-2 px-1 border-b-2 font-medium text-sm ${
                      activeTab === 'purchases'
                        ? 'border-indigo-500 text-indigo-600'
                        : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                    }`}
                  >
                    <div className="flex items-center">
                      <Truck className="h-4 w-4 mr-2" />
                      Nákupy ({detailData?.historicalData.purchaseHistory.length || 0})
                    </div>
                  </button>
                  <button
                    onClick={() => setActiveTab('consumed')}
                    className={`py-2 px-1 border-b-2 font-medium text-sm ${
                      activeTab === 'consumed'
                        ? 'border-indigo-500 text-indigo-600'
                        : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                    }`}
                  >
                    <div className="flex items-center">
                      <Factory className="h-4 w-4 mr-2" />
                      Spotřeba ({detailData?.historicalData.consumedHistory.length || 0})
                    </div>
                  </button>
                </nav>
              </div>

              {/* Tab Content */}
              <div className="mt-4">
                {activeTab === 'sales' && (
                  <SalesHistoryTable data={detailData?.historicalData.salesHistory || []} />
                )}
                {activeTab === 'purchases' && (
                  <PurchaseHistoryTable data={detailData?.historicalData.purchaseHistory || []} />
                )}
                {activeTab === 'consumed' && (
                  <ConsumedHistoryTable data={detailData?.historicalData.consumedHistory || []} />
                )}
              </div>
            </div>
          </div>
              </>
            )}
        </div>

        {/* Footer */}
        <div className="flex justify-end p-6 border-t border-gray-200 bg-gray-50">
          <button
            onClick={onClose}
            className="bg-gray-600 hover:bg-gray-700 text-white font-medium py-2 px-4 rounded-md transition-colors duration-200"
          >
            Zavřít
          </button>
        </div>
      </div>
    </div>
  );
};

// Sales History Table Component
const SalesHistoryTable: React.FC<{ data: CatalogSalesRecordDto[] }> = ({ data }) => {
  const formatDate = (dateString: string) => {
    return new Date(dateString).toLocaleDateString('cs-CZ');
  };

  if (data.length === 0) {
    return (
      <div className="text-center py-8 text-gray-500">
        Žádné záznamy o prodeji nejsou k dispozici.
      </div>
    );
  }

  return (
    <div className="overflow-x-auto">
      <table className="min-w-full divide-y divide-gray-200">
        <thead className="bg-gray-50">
          <tr>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
              Datum
            </th>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
              Množství celkem
            </th>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
              B2B
            </th>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
              B2C
            </th>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
              Suma celkem
            </th>
          </tr>
        </thead>
        <tbody className="bg-white divide-y divide-gray-200">
          {data.map((record, index) => (
            <tr key={index} className="hover:bg-gray-50">
              <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                {formatDate(record.date)}
              </td>
              <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                {record.amountTotal}
              </td>
              <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                {record.amountB2B}
              </td>
              <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                {record.amountB2C}
              </td>
              <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                {record.sumTotal.toLocaleString('cs-CZ', { style: 'currency', currency: 'CZK' })}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
};

// Purchase History Table Component
const PurchaseHistoryTable: React.FC<{ data: CatalogPurchaseRecordDto[] }> = ({ data }) => {
  const formatDate = (dateString: string) => {
    return new Date(dateString).toLocaleDateString('cs-CZ');
  };

  if (data.length === 0) {
    return (
      <div className="text-center py-8 text-gray-500">
        Žádné záznamy o nákupu nejsou k dispozici.
      </div>
    );
  }

  return (
    <div className="overflow-x-auto">
      <table className="min-w-full divide-y divide-gray-200">
        <thead className="bg-gray-50">
          <tr>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
              Datum
            </th>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
              Dodavatel
            </th>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
              Množství
            </th>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
              Cena/ks
            </th>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
              Celkem
            </th>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
              Doklad
            </th>
          </tr>
        </thead>
        <tbody className="bg-white divide-y divide-gray-200">
          {data.map((record, index) => (
            <tr key={index} className="hover:bg-gray-50">
              <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                {formatDate(record.date)}
              </td>
              <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                {record.supplierName}
              </td>
              <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                {record.amount}
              </td>
              <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                {record.pricePerPiece.toLocaleString('cs-CZ', { style: 'currency', currency: 'CZK' })}
              </td>
              <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                {record.priceTotal.toLocaleString('cs-CZ', { style: 'currency', currency: 'CZK' })}
              </td>
              <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                {record.documentNumber}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
};

// Consumed History Table Component
const ConsumedHistoryTable: React.FC<{ data: CatalogConsumedRecordDto[] }> = ({ data }) => {
  const formatDate = (dateString: string) => {
    return new Date(dateString).toLocaleDateString('cs-CZ');
  };

  if (data.length === 0) {
    return (
      <div className="text-center py-8 text-gray-500">
        Žádné záznamy o spotřebě nejsou k dispozici.
      </div>
    );
  }

  return (
    <div className="overflow-x-auto">
      <table className="min-w-full divide-y divide-gray-200">
        <thead className="bg-gray-50">
          <tr>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
              Datum
            </th>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
              Množství
            </th>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
              Použito ve výrobku
            </th>
          </tr>
        </thead>
        <tbody className="bg-white divide-y divide-gray-200">
          {data.map((record, index) => (
            <tr key={index} className="hover:bg-gray-50">
              <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                {formatDate(record.date)}
              </td>
              <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                {record.amount}
              </td>
              <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                {record.productName}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
};

export default CatalogDetail;