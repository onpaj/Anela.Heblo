import React from 'react';
import { BarChart3 } from 'lucide-react';
import { Line } from 'react-chartjs-2';
import { ProductType, CatalogSalesRecordDto, CatalogConsumedRecordDto, CatalogPurchaseRecordDto, CatalogManufactureRecordDto } from '../../../../api/hooks/useCatalog';
import { JournalEntryDto } from '../../../../api/generated/api-client';
import { generateMonthLabels, mapDataToMonthlyArray, generatePointStyling, generateTooltipCallback } from './ChartHelpers';

interface ProductChartProps {
  productType: ProductType;
  activeTab: 'input' | 'output';
  salesData: CatalogSalesRecordDto[];
  consumedData: CatalogConsumedRecordDto[];
  purchaseData: CatalogPurchaseRecordDto[];
  manufactureData: CatalogManufactureRecordDto[];
  journalEntries: JournalEntryDto[];
}

const ProductChart: React.FC<ProductChartProps> = ({ 
  productType, 
  activeTab, 
  salesData, 
  consumedData, 
  purchaseData,
  manufactureData,
  journalEntries 
}) => {
  const monthLabels = generateMonthLabels();
  
  // Determine which data to use based on product type and active tab
  const getChartData = () => {
    if (activeTab === 'input') {
      // Input tab - Purchase for Material/Goods, Manufacture for Product/SemiProduct
      if (productType === ProductType.Material || productType === ProductType.Goods) {
        return {
          labels: monthLabels,
          data: mapDataToMonthlyArray(purchaseData, 'amount'),
          label: 'Nákup',
          backgroundColor: 'rgba(34, 197, 94, 0.2)', // Green for purchases
          borderColor: 'rgba(34, 197, 94, 1)',
          yAxisLabel: 'Kusů nakoupeno'
        };
      } else if (productType === ProductType.Product || productType === ProductType.SemiProduct) {
        // Use actual manufacture data for Product and SemiProduct
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
      if (productType === ProductType.Material || productType === ProductType.SemiProduct) {
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
  
  // Generate point styling based on journal entries
  const pointStyling = generatePointStyling(13, journalEntries, chartConfig.borderColor);
  
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
        pointBackgroundColor: pointStyling.pointBackgroundColors,
        pointBorderColor: pointStyling.pointBackgroundColors,
        pointRadius: pointStyling.pointRadiuses,
        pointHoverRadius: pointStyling.pointHoverRadiuses,
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
      tooltip: {
        mode: 'index' as const,
        intersect: false,
        callbacks: generateTooltipCallback(journalEntries)
      }
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

export default ProductChart;