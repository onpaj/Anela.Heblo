import React, { useState, useMemo, useCallback } from 'react';
import { Chart } from 'react-chartjs-2';
import { ChartOptions } from 'chart.js';
import { AlertTriangle, BarChart3 } from 'lucide-react';
import { useProductMarginSummaryQuery, ProductGroupingMode } from '../../api/hooks/useProductMarginSummary';

type TimeWindowType = 'current-year' | 'current-and-previous-year' | 'last-6-months' | 'last-12-months' | 'last-24-months';

// Color palette for products (blue/green/purple theme, highest margin to lowest)
const PRODUCT_COLORS = [
  "#1E40AF", // Deep blue (highest)
  "#3B82F6", // Blue
  "#60A5FA", // Light blue
  "#0891B2", // Cyan
  "#06B6D4", // Cyan-blue
  "#059669", // Emerald green
  "#10B981", // Green
  "#34D399", // Light green
  "#6366F1", // Indigo
  "#8B5CF6", // Purple
  "#A855F7", // Violet
  "#C084FC", // Light violet
  "#EC4899", // Pink
  "#F472B6", // Light pink
  "#8B5A3C"  // Brown accent (lowest in top 15)
];

const OTHER_COLOR = "#9CA3AF"; // Gray for "Other"
const DEFAULT_COLOR = "#9CA3AF"; // Gray for products not in top 15

const ProductMarginSummary: React.FC = () => {
  const [selectedTimeWindow, setSelectedTimeWindow] = useState<TimeWindowType>('current-year');
  const [selectedGroupingMode, setSelectedGroupingMode] = useState<ProductGroupingMode>(ProductGroupingMode.Products);
  
  const { data, isLoading, error } = useProductMarginSummaryQuery(selectedTimeWindow, selectedGroupingMode);

  const formatCurrency = useCallback((amount: number) => {
    return new Intl.NumberFormat('cs-CZ', {
      style: 'currency',
      currency: 'CZK',
      minimumFractionDigits: 0,
      maximumFractionDigits: 0,
    }).format(amount);
  }, []);

  const chartData = useMemo(() => {
    if (!data?.monthlyData || !data?.topProducts) return null;
    
    const labels = data.monthlyData.map(m => m.monthDisplay);
    
    // Create a map of products to consistent colors based on total margin ranking
    const sortedByTotalMargin = [...data.topProducts].sort((a, b) => (b.totalMargin || 0) - (a.totalMargin || 0));
    const productColorMap = new Map<string, string>();
    
    // For chart, we want to show only top 15 products and group the rest as "Other"
    const TOP_CHART_PRODUCTS = 15;
    const topProductsForChart = sortedByTotalMargin.slice(0, TOP_CHART_PRODUCTS);
    const otherProductsForChart = sortedByTotalMargin.slice(TOP_CHART_PRODUCTS);
    
    // Assign colors to top products
    topProductsForChart.forEach((product, index) => {
      if (product.groupKey) {
        productColorMap.set(product.groupKey, PRODUCT_COLORS[index % PRODUCT_COLORS.length]);
      }
    });
    
    // Collect product display names
    const productDisplayNames = new Map<string, string>();
    data.monthlyData.forEach(month => {
      month.productSegments?.forEach(segment => {
        if (segment.groupKey && segment.displayName) {
          productDisplayNames.set(segment.groupKey, segment.displayName);
        }
      });
    });
    
    // Create datasets for chart
    const datasets: any[] = [];
    
    // Add "Other" category first (will be at bottom of stack)
    if (otherProductsForChart.length > 0) {
      const otherKeys = new Set(otherProductsForChart.map(p => p.groupKey).filter(Boolean));
      
      datasets.push({
        label: 'Ostatní produkty',
        data: data.monthlyData!.map(month => {
          // Sum up margin contributions for all "other" products in this month
          const otherMargin = month.productSegments?.reduce((sum, segment) => {
            if (segment.groupKey && otherKeys.has(segment.groupKey)) {
              return sum + (segment.marginContribution || 0);
            }
            return sum;
          }, 0) || 0;
          return otherMargin;
        }),
        backgroundColor: OTHER_COLOR,
        borderColor: OTHER_COLOR,
        borderWidth: 1,
      });
    }
    
    // Sort top products by total margin for consistent dataset order
    // Highest margin products should be added last to appear at the top of the stack
    const topProductKeys = topProductsForChart.map(p => p.groupKey).filter(Boolean);
    topProductKeys.sort((a, b) => {
      const aTotal = sortedByTotalMargin.find(p => p.groupKey === a)?.totalMargin || 0;
      const bTotal = sortedByTotalMargin.find(p => p.groupKey === b)?.totalMargin || 0;
      return aTotal - bTotal; // Lower total margin first, will be at bottom of stack
    });
    
    // Add top products
    topProductKeys.forEach(productKey => {
      if (!productKey) return;
      const color = productColorMap.get(productKey) || DEFAULT_COLOR;
      const displayName = productDisplayNames.get(productKey) || productKey;
      
      datasets.push({
        label: displayName,
        data: data.monthlyData!.map(month => {
          const segment = month.productSegments?.find(s => s.groupKey === productKey);
          return segment?.marginContribution || 0;
        }),
        backgroundColor: color,
        borderColor: color,
        borderWidth: 1,
      });
    });
    
    return { labels, datasets };
  }, [data]);

  // Prepare table data with aggregated statistics across all months for ALL products
  const tableData = useMemo(() => {
    if (!data?.monthlyData) return [];
    
    // Collect all unique products/groups from all months (excluding "Other")
    const allProducts = new Map<string, { displayName: string; colorCode: string }>();
    
    data.monthlyData.forEach(month => {
      month.productSegments?.forEach(segment => {
        if (segment.groupKey && segment.displayName && !allProducts.has(segment.groupKey)) {
          // Find if this product is in top products to get proper color
          // First sort products by total margin to get consistent ordering
          const sortedTopProducts = data.topProducts ? 
            [...data.topProducts].sort((a, b) => (b.totalMargin || 0) - (a.totalMargin || 0)) : [];
          const topProductIndex = sortedTopProducts.findIndex(tp => tp.groupKey === segment.groupKey);
          const color = topProductIndex >= 0 ? 
            PRODUCT_COLORS[topProductIndex % PRODUCT_COLORS.length] : 
            DEFAULT_COLOR;
            
          allProducts.set(segment.groupKey, {
            displayName: segment.displayName,
            colorCode: color
          });
        }
      });
    });
    
    return Array.from(allProducts.entries()).map(([groupKey, product]) => {
      // Aggregate data across all months for this product
      let totalMargin = 0;
      let totalUnits = 0;
      let totalRevenue = 0;
      let avgMarginPerPiece = 0;
      let avgSellingPrice = 0;
      let avgMaterialCosts = 0;
      let avgLaborCosts = 0;
      let monthsWithData = 0;

      data.monthlyData!.forEach(month => {
        const segment = month.productSegments?.find(s => s.groupKey === groupKey);
        if (segment) {
          totalMargin += segment.marginContribution || 0;
          totalUnits += segment.unitsSold || 0;
          totalRevenue += (segment.unitsSold || 0) * (segment.averageSellingPriceWithoutVat || 0);
          
          // Accumulate averages (will divide by monthsWithData)
          avgMarginPerPiece += segment.averageMarginPerPiece || 0;
          avgSellingPrice += segment.averageSellingPriceWithoutVat || 0;
          avgMaterialCosts += segment.averageMaterialCosts || 0;
          avgLaborCosts += segment.averageLaborCosts || 0;
          monthsWithData++;
        }
      });

      // Calculate averages
      const finalAvgMarginPerPiece = monthsWithData > 0 ? avgMarginPerPiece / monthsWithData : 0;
      const finalAvgSellingPrice = monthsWithData > 0 ? avgSellingPrice / monthsWithData : 0;
      const finalAvgMaterialCosts = monthsWithData > 0 ? avgMaterialCosts / monthsWithData : 0;
      const finalAvgLaborCosts = monthsWithData > 0 ? avgLaborCosts / monthsWithData : 0;
      
      // Calculate margin percentage
      const marginPercentage = totalRevenue > 0 ? (totalMargin / totalRevenue) * 100 : 0;

      return {
        groupKey,
        displayName: product.displayName,
        colorCode: product.colorCode,
        totalMargin,
        marginPercentage,
        totalUnits,
        totalRevenue,
        avgMarginPerPiece: finalAvgMarginPerPiece,
        avgSellingPrice: finalAvgSellingPrice,
        avgMaterialCosts: finalAvgMaterialCosts,
        avgLaborCosts: finalAvgLaborCosts,
      };
    }).sort((a, b) => b.totalMargin - a.totalMargin); // Sort by total margin desc
  }, [data]);
  
  const chartOptions: ChartOptions<'bar'> = useMemo(() => ({
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
      legend: {
        position: window.innerWidth < 768 ? 'top' : 'right',
      },
      tooltip: {
        callbacks: {
          label: (context) => {
            const productName = context.dataset.label;
            const margin = formatCurrency(context.parsed.y);
            const monthData = data?.monthlyData?.[context.dataIndex];
            let tooltip = `${productName}: ${margin}`;
            
            // For "Other" products, we need to calculate aggregated tooltip data
            if (productName === 'Ostatní produkty') {
              const monthData = data?.monthlyData?.[context.dataIndex];
              const totalMargin = context.parsed.y;
              const totalMonthMargin = monthData?.totalMonthMargin || 0;
              const percentage = totalMonthMargin > 0 ? (totalMargin / totalMonthMargin) * 100 : 0;
              tooltip = `${productName}: ${margin} (${percentage.toFixed(1)}%)`;
            } else {
              // For individual products
              const segment = monthData?.productSegments?.find(s => s.displayName === productName);
              const percentage = segment?.percentage || 0;
              
              // Enhanced tooltip with detailed margin information
              tooltip = `${productName}: ${margin} (${percentage.toFixed(1)}%)`;
              if (segment) {
                tooltip += `\nPrůměrná marže za kus: ${formatCurrency(segment.averageMarginPerPiece || 0)}`;
                tooltip += `\nPrůměrná cena za kus: ${formatCurrency(segment.averageSellingPriceWithoutVat || 0)}`;
                tooltip += `\nProdano kusů: ${segment.unitsSold || 0}`;
                tooltip += `\nPočet produktů: ${segment.productCount || 0}`;
                tooltip += `\nPrůměrné náklady materiál: ${formatCurrency(segment.averageMaterialCosts || 0)}`;
                tooltip += `\nPrůměrné náklady práce: ${formatCurrency(segment.averageLaborCosts || 0)}`;
              }
            }
            return tooltip.split('\n');
          }
        }
      }
    },
    scales: {
      x: {
        stacked: true,
      },
      y: {
        stacked: true,
        ticks: {
          callback: (value) => formatCurrency(Number(value))
        },
        title: {
          display: true,
          text: 'Marže (Kč)'
        }
      }
    }
  }), [data, formatCurrency]);

  if (isLoading) {
    return (
      <div className="flex flex-col h-full w-full">
        <div className="flex-shrink-0 mb-3">
          <h1 className="text-lg font-semibold text-gray-900">Přehled marží produktů</h1>
          <p className="mt-1 text-sm text-gray-600">
            Analýza celkové marže z prodeje produktů v čase s rozložením podle jednotlivých produktů
          </p>
        </div>
        <div className="flex-1 bg-white shadow rounded-lg overflow-hidden flex items-center justify-center">
          <div className="flex items-center justify-center py-12">
            <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600"></div>
            <span className="ml-2 text-gray-600">Načítám data o marži produktů...</span>
          </div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex flex-col h-full w-full">
        <div className="flex-shrink-0 mb-3">
          <h1 className="text-lg font-semibold text-gray-900">Přehled marží produktů</h1>
          <p className="mt-1 text-sm text-gray-600">
            Analýza celkové marže z prodeje produktů v čase s rozložením podle jednotlivých produktů
          </p>
        </div>
        <div className="mb-8 p-4 bg-red-50 border border-red-200 rounded-lg">
          <div className="flex items-center">
            <AlertTriangle className="w-5 h-5 text-red-500 mr-2" />
            <h3 className="text-red-800 font-medium">Chyba při načítání dat o marži</h3>
          </div>
          <p className="mt-1 text-red-700 text-sm">{error.message}</p>
        </div>
      </div>
    );
  }

  if (!data?.monthlyData?.length) {
    return (
      <div className="flex flex-col h-full w-full">
        <div className="flex-shrink-0 mb-3">
          <h1 className="text-lg font-semibold text-gray-900">Přehled marží produktů</h1>
          <p className="mt-1 text-sm text-gray-600">
            Analýza celkové marže z prodeje produktů v čase s rozložením podle jednotlivých produktů
          </p>
        </div>
        <div className="flex-1 bg-white shadow rounded-lg overflow-hidden flex items-center justify-center">
          <div className="text-center py-12">
            <BarChart3 className="mx-auto h-12 w-12 text-gray-400" />
            <h3 className="mt-2 text-sm font-medium text-gray-900">Žádná data o marži</h3>
            <p className="mt-1 text-sm text-gray-500">
              Pro vybrané období nejsou k dispozici žádná data o marži produktů.
            </p>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col h-full w-full">
      {/* Header */}
      <div className="flex-shrink-0 mb-3">
        <h1 className="text-lg font-semibold text-gray-900">Analýza marže</h1>
        <p className="mt-1 text-sm text-gray-600">
          Analýza celkové marže z prodeje v čase s rozložením podle produktů nebo kategorií
        </p>
      </div>
      
      {/* Controls */}
      <div className="flex-shrink-0 bg-white shadow rounded-lg p-4 mb-4">
        <div className="flex flex-col space-y-4 lg:flex-row lg:space-y-0 lg:space-x-8">
          {/* Grouping Mode Selector */}
          <div className="flex items-center space-x-4">
            <label htmlFor="grouping-mode" className="text-sm font-medium text-gray-700">
              Seskupení:
            </label>
            <select
              id="grouping-mode"
              value={selectedGroupingMode}
              onChange={(e) => setSelectedGroupingMode(e.target.value as ProductGroupingMode)}
              className="block w-48 pl-3 pr-10 py-2 text-base border-gray-300 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm rounded-md"
            >
              <option value={ProductGroupingMode.Products}>Jednotlivé produkty</option>
              <option value={ProductGroupingMode.ProductFamily}>Rodiny produktů</option>
              <option value={ProductGroupingMode.ProductCategory}>Kategorie produktů</option>
            </select>
          </div>
          
          {/* Time Window Selector */}
          <div className="flex items-center space-x-4">
            <label htmlFor="time-window" className="text-sm font-medium text-gray-700">
              Časové období:
            </label>
            <select
              id="time-window"
              value={selectedTimeWindow}
              onChange={(e) => setSelectedTimeWindow(e.target.value as TimeWindowType)}
              className="block w-60 pl-3 pr-10 py-2 text-base border-gray-300 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm rounded-md"
            >
              <option value="current-year">Aktuální rok</option>
              <option value="current-and-previous-year">Aktuální + předchozí rok</option>
              <option value="last-6-months">Posledních 6 měsíců</option>
              <option value="last-12-months">Posledních 12 měsíců</option>
              <option value="last-24-months">Posledních 24 měsíců</option>
            </select>
          </div>
        </div>
        
        {/* Summary Information */}
        {data && (
          <div className="mt-4 flex flex-col space-y-2 lg:flex-row lg:space-y-0 lg:items-center lg:space-x-8 text-sm text-gray-600">
            <span>
              <strong>Celková marže:</strong> {formatCurrency(data.totalMargin || 0)}
            </span>
            <span>
              <strong>Období:</strong> {data.fromDate ? new Date(data.fromDate).toLocaleDateString('cs-CZ') : ''} - {data.toDate ? new Date(data.toDate).toLocaleDateString('cs-CZ') : ''}
            </span>
            <span>
              <strong>Celkem skupin:</strong> {data.topProducts?.length || 0} (graf: top 15 + ostatní, tabulka: všechny)
            </span>
          </div>
        )}
      </div>
      
      {/* Chart */}
      <div className="flex-shrink-0 bg-white shadow rounded-lg overflow-hidden" style={{ height: '500px' }}>
        {chartData && (
          <div className="h-full p-6">
            <div className="h-full">
              <Chart type="bar" data={chartData} options={chartOptions} />
            </div>
          </div>
        )}
      </div>

      {/* Detailed Products Table */}
      {tableData.length > 0 && (
        <div className="mt-6 bg-white shadow rounded-lg overflow-hidden flex flex-col flex-1 min-h-0">
          <div className="flex-shrink-0 px-6 py-4 border-b border-gray-200">
            <h3 className="text-lg font-medium text-gray-900">Detailní přehled produktů</h3>
            <p className="mt-1 text-sm text-gray-600">
              Celkové průměry a statistiky za vybrané období
            </p>
          </div>
          <div className="flex-1 overflow-auto">
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50 sticky top-0 z-10">
                <tr>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Produkt/Skupina
                  </th>
                  <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Celková marže
                  </th>
                  <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Marže %
                  </th>
                  <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Prodáno kusů
                  </th>
                  <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Prům. marže/kus
                  </th>
                  <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Prům. cena/kus
                  </th>
                  <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Prům. materiál
                  </th>
                  <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Prům. práce
                  </th>
                </tr>
              </thead>
              <tbody className="bg-white divide-y divide-gray-200">
                {tableData.map((row, index) => (
                  <tr key={row.groupKey} className={index % 2 === 0 ? 'bg-white' : 'bg-gray-50'}>
                    <td className="px-6 py-4 whitespace-nowrap">
                      <div className="flex items-center">
                        <div 
                          className="h-4 w-4 rounded-full mr-3 flex-shrink-0"
                          style={{ backgroundColor: row.colorCode }}
                        ></div>
                        <div className="text-sm font-medium text-gray-900">
                          {row.displayName}
                        </div>
                      </div>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-right text-sm text-gray-900 font-medium">
                      {formatCurrency(row.totalMargin)}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-right text-sm text-gray-900">
                      {row.marginPercentage.toFixed(1)}%
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-right text-sm text-gray-900">
                      {row.totalUnits.toLocaleString('cs-CZ')}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-right text-sm text-gray-900">
                      {formatCurrency(row.avgMarginPerPiece)}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-right text-sm text-gray-900">
                      {formatCurrency(row.avgSellingPrice)}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-right text-sm text-gray-900">
                      {formatCurrency(row.avgMaterialCosts)}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-right text-sm text-gray-900">
                      {formatCurrency(row.avgLaborCosts)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
};

export default ProductMarginSummary;