import React, { useState } from 'react';
import { X, Package, BarChart3, MapPin, Hash, Layers, Loader2, AlertCircle, DollarSign, FileText, ShoppingCart, TrendingUp } from 'lucide-react';
import { CatalogItemDto, ProductType, useCatalogDetail, CatalogSalesRecordDto, CatalogConsumedRecordDto, CatalogPurchaseRecordDto, CatalogManufactureRecordDto } from '../../api/hooks/useCatalog';
import { ManufactureCostDto } from '../../api/generated/api-client';
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
  defaultTab?: 'basic' | 'history' | 'margins';
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

const CatalogDetail: React.FC<CatalogDetailProps> = ({ item, isOpen, onClose, defaultTab = 'basic' }) => {
  const [activeTab, setActiveTab] = useState<'basic' | 'history' | 'margins'>(defaultTab);
  const [showFullHistory, setShowFullHistory] = useState(false);
  const [activeChartTab, setActiveChartTab] = useState<'input' | 'output'>('output');

  // Fetch detailed data from API - use 13 months by default, 999 for full history
  const monthsBack = showFullHistory ? 999 : 13;
  const { data: detailData, isLoading: detailLoading, error: detailError } = useCatalogDetail(item?.productCode || '', monthsBack);

  // Reset tab and history state when modal opens with new item or different default tab
  React.useEffect(() => {
    if (isOpen) {
      setActiveTab(defaultTab);
      setShowFullHistory(false);
      setActiveChartTab('output'); // Default to output tab (sales/consumption)
    }
  }, [isOpen, defaultTab, item?.productCode]);

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

  // Helper functions for chart tabs based on ProductType
  const getInputTabName = (productType: ProductType) => {
    switch (productType) {
      case ProductType.Material:
      case ProductType.Goods:
        return 'Nákup';
      case ProductType.Product:
        return 'Výroba';
      default:
        return '';
    }
  };

  const getOutputTabName = (productType: ProductType) => {
    switch (productType) {
      case ProductType.Material:
        return 'Spotřeba';
      case ProductType.Product:
      case ProductType.Goods:
        return 'Prodeje';
      default:
        return '';
    }
  };

  const shouldShowChartTabs = (productType: ProductType) => {
    return productType !== ProductType.SemiProduct && productType !== ProductType.UNDEFINED;
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
                {/* Left Column - Tabbed Content */}
                <div className="flex flex-col overflow-hidden">
                  {/* Tab Navigation */}
                  <div className="flex border-b border-gray-200 mb-6">
                    <button
                      onClick={() => setActiveTab('basic')}
                      className={`px-4 py-2 text-sm font-medium flex items-center space-x-2 border-b-2 transition-colors ${
                        activeTab === 'basic'
                          ? 'border-indigo-500 text-indigo-600'
                          : 'border-transparent text-gray-500 hover:text-gray-700'
                      }`}
                    >
                      <FileText className="h-4 w-4" />
                      <span>Základní informace</span>
                    </button>
                    <button
                      onClick={() => setActiveTab('history')}
                      className={`px-4 py-2 text-sm font-medium flex items-center space-x-2 border-b-2 transition-colors ${
                        activeTab === 'history'
                          ? 'border-indigo-500 text-indigo-600'
                          : 'border-transparent text-gray-500 hover:text-gray-700'
                      }`}
                    >
                      <ShoppingCart className="h-4 w-4" />
                      <span>Historie nákupů</span>
                    </button>
                    {(item?.type === ProductType.Product || item?.type === ProductType.SemiProduct) && (
                      <button
                        onClick={() => setActiveTab('margins')}
                        className={`px-4 py-2 text-sm font-medium flex items-center space-x-2 border-b-2 transition-colors ${
                          activeTab === 'margins'
                            ? 'border-indigo-500 text-indigo-600'
                            : 'border-transparent text-gray-500 hover:text-gray-700'
                        }`}
                      >
                        <TrendingUp className="h-4 w-4" />
                        <span>Marže</span>
                      </button>
                    )}
                  </div>

                  {/* Tab Content */}
                  <div className="flex-1 overflow-y-auto">
                    {activeTab === 'basic' ? (
                      <BasicInfoTab item={item} />
                    ) : activeTab === 'history' ? (
                      <PurchaseHistoryTab 
                        purchaseHistory={detailData?.historicalData?.purchaseHistory || []} 
                        showFullHistory={showFullHistory}
                        onToggleFullHistory={() => setShowFullHistory(!showFullHistory)}
                        isLoading={detailLoading}
                      />
                    ) : (
                      <MarginsTab 
                        item={item}
                        manufactureCostHistory={detailData?.historicalData?.manufactureCostHistory || []}
                        isLoading={detailLoading}
                      />
                    )}
                  </div>
                </div>

                {/* Right Column - Charts with Tabs */}
                <div className="space-y-4">
                  <div className="h-full flex flex-col">
                    {shouldShowChartTabs(item.type || ProductType.UNDEFINED) ? (
                      <>
                        {/* Chart Tab Navigation */}
                        <div className="flex border-b border-gray-200 mb-4">
                          <button
                            onClick={() => setActiveChartTab('input')}
                            className={`px-4 py-2 text-sm font-medium flex items-center space-x-2 border-b-2 transition-colors ${
                              activeChartTab === 'input'
                                ? 'border-indigo-500 text-indigo-600'
                                : 'border-transparent text-gray-500 hover:text-gray-700'
                            }`}
                          >
                            <BarChart3 className="h-4 w-4" />
                            <span>{getInputTabName(item.type || ProductType.UNDEFINED)}</span>
                          </button>
                          <button
                            onClick={() => setActiveChartTab('output')}
                            className={`px-4 py-2 text-sm font-medium flex items-center space-x-2 border-b-2 transition-colors ${
                              activeChartTab === 'output'
                                ? 'border-indigo-500 text-indigo-600'
                                : 'border-transparent text-gray-500 hover:text-gray-700'
                            }`}
                          >
                            <BarChart3 className="h-4 w-4" />
                            <span>{getOutputTabName(item.type || ProductType.UNDEFINED)}</span>
                          </button>
                        </div>

                        {/* Summary Section */}
                        <ProductSummaryTabs
                          productType={item.type || ProductType.UNDEFINED}
                          activeTab={activeChartTab}
                          salesData={detailData?.historicalData?.salesHistory || []}
                          consumedData={detailData?.historicalData?.consumedHistory || []}
                          purchaseData={detailData?.historicalData?.purchaseHistory || []}
                          manufactureData={detailData?.historicalData?.manufactureHistory || []}
                        />

                        {/* Chart Content */}
                        <div className="flex-1 bg-gray-50 rounded-lg p-4 mb-4">
                          <ProductChartTabs
                            productType={item.type || ProductType.UNDEFINED}
                            activeTab={activeChartTab}
                            salesData={detailData?.historicalData?.salesHistory || []}
                            consumedData={detailData?.historicalData?.consumedHistory || []}
                            purchaseData={detailData?.historicalData?.purchaseHistory || []}
                            manufactureData={detailData?.historicalData?.manufactureHistory || []}
                          />
                        </div>
                      </>
                    ) : (
                      <>
                        {/* Original behavior for SemiProduct and UNDEFINED */}
                        <h3 className="text-lg font-medium text-gray-900 mb-4 flex items-center">
                          <BarChart3 className="h-5 w-5 mr-2 text-gray-500" />
                          Graf není k dispozici
                        </h3>
                        <div className="flex-1 bg-gray-50 rounded-lg p-4 mb-4 flex items-center justify-center">
                          <div className="text-center text-gray-500">
                            <BarChart3 className="h-12 w-12 mx-auto mb-2 text-gray-300" />
                            <p>Pro tento typ produktu není graf k dispozici</p>
                          </div>
                        </div>
                      </>
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


// ProductChartTabs Component - displays charts based on active tab and product type
interface ProductChartTabsProps {
  productType: ProductType;
  activeTab: 'input' | 'output';
  salesData: CatalogSalesRecordDto[];
  consumedData: CatalogConsumedRecordDto[];
  purchaseData: CatalogPurchaseRecordDto[];
  manufactureData: CatalogManufactureRecordDto[];
}

const ProductChartTabs: React.FC<ProductChartTabsProps> = ({ 
  productType, 
  activeTab, 
  salesData, 
  consumedData, 
  purchaseData,
  manufactureData 
}) => {
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
  const mapDataToMonthlyArray = (data: CatalogSalesRecordDto[] | CatalogConsumedRecordDto[] | CatalogPurchaseRecordDto[] | CatalogManufactureRecordDto[], valueKey: 'amountTotal' | 'amount') => {
    const monthlyData = new Array(13).fill(0);
    const now = new Date();
    const currentYear = now.getFullYear();
    const currentMonth = now.getMonth() + 1; // JavaScript months are 0-based, convert to 1-based
    
    // Create a map for quick lookup of data by year-month key
    const dataMap = new Map<string, number>();
    data.forEach(record => {
      if (record.year && record.month) {
        const key = `${record.year}-${record.month}`;
        const value = (record as any)[valueKey] || 0;
        dataMap.set(key, value);
      }
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
  
  // Determine which data to use based on product type and active tab
  const getChartData = () => {
    if (activeTab === 'input') {
      // Input tab - Purchase for Material/Goods, no data for Product (Manufacture not implemented yet)
      if (productType === ProductType.Material || productType === ProductType.Goods) {
        return {
          labels: monthLabels,
          data: mapDataToMonthlyArray(purchaseData, 'amount'),
          label: 'Nákup',
          backgroundColor: 'rgba(34, 197, 94, 0.2)', // Green for purchases
          borderColor: 'rgba(34, 197, 94, 1)',
          yAxisLabel: 'Kusů nakoupeno'
        };
      } else if (productType === ProductType.Product) {
        // Use actual manufacture data for Product
        return {
          labels: monthLabels,
          data: mapDataToMonthlyArray(manufactureData, 'amount'),
          label: 'Výroba',
          backgroundColor: 'rgba(168, 85, 247, 0.2)', // Purple for manufacture
          borderColor: 'rgba(168, 85, 247, 1)',
          yAxisLabel: 'Kusů vyrobeno'
        };
      }
    } else {
      // Output tab
      if (productType === ProductType.Material) {
        return {
          labels: monthLabels,
          data: mapDataToMonthlyArray(consumedData, 'amount'),
          label: 'Spotřeba',
          backgroundColor: 'rgba(251, 146, 60, 0.2)', // Orange for consumption
          borderColor: 'rgba(251, 146, 60, 1)',
          yAxisLabel: 'Množství spotřebováno'
        };
      } else if (productType === ProductType.Product || productType === ProductType.Goods) {
        return {
          labels: monthLabels,
          data: mapDataToMonthlyArray(salesData, 'amountTotal'),
          label: 'Prodeje',
          backgroundColor: 'rgba(59, 130, 246, 0.2)', // Blue for sales
          borderColor: 'rgba(59, 130, 246, 1)',
          yAxisLabel: 'Kusů prodáno'
        };
      }
    }
    
    // Default empty data
    return {
      labels: monthLabels,
      data: new Array(13).fill(0),
      label: 'Data',
      backgroundColor: 'rgba(156, 163, 175, 0.2)',
      borderColor: 'rgba(156, 163, 175, 1)',
      yAxisLabel: 'Množství'
    };
  };

  const chartConfig = getChartData();
  
  const chartData = {
    labels: chartConfig.labels,
    datasets: [
      {
        label: chartConfig.label,
        data: chartConfig.data,
        backgroundColor: chartConfig.backgroundColor,
        borderColor: chartConfig.borderColor,
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
          text: chartConfig.yAxisLabel,
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
  const hasData = chartConfig.data.some(value => value > 0);

  if (!hasData) {
    return (
      <div className="flex items-center justify-center h-96">
        <div className="text-center text-gray-500">
          <BarChart3 className="h-12 w-12 mx-auto mb-2 text-gray-300" />
          <p>Žádná data pro zobrazení grafu</p>
          <p className="text-sm">{chartConfig.label} za posledních 13 měsíců</p>
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

// ProductSummaryTabs Component - displays summary statistics based on active tab
interface ProductSummaryTabsProps {
  productType: ProductType;
  activeTab: 'input' | 'output';
  salesData: CatalogSalesRecordDto[];
  consumedData: CatalogConsumedRecordDto[];
  purchaseData: CatalogPurchaseRecordDto[];
  manufactureData: CatalogManufactureRecordDto[];
}

const ProductSummaryTabs: React.FC<ProductSummaryTabsProps> = ({ 
  productType, 
  activeTab, 
  salesData, 
  consumedData, 
  purchaseData,
  manufactureData 
}) => {
  // Calculate summary statistics based on active tab and product type
  const calculateSummary = () => {
    const currentDate = new Date();
    const oneYearAgo = new Date(currentDate.getFullYear() - 1, currentDate.getMonth(), 1);
    
    let totalForLastYear = 0;
    let monthsWithData = 0;
    let dataLabel = '';
    let unitLabel = '';
    
    if (activeTab === 'input') {
      if (productType === ProductType.Material || productType === ProductType.Goods) {
        // Purchase data
        purchaseData.forEach(record => {
          if (record.year && record.month) {
            const recordDate = new Date(record.year, record.month - 1);
            if (recordDate >= oneYearAgo) {
              totalForLastYear += record.amount || 0;
              if ((record.amount || 0) > 0) monthsWithData++;
            }
          }
        });
        dataLabel = 'nákup';
        unitLabel = 'kusů';
      } else if (productType === ProductType.Product) {
        // Manufacture data
        manufactureData.forEach(record => {
          if (record.year && record.month) {
            const recordDate = new Date(record.year, record.month - 1);
            if (recordDate >= oneYearAgo) {
              totalForLastYear += record.amount || 0;
              if ((record.amount || 0) > 0) monthsWithData++;
            }
          }
        });
        dataLabel = 'výroba';
        unitLabel = 'kusů';
      }
    } else {
      // Output tab
      if (productType === ProductType.Material) {
        // Consumption data
        consumedData.forEach(record => {
          if (record.year && record.month) {
            const recordDate = new Date(record.year, record.month - 1);
            if (recordDate >= oneYearAgo) {
              totalForLastYear += record.amount || 0;
              if ((record.amount || 0) > 0) monthsWithData++;
            }
          }
        });
        dataLabel = 'spotřeba';
        unitLabel = 'množství';
      } else if (productType === ProductType.Product || productType === ProductType.Goods) {
        // Sales data
        salesData.forEach(record => {
          if (record.year && record.month) {
            const recordDate = new Date(record.year, record.month - 1);
            if (recordDate >= oneYearAgo) {
              totalForLastYear += record.amountTotal || 0;
              if ((record.amountTotal || 0) > 0) monthsWithData++;
            }
          }
        });
        dataLabel = 'prodeje';
        unitLabel = 'kusů';
      }
    }
    
    const averageMonthly = monthsWithData > 0 ? totalForLastYear / 12 : 0;
    
    return {
      totalForLastYear: Math.round(totalForLastYear * 100) / 100,
      averageMonthly: Math.round(averageMonthly * 100) / 100,
      monthsWithData,
      dataLabel,
      unitLabel
    };
  };

  const summary = calculateSummary();

  return (
    <div className="bg-white rounded-lg border border-gray-200 p-4 shadow-sm">
      <h4 className="text-md font-medium text-gray-900 mb-3 flex items-center">
        <BarChart3 className="h-4 w-4 mr-2 text-gray-500" />
        Celkové shrnutí - {summary.dataLabel}
      </h4>
      
      <div className="grid grid-cols-2 gap-4">
        <div className="text-center p-3 bg-blue-50 rounded-lg">
          <div className="text-sm font-medium text-gray-600 mb-1">
            Celkový {summary.dataLabel} za rok
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
            Průměrný {summary.dataLabel}/měsíc
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

// BasicInfoTab Component - displays basic information, stock, price, and properties
interface BasicInfoTabProps {
  item: CatalogItemDto;
}

const BasicInfoTab: React.FC<BasicInfoTabProps> = ({ item }) => {
  return (
    <div className="space-y-6">
      {/* Basic Information */}
      <div className="space-y-4">
        <h3 className="text-lg font-medium text-gray-900 flex items-center">
          <Hash className="h-5 w-5 mr-2 text-gray-500" />
          Základní informace
        </h3>
        
        <div className="bg-gray-50 rounded-lg p-4 space-y-3">
          <div className="flex justify-between items-center">
            <span className="text-sm font-medium text-gray-600">Typ produktu:</span>
            <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${productTypeColors[item.type || ProductType.UNDEFINED]}`}>
              {productTypeLabels[item.type || ProductType.UNDEFINED]}
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
              {Math.round((item.stock?.available || 0) * 100) / 100}
            </span>
          </div>
          
          {/* Other stock info in compact grid */}
          <div className="grid grid-cols-2 gap-x-4 gap-y-1 text-sm border-t border-gray-200 pt-2">
            <div className="flex justify-between">
              <span className="text-gray-600">Shoptet:</span>
              <span className="font-medium">{Math.round((item.stock?.eshop || 0) * 100) / 100}</span>
            </div>
            
            <div className="flex justify-between">
              <span className="text-gray-600">Transport:</span>
              <span className="font-medium">{Math.round((item.stock?.transport || 0) * 100) / 100}</span>
            </div>
            
            <div className="flex justify-between">
              <span className="text-gray-600">ABRA:</span>
              <span className="font-medium">{Math.round((item.stock?.erp || 0) * 100) / 100}</span>
            </div>
            
            <div className="flex justify-between">
              <span className="text-gray-600">Rezervované:</span>
              <span className="font-medium">{Math.round((item.stock?.reserve || 0) * 100) / 100}</span>
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
          <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
            <div className="text-center">
              <span className="text-xs font-medium text-gray-600 block mb-1">
                Optimální zásoby (dny)
              </span>
              <span className="text-lg font-semibold text-gray-900">
                {item.properties?.optimalStockDaysSetup || '-'}
              </span>
            </div>
            
            <div className="text-center">
              <span className="text-xs font-medium text-gray-600 block mb-1">
                Min. zásoba
              </span>
              <span className="text-lg font-semibold text-gray-900">
                {item.properties?.stockMinSetup || '-'}
              </span>
            </div>
            
            <div className="text-center">
              <span className="text-xs font-medium text-gray-600 block mb-1">
                Velikost šarže
              </span>
              <span className="text-lg font-semibold text-gray-900">
                {item.properties?.batchSize || '-'}
              </span>
            </div>
            
            <div className="text-center">
              <span className="text-xs font-medium text-gray-600 block mb-1">
                Náročnost výroby
              </span>
              <span className="text-lg font-semibold text-gray-900">
                {item.manufactureDifficulty && item.manufactureDifficulty > 0 
                  ? item.manufactureDifficulty.toFixed(2)
                  : '-'
                }
              </span>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};

// PurchaseHistoryTab Component - displays purchase history table
interface PurchaseHistoryTabProps {
  purchaseHistory: CatalogPurchaseRecordDto[];
  showFullHistory: boolean;
  onToggleFullHistory: () => void;
  isLoading: boolean;
}

const PurchaseHistoryTab: React.FC<PurchaseHistoryTabProps> = ({ purchaseHistory, showFullHistory, onToggleFullHistory, isLoading }) => {
  // Sort history by date (most recent first)
  const sortedHistory = [...purchaseHistory].sort((a, b) => {
    const dateA = new Date(a.date || 0);
    const dateB = new Date(b.date || 0);
    return dateB.getTime() - dateA.getTime();
  });

  // Format date for display - using numeric month for better readability
  const formatDate = (dateString: string | Date | undefined) => {
    if (!dateString) return '-';
    const date = new Date(dateString);
    return date.toLocaleDateString('cs-CZ', { 
      day: '2-digit',
      month: '2-digit', 
      year: 'numeric' 
    });
  };

  if (sortedHistory.length === 0) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="text-center text-gray-500">
          <ShoppingCart className="h-12 w-12 mx-auto mb-2 text-gray-300" />
          <p className="text-lg font-medium">Žádná historie nákupů</p>
          <p className="text-sm">Pro tento produkt není k dispozici historie nákupů</p>
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h3 className="text-lg font-medium text-gray-900 flex items-center">
          <ShoppingCart className="h-5 w-5 mr-2 text-gray-500" />
          Historie nákupů ({sortedHistory.length} záznamů)
        </h3>
        <button
          onClick={onToggleFullHistory}
          disabled={isLoading}
          className={`px-3 py-1 text-sm font-medium rounded-md transition-colors ${
            showFullHistory
              ? 'bg-indigo-100 text-indigo-700 hover:bg-indigo-200'
              : 'bg-gray-100 text-gray-700 hover:bg-gray-200'
          } ${isLoading ? 'opacity-50 cursor-not-allowed' : ''}`}
        >
          {isLoading ? (
            <div className="flex items-center space-x-2">
              <div className="w-3 h-3 border border-gray-400 border-t-transparent rounded-full animate-spin"></div>
              <span>Načítám...</span>
            </div>
          ) : showFullHistory ? (
            'Zobrazit posledních 13 měsíců'
          ) : (
            'Zobrazit celou historii'
          )}
        </button>
      </div>
      
      <div className="bg-white rounded-lg border border-gray-200 overflow-hidden">
        <div className="space-y-1">
          {sortedHistory.map((record, index) => (
            <div key={index} className="border-b border-gray-100 last:border-b-0 hover:bg-gray-50 p-4">
              {/* Primary row - main information */}
              <div className="flex items-center justify-between mb-2">
                <div className="flex items-center space-x-4">
                  <div className="text-sm font-medium text-gray-900">
                    {formatDate(record.date)}
                  </div>
                  <div className="text-sm font-medium text-gray-900 max-w-xs truncate" title={record.supplierName || '-'}>
                    {record.supplierName || '-'}
                  </div>
                </div>
                <div className="text-sm font-semibold text-gray-900">
                  {record.pricePerPiece 
                    ? `${record.pricePerPiece.toLocaleString('cs-CZ', { minimumFractionDigits: 2 })} Kč/ks`
                    : '-'
                  }
                </div>
              </div>
              
              {/* Secondary row - additional details */}
              <div className="flex items-center justify-between text-xs text-gray-500">
                <div className="flex items-center space-x-4">
                  <span>Množství: <strong className="text-gray-700">{record.amount ? Math.round(record.amount * 100) / 100 : '-'}</strong></span>
                  <span>Celkem: <strong className="text-gray-700">
                    {record.priceTotal 
                      ? `${record.priceTotal.toLocaleString('cs-CZ', { minimumFractionDigits: 2 })} Kč`
                      : '-'
                    }
                  </strong></span>
                </div>
                <div className="font-mono text-gray-400">
                  {record.documentNumber || '-'}
                </div>
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* Summary */}
      <div className="bg-blue-50 rounded-lg p-4">
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
          <div className="text-center">
            <div className="text-sm font-medium text-gray-600 mb-1">Celkové nákupy</div>
            <div className="text-xl font-bold text-blue-900">
              {sortedHistory.reduce((sum, record) => sum + (record.amount || 0), 0).toLocaleString('cs-CZ')}
            </div>
            <div className="text-xs text-gray-500">kusů celkem</div>
          </div>
          
          <div className="text-center">
            <div className="text-sm font-medium text-gray-600 mb-1">Celková hodnota</div>
            <div className="text-xl font-bold text-blue-900">
              {sortedHistory.reduce((sum, record) => sum + (record.priceTotal || 0), 0).toLocaleString('cs-CZ', { minimumFractionDigits: 2 })} Kč
            </div>
            <div className="text-xs text-gray-500">celkové náklady</div>
          </div>
          
          <div className="text-center">
            <div className="text-sm font-medium text-gray-600 mb-1">Průměrná cena</div>
            <div className="text-xl font-bold text-blue-900">
              {sortedHistory.length > 0 
                ? (sortedHistory.reduce((sum, record) => sum + (record.pricePerPiece || 0), 0) / sortedHistory.length).toLocaleString('cs-CZ', { minimumFractionDigits: 2 })
                : '0'
              } Kč
            </div>
            <div className="text-xs text-gray-500">za kus</div>
          </div>
        </div>
      </div>
    </div>
  );
};

// MarginsTab Component - displays manufacturing cost history and margins
interface MarginsTabProps {
  item: CatalogItemDto | null;
  manufactureCostHistory: ManufactureCostDto[];
  isLoading: boolean;
}

const MarginsTab: React.FC<MarginsTabProps> = ({ item, manufactureCostHistory, isLoading }) => {
  // Calculate average manufacturing costs for display
  const averageMaterialCost = manufactureCostHistory.length > 0 
    ? manufactureCostHistory.reduce((sum, record) => sum + (record.materialCost || 0), 0) / manufactureCostHistory.length 
    : 0;
  
  const averageHandlingCost = manufactureCostHistory.length > 0 
    ? manufactureCostHistory.reduce((sum, record) => sum + (record.handlingCost || 0), 0) / manufactureCostHistory.length 
    : 0;
    
  const averageTotalCost = manufactureCostHistory.length > 0 
    ? manufactureCostHistory.reduce((sum, record) => sum + (record.total || 0), 0) / manufactureCostHistory.length 
    : 0;

  // Use pre-calculated margin values from backend
  const sellingPrice = item?.price?.eshopPrice?.priceWithoutVat || 0;
  const margin = item?.marginPercentage || 0;
  const marginAmount = item?.marginAmount || 0;

  // Generate last 13 months labels for chart
  const generateMonthLabels = () => {
    const months = [];
    const now = new Date();
    
    for (let i = 12; i >= 0; i--) {
      const date = new Date(now.getFullYear(), now.getMonth() - i, 1);
      months.push(date.toLocaleDateString('cs-CZ', { month: 'short', year: 'numeric' }));
    }
    
    return months;
  };

  // Map manufacturing cost data to monthly arrays
  const mapCostDataToMonthlyArrays = () => {
    const materialCostData = new Array(13).fill(0);
    const handlingCostData = new Array(13).fill(0);
    const totalCostData = new Array(13).fill(0);
    const now = new Date();
    const currentYear = now.getFullYear();
    const currentMonth = now.getMonth() + 1;
    
    // Create maps for quick lookup of data by year-month key
    const materialCostMap = new Map<string, number>();
    const handlingCostMap = new Map<string, number>();
    const totalCostMap = new Map<string, number>();
    
    manufactureCostHistory.forEach(record => {
      if (record.date) {
        const recordDate = new Date(record.date);
        const key = `${recordDate.getFullYear()}-${recordDate.getMonth() + 1}`;
        materialCostMap.set(key, record.materialCost || 0);
        handlingCostMap.set(key, record.handlingCost || 0);
        totalCostMap.set(key, record.total || 0);
      }
    });
    
    // Fill the arrays with data for the last 13 months
    for (let i = 0; i < 13; i++) {
      const monthsBack = 12 - i;
      let adjustedYear = currentYear;
      let adjustedMonth = currentMonth - monthsBack;
      
      // Handle year transitions
      if (adjustedMonth <= 0) {
        adjustedYear--;
        adjustedMonth += 12;
      }
      
      const key = `${adjustedYear}-${adjustedMonth}`;
      materialCostData[i] = materialCostMap.get(key) || 0;
      handlingCostData[i] = handlingCostMap.get(key) || 0;
      totalCostData[i] = totalCostMap.get(key) || 0;
    }
    
    return { materialCostData, handlingCostData, totalCostData };
  };

  const monthLabels = generateMonthLabels();
  const { materialCostData, handlingCostData, totalCostData } = mapCostDataToMonthlyArrays();
  
  const chartData = {
    labels: monthLabels,
    datasets: [
      {
        label: 'Materiálové náklady (Kč/ks)',
        data: materialCostData,
        backgroundColor: 'rgba(34, 197, 94, 0.2)', // Green for material costs
        borderColor: 'rgba(34, 197, 94, 1)',
        borderWidth: 2,
        tension: 0.1,
      },
      {
        label: 'Náklady na zpracování (Kč/ks)',
        data: handlingCostData,
        backgroundColor: 'rgba(59, 130, 246, 0.2)', // Blue for handling costs
        borderColor: 'rgba(59, 130, 246, 1)',
        borderWidth: 2,
        tension: 0.1,
      },
      {
        label: 'Celkové náklady (Kč/ks)',
        data: totalCostData,
        backgroundColor: 'rgba(168, 85, 247, 0.2)', // Purple for total costs
        borderColor: 'rgba(168, 85, 247, 1)',
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
          text: 'Náklady na výrobu (Kč/ks)',
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

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2">
          <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
          <div className="text-gray-500">Načítání dat o marži...</div>
        </div>
      </div>
    );
  }

  if (manufactureCostHistory.length === 0) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="text-center text-gray-500">
          <TrendingUp className="h-12 w-12 mx-auto mb-2 text-gray-300" />
          <p className="text-lg font-medium">Žádné údaje o nákladech na výrobu</p>
          <p className="text-sm">Pro tento produkt nejsou k dispozici historické náklady na výrobu</p>
        </div>
      </div>
    );
  }

  // Check if we have any non-zero data
  const hasData = totalCostData.some(value => value > 0);

  return (
    <div className="space-y-4">
      {/* Cost Summary & Margins - compact design */}
      <div className="bg-white rounded-lg border border-gray-200 p-4 shadow-sm">
        <h4 className="text-md font-medium text-gray-900 mb-3 flex items-center">
          <BarChart3 className="h-4 w-4 mr-2 text-gray-500" />
          Přehled nákladů a marže
        </h4>
        
        {/* Compact cost breakdown */}
        <div className="grid grid-cols-2 md:grid-cols-4 gap-3 mb-4">
          <div className="text-center p-2 bg-orange-50 rounded border border-orange-200">
            <div className="text-xs font-medium text-gray-600 mb-1">Materiál</div>
            <div className="text-lg font-bold text-orange-900">
              {averageMaterialCost.toLocaleString('cs-CZ', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
            </div>
            <div className="text-xs text-gray-500">Kč/ks</div>
          </div>
          
          <div className="text-center p-2 bg-blue-50 rounded border border-blue-200">
            <div className="text-xs font-medium text-gray-600 mb-1">Zpracování</div>
            <div className="text-lg font-bold text-blue-900">
              {averageHandlingCost.toLocaleString('cs-CZ', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
            </div>
            <div className="text-xs text-gray-500">Kč/ks</div>
          </div>
          
          <div className="text-center p-2 bg-red-50 rounded border border-red-200">
            <div className="text-xs font-medium text-gray-600 mb-1">Celkem náklady</div>
            <div className="text-lg font-bold text-red-900">
              {averageTotalCost.toLocaleString('cs-CZ', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
            </div>
            <div className="text-xs text-gray-500">Kč/ks</div>
          </div>
          
          <div className="text-center p-2 bg-purple-50 rounded border border-purple-200">
            <div className="text-xs font-medium text-gray-600 mb-1">Prodej (bez DPH)</div>
            <div className="text-lg font-bold text-purple-900">
              {sellingPrice.toLocaleString('cs-CZ', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
            </div>
            <div className="text-xs text-gray-500">Kč/ks</div>
          </div>
        </div>
        
        {/* Margin summary */}
        <div className="grid grid-cols-2 gap-4">
          <div className="text-center p-3 bg-green-50 rounded-lg border border-green-200">
            <div className="text-sm font-medium text-gray-600 mb-1">
              Marže v %
            </div>
            <div className={`text-2xl font-bold ${margin >= 0 ? 'text-green-900' : 'text-red-900'}`}>
              {margin.toLocaleString('cs-CZ', { minimumFractionDigits: 1, maximumFractionDigits: 1 })}%
            </div>
            <div className="text-xs text-gray-500 mt-1">
              {margin >= 0 ? 'zisk' : 'ztráta'}
            </div>
          </div>
          
          <div className="text-center p-3 bg-emerald-50 rounded-lg border border-emerald-200">
            <div className="text-sm font-medium text-gray-600 mb-1">
              Marže v Kč
            </div>
            <div className={`text-2xl font-bold ${marginAmount >= 0 ? 'text-emerald-900' : 'text-red-900'}`}>
              {marginAmount.toLocaleString('cs-CZ', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} Kč
            </div>
            <div className="text-xs text-gray-500 mt-1">
              za kus
            </div>
          </div>
        </div>
        
        {manufactureCostHistory.length === 0 && (
          <div className="mt-3 text-center text-sm text-gray-500">
            Žádná data o nákladech za posledních 13 měsíců
          </div>
        )}
        
        {sellingPrice === 0 && (
          <div className="mt-2 text-center text-xs text-amber-600 bg-amber-50 p-2 rounded">
            Není dostupná prodejní cena - marže nelze vypočítat
          </div>
        )}
      </div>

      {/* Manufacturing Cost History Chart */}
      <div className="flex-1 bg-gray-50 rounded-lg p-4 mb-4">
        {hasData ? (
          <div className="h-96">
            <Line data={chartData} options={chartOptions} />
          </div>
        ) : (
          <div className="flex items-center justify-center h-96">
            <div className="text-center text-gray-500">
              <BarChart3 className="h-12 w-12 mx-auto mb-2 text-gray-300" />
              <p>Žádná data pro zobrazení grafu</p>
              <p className="text-sm">Náklady na výrobu za posledních 13 měsíců</p>
            </div>
          </div>
        )}
      </div>

    </div>
  );
};

export default CatalogDetail;