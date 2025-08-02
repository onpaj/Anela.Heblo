import React from 'react';
import { X, Package, BarChart3, MapPin, Hash, Layers, Loader2, AlertCircle, DollarSign } from 'lucide-react';
import { CatalogItemDto, ProductType, useCatalogDetail, CatalogSalesRecordDto, CatalogConsumedRecordDto } from '../../api/hooks/useCatalog';
import {
  Chart as ChartJS,
  CategoryScale,
  LinearScale,
  BarElement,
  Title,
  Tooltip,
  Legend,
  LineElement,
  PointElement,
} from 'chart.js';
import { Line } from 'react-chartjs-2';

ChartJS.register(
  CategoryScale,
  LinearScale,
  BarElement,
  Title,
  Tooltip,
  Legend,
  LineElement,
  PointElement
);

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

  // Fetch detailed data from API
  const { data: detailData, isLoading: detailLoading, error: detailError } = useCatalogDetail(item?.productCode || '');

  // Add keyboard event listener for Esc key
  React.useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && isOpen) {
        onClose();
      }
    };

    if (isOpen) {
      document.addEventListener('keydown', handleKeyDown);
    }

    return () => {
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [isOpen, onClose]);

  if (!isOpen || !item) {
    return null;
  }

  const handleBackdropClick = (e: React.MouseEvent) => {
    if (e.target === e.currentTarget) {
      onClose();
    }
  };


  return (
    <div 
      className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center p-4 z-50"
      onClick={handleBackdropClick}
    >
      <div className="bg-white rounded-lg shadow-xl max-w-7xl w-full max-h-[90vh] overflow-hidden">
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
              <div className="grid grid-cols-1 lg:grid-cols-2 gap-8 h-full">
                {/* Left Column - Product Information */}
                <div className="space-y-6 overflow-y-auto">
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
                  <div className="space-y-3">
                    <h3 className="text-lg font-medium text-gray-900 flex items-center">
                      <BarChart3 className="h-5 w-5 mr-2 text-gray-500" />
                      Skladové zásoby
                    </h3>
                    
                    <div className="bg-gray-50 rounded-lg p-3 space-y-2">
                      {/* Available stock - highlighted */}
                      <div className="flex justify-between items-center">
                        <span className="text-sm font-medium text-gray-600">Dostupné:</span>
                        <span className="inline-flex items-center px-2 py-1 rounded-full text-sm font-semibold bg-green-100 text-green-800">
                          {Math.round(item.stock.available * 100) / 100}
                        </span>
                      </div>
                      
                      {/* Other stock info in compact grid */}
                      <div className="grid grid-cols-2 gap-x-4 gap-y-1 text-sm border-t border-gray-200 pt-2">
                        <div className="flex justify-between">
                          <span className="text-gray-600">Shoptet:</span>
                          <span className="font-medium">{Math.round(item.stock.eshop * 100) / 100}</span>
                        </div>
                        
                        <div className="flex justify-between">
                          <span className="text-gray-600">Transport:</span>
                          <span className="font-medium">{Math.round(item.stock.transport * 100) / 100}</span>
                        </div>
                        
                        <div className="flex justify-between">
                          <span className="text-gray-600">ABRA:</span>
                          <span className="font-medium">{Math.round(item.stock.erp * 100) / 100}</span>
                        </div>
                        
                        <div className="flex justify-between">
                          <span className="text-gray-600">Rezervované:</span>
                          <span className="font-medium">{Math.round(item.stock.reserve * 100) / 100}</span>
                        </div>
                      </div>
                    </div>
                  </div>

                  {/* Price Information */}
                  <div className="space-y-3">
                    <h3 className="text-lg font-medium text-gray-900 flex items-center">
                      <DollarSign className="h-5 w-5 mr-2 text-gray-500" />
                      Cenové informace
                    </h3>
                    
                    <div className="bg-gray-50 rounded-lg p-3">
                      {/* Check if we have any price data */}
                      {(item.price?.eshopPrice || item.price?.erpPrice) ? (
                        <div className="overflow-x-auto">
                          <table className="w-full text-sm">
                            <thead>
                              <tr className="border-b border-gray-200">
                                <th className="text-left py-2 pr-4 font-medium text-gray-700"></th>
                                <th className="text-center py-2 px-2 font-medium text-gray-700">Shoptet</th>
                                <th className="text-center py-2 pl-2 font-medium text-gray-700">ABRA</th>
                              </tr>
                            </thead>
                            <tbody className="divide-y divide-gray-100">
                              {/* Selling price with VAT row */}
                              <tr>
                                <td className="py-2 pr-4 font-medium text-gray-600">Prodejní s DPH:</td>
                                <td className="text-center py-2 px-2">
                                  {item.price?.eshopPrice?.priceWithVat 
                                    ? `${item.price.eshopPrice.priceWithVat.toLocaleString('cs-CZ', { minimumFractionDigits: 0 })} Kč`
                                    : '-'
                                  }
                                </td>
                                <td className="text-center py-2 pl-2">
                                  {item.price?.erpPrice?.priceWithVat 
                                    ? `${item.price.erpPrice.priceWithVat.toLocaleString('cs-CZ', { minimumFractionDigits: 0 })} Kč`
                                    : '-'
                                  }
                                </td>
                              </tr>
                              
                              {/* Selling price without VAT row */}
                              <tr>
                                <td className="py-2 pr-4 font-medium text-gray-600">Prodejní bez DPH:</td>
                                <td className="text-center py-2 px-2">-</td>
                                <td className="text-center py-2 pl-2">
                                  {item.price?.erpPrice?.priceWithoutVat 
                                    ? `${item.price.erpPrice.priceWithoutVat.toLocaleString('cs-CZ', { minimumFractionDigits: 0 })} Kč`
                                    : '-'
                                  }
                                </td>
                              </tr>
                              
                              {/* Purchase price row */}
                              <tr>
                                <td className="py-2 pr-4 font-medium text-gray-600">Nákupní:</td>
                                <td className="text-center py-2 px-2">
                                  {item.price?.eshopPrice?.purchasePrice 
                                    ? `${item.price.eshopPrice.purchasePrice.toLocaleString('cs-CZ', { minimumFractionDigits: 0 })} Kč`
                                    : '-'
                                  }
                                </td>
                                <td className="text-center py-2 pl-2">
                                  {item.price?.erpPrice?.purchasePrice 
                                    ? `${item.price.erpPrice.purchasePrice.toLocaleString('cs-CZ', { minimumFractionDigits: 0 })} Kč`
                                    : '-'
                                  }
                                </td>
                              </tr>
                            </tbody>
                          </table>
                        </div>
                      ) : (
                        <div className="text-center text-gray-500 py-4">
                          <span className="text-sm">Cenové informace nejsou k dispozici</span>
                        </div>
                      )}
                    </div>
                  </div>

                  {/* Properties */}
                  <div className="space-y-4">
                    <h3 className="text-lg font-medium text-gray-900 flex items-center">
                      <Layers className="h-5 w-5 mr-2 text-gray-500" />
                      Vlastnosti produktu
                    </h3>
                    
                    <div className="bg-gray-50 rounded-lg p-4">
                      <div className="grid grid-cols-3 gap-4">
                        <div className="text-center">
                          <span className="text-xs font-medium text-gray-600 block mb-1">
                            Optimální zásoby (dny)
                          </span>
                          <span className="text-lg font-semibold text-gray-900">
                            {item.properties.optimalStockDaysSetup || '-'}
                          </span>
                        </div>
                        
                        <div className="text-center">
                          <span className="text-xs font-medium text-gray-600 block mb-1">
                            Min. zásoba
                          </span>
                          <span className="text-lg font-semibold text-gray-900">
                            {item.properties.stockMinSetup || '-'}
                          </span>
                        </div>
                        
                        <div className="text-center">
                          <span className="text-xs font-medium text-gray-600 block mb-1">
                            Velikost šarže
                          </span>
                          <span className="text-lg font-semibold text-gray-900">
                            {item.properties.batchSize || '-'}
                          </span>
                        </div>
                      </div>
                    </div>
                  </div>
                </div>

                {/* Right Column - Charts */}
                <div className="space-y-4">
                  <div className="h-full flex flex-col">
                    <h3 className="text-lg font-medium text-gray-900 mb-4 flex items-center">
                      <BarChart3 className="h-5 w-5 mr-2 text-gray-500" />
                      {item.type === ProductType.Material ? 'Spotřeba za posledních 13 měsíců' : 'Prodeje za posledních 13 měsíců'}
                    </h3>
                    
                    <div className="flex-1 bg-gray-50 rounded-lg p-4 mb-4">
                      <ProductChart 
                        productType={item.type}
                        salesData={detailData?.historicalData.salesHistory || []}
                        consumedData={detailData?.historicalData.consumedHistory || []}
                      />
                    </div>

                    {/* Summary Section */}
                    <ProductSummary 
                      productType={item.type}
                      salesData={detailData?.historicalData.salesHistory || []}
                      consumedData={detailData?.historicalData.consumedHistory || []}
                    />
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

// ProductChart Component - displays sales or consumption data based on product type
interface ProductChartProps {
  productType: ProductType;
  salesData: CatalogSalesRecordDto[];
  consumedData: CatalogConsumedRecordDto[];
}

const ProductChart: React.FC<ProductChartProps> = ({ productType, salesData, consumedData }) => {
  // Generate last 13 months labels
  const generateMonthLabels = () => {
    const months = [];
    const now = new Date();
    
    for (let i = 12; i >= 0; i--) {
      const date = new Date(now.getFullYear(), now.getMonth() - i, 1);
      months.push(date.toLocaleDateString('cs-CZ', { month: 'short', year: 'numeric' }));
    }
    
    return months;
  };

  // Map data to monthly array based on year/month
  const mapDataToMonthlyArray = (data: CatalogSalesRecordDto[] | CatalogConsumedRecordDto[], valueKey: 'amountTotal' | 'amount') => {
    const monthlyData = new Array(13).fill(0);
    const now = new Date();
    const currentYear = now.getFullYear();
    const currentMonth = now.getMonth() + 1; // JavaScript months are 0-based, convert to 1-based
    
    // Create a map for quick lookup of data by year-month key
    const dataMap = new Map<string, number>();
    data.forEach(record => {
      const key = `${record.year}-${record.month}`;
      const value = (record as any)[valueKey] || 0;
      dataMap.set(key, value);
    });
    
    // Fill the array with data for the last 13 months
    for (let i = 0; i < 13; i++) {
      const monthsBack = 12 - i; // 12 months back to current month
      let adjustedYear = currentYear;
      let adjustedMonth = currentMonth - monthsBack;
      
      // Handle year transitions
      if (adjustedMonth <= 0) {
        adjustedYear--;
        adjustedMonth += 12;
      }
      
      const key = `${adjustedYear}-${adjustedMonth}`;
      const value = dataMap.get(key) || 0;
      monthlyData[i] = value;
    }
    return monthlyData;
  };

  const monthLabels = generateMonthLabels();
  
  // Determine which data to use based on product type
  const chartData = {
    labels: monthLabels,
    datasets: [
      {
        label: productType === ProductType.Material ? 'Spotřeba' : 'Prodeje',
        data: productType === ProductType.Material 
          ? mapDataToMonthlyArray(consumedData, 'amount')
          : mapDataToMonthlyArray(salesData, 'amountTotal'),
        backgroundColor: productType === ProductType.Material
          ? 'rgba(251, 146, 60, 0.2)'  // Orange for consumption
          : 'rgba(59, 130, 246, 0.2)',  // Blue for sales
        borderColor: productType === ProductType.Material
          ? 'rgba(251, 146, 60, 1)'
          : 'rgba(59, 130, 246, 1)',
        borderWidth: 2,
        tension: 0.1,
      },
    ],
  };

  const chartOptions = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
      legend: {
        position: 'top' as const,
      },
      title: {
        display: false,
      },
    },
    scales: {
      y: {
        beginAtZero: true,
        title: {
          display: true,
          text: productType === ProductType.Material ? 'Množství' : 'Kusů prodáno',
        },
      },
      x: {
        title: {
          display: true,
          text: 'Měsíc',
        },
      },
    },
  };

  // Check if we have any data
  const hasData = productType === ProductType.Material 
    ? consumedData.length > 0 
    : salesData.length > 0;

  if (!hasData) {
    return (
      <div className="flex items-center justify-center h-96">
        <div className="text-center text-gray-500">
          <BarChart3 className="h-12 w-12 mx-auto mb-2 text-gray-300" />
          <p>Žádná data pro zobrazení grafu</p>
          <p className="text-sm">{productType === ProductType.Material ? 'Spotřeba' : 'Prodeje'} za posledních 13 měsíců</p>
        </div>
      </div>
    );
  }

  return (
    <div className="h-96">
      <Line data={chartData} options={chartOptions} />
    </div>
  );
};

// ProductSummary Component - displays summary statistics for sales or consumption
interface ProductSummaryProps {
  productType: ProductType;
  salesData: CatalogSalesRecordDto[];
  consumedData: CatalogConsumedRecordDto[];
}

const ProductSummary: React.FC<ProductSummaryProps> = ({ productType, salesData, consumedData }) => {
  // Calculate summary statistics
  const calculateSummary = () => {
    const currentDate = new Date();
    const oneYearAgo = new Date(currentDate.getFullYear() - 1, currentDate.getMonth(), 1);
    
    let totalForLastYear = 0;
    let monthsWithData = 0;
    
    if (productType === ProductType.Material) {
      // Calculate total consumption for last 12 months
      consumedData.forEach(record => {
        const recordDate = new Date(record.year, record.month - 1); // month is 1-based
        if (recordDate >= oneYearAgo) {
          totalForLastYear += record.amount || 0;
          if ((record.amount || 0) > 0) monthsWithData++;
        }
      });
    } else {
      // Calculate total sales for last 12 months
      salesData.forEach(record => {
        const recordDate = new Date(record.year, record.month - 1); // month is 1-based
        if (recordDate >= oneYearAgo) {
          totalForLastYear += record.amountTotal || 0;
          if ((record.amountTotal || 0) > 0) monthsWithData++;
        }
      });
    }
    
    const averageMonthly = monthsWithData > 0 ? totalForLastYear / 12 : 0;
    
    return {
      totalForLastYear: Math.round(totalForLastYear * 100) / 100,
      averageMonthly: Math.round(averageMonthly * 100) / 100,
      monthsWithData
    };
  };

  const summary = calculateSummary();
  const isMaterial = productType === ProductType.Material;

  return (
    <div className="bg-white rounded-lg border border-gray-200 p-4 shadow-sm">
      <h4 className="text-md font-medium text-gray-900 mb-3 flex items-center">
        <BarChart3 className="h-4 w-4 mr-2 text-gray-500" />
        Celkové shrnutí
      </h4>
      
      <div className="grid grid-cols-2 gap-4">
        <div className="text-center p-3 bg-blue-50 rounded-lg">
          <div className="text-sm font-medium text-gray-600 mb-1">
            {isMaterial ? 'Celková spotřeba za rok' : 'Celkové prodeje za rok'}
          </div>
          <div className="text-2xl font-bold text-blue-900">
            {summary.totalForLastYear.toLocaleString('cs-CZ')}
          </div>
          <div className="text-xs text-gray-500 mt-1">
            za posledních 12 měsíců
          </div>
        </div>
        
        <div className="text-center p-3 bg-green-50 rounded-lg">
          <div className="text-sm font-medium text-gray-600 mb-1">
            {isMaterial ? 'Průměrná spotřeba/měsíc' : 'Průměrné prodeje/měsíc'}
          </div>
          <div className="text-2xl font-bold text-green-900">
            {summary.averageMonthly.toLocaleString('cs-CZ')}
          </div>
          <div className="text-xs text-gray-500 mt-1">
            měsíční průměr
          </div>
        </div>
      </div>
      
      {summary.monthsWithData === 0 && (
        <div className="mt-3 text-center text-sm text-gray-500">
          Žádná data za posledních 12 měsíců
        </div>
      )}
    </div>
  );
};

export default CatalogDetail;