import React from 'react';
import { BarChart3 } from 'lucide-react';
import { Line } from 'react-chartjs-2';
import { ManufactureCostDto, MarginHistoryDto, JournalEntryDto } from '../../../../../api/generated/api-client';
import { generateMonthLabels, generatePointStyling, generateTooltipCallback } from '../../charts/ChartHelpers';

interface MarginsChartProps {
  manufactureCostHistory: ManufactureCostDto[];
  marginHistory: MarginHistoryDto[];
  journalEntries: JournalEntryDto[];
}

const MarginsChart: React.FC<MarginsChartProps> = ({ 
  manufactureCostHistory, 
  marginHistory, 
  journalEntries 
}) => {
  const monthLabels = generateMonthLabels();

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

  // Map margin history data to monthly arrays  
  const mapMarginDataToMonthlyArrays = () => {
    const marginAmountData = new Array(13).fill(0);
    const now = new Date();
    const currentYear = now.getFullYear();
    const currentMonth = now.getMonth() + 1;
    
    // Create map for quick lookup of margin data by year-month key
    const marginMap = new Map<string, number>();
    
    marginHistory.forEach(record => {
      if (record.date) {
        const recordDate = new Date(record.date);
        const key = `${recordDate.getFullYear()}-${recordDate.getMonth() + 1}`;
        marginMap.set(key, record.marginAmount || 0);
      }
    });
    
    // Fill the array with data for the last 13 months
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
      marginAmountData[i] = marginMap.get(key) || 0;
    }
    
    return marginAmountData;
  };

  const { materialCostData, handlingCostData, totalCostData } = mapCostDataToMonthlyArrays();
  const marginData = mapMarginDataToMonthlyArrays();
  
  // Generate point styling for each dataset
  const materialStyling = generatePointStyling(13, journalEntries, 'rgba(34, 197, 94, 1)');
  const handlingStyling = generatePointStyling(13, journalEntries, 'rgba(59, 130, 246, 1)');
  const totalStyling = generatePointStyling(13, journalEntries, 'rgba(168, 85, 247, 1)');
  const marginStyling = generatePointStyling(13, journalEntries, 'rgba(245, 158, 11, 1)');

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
        pointBackgroundColor: materialStyling.pointBackgroundColors,
        pointBorderColor: materialStyling.pointBackgroundColors,
        pointRadius: materialStyling.pointRadiuses,
        pointHoverRadius: materialStyling.pointHoverRadiuses,
        yAxisID: 'y',
      },
      {
        label: 'Náklady na zpracování (Kč/ks)',
        data: handlingCostData,
        backgroundColor: 'rgba(59, 130, 246, 0.2)', // Blue for handling costs
        borderColor: 'rgba(59, 130, 246, 1)',
        borderWidth: 2,
        tension: 0.1,
        pointBackgroundColor: handlingStyling.pointBackgroundColors,
        pointBorderColor: handlingStyling.pointBackgroundColors,
        pointRadius: handlingStyling.pointRadiuses,
        pointHoverRadius: handlingStyling.pointHoverRadiuses,
        yAxisID: 'y',
      },
      {
        label: 'Celkové náklady (Kč/ks)',
        data: totalCostData,
        backgroundColor: 'rgba(168, 85, 247, 0.2)', // Purple for total costs
        borderColor: 'rgba(168, 85, 247, 1)',
        borderWidth: 2,
        tension: 0.1,
        pointBackgroundColor: totalStyling.pointBackgroundColors,
        pointBorderColor: totalStyling.pointBackgroundColors,
        pointRadius: totalStyling.pointRadiuses,
        pointHoverRadius: totalStyling.pointHoverRadiuses,
        yAxisID: 'y',
      },
      {
        label: 'Absolutní marže (Kč/ks)',
        data: marginData,
        backgroundColor: 'rgba(245, 158, 11, 0.2)', // Amber for margin
        borderColor: 'rgba(245, 158, 11, 1)',
        borderWidth: 2,
        tension: 0.1,
        pointBackgroundColor: marginStyling.pointBackgroundColors,
        pointBorderColor: marginStyling.pointBackgroundColors,
        pointRadius: marginStyling.pointRadiuses,
        pointHoverRadius: marginStyling.pointHoverRadiuses,
        yAxisID: 'y1',
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
        type: 'linear' as const,
        display: true,
        position: 'left' as const,
        beginAtZero: true,
        title: {
          display: true,
          text: 'Náklady na výrobu (Kč/ks)',
        },
      },
      y1: {
        type: 'linear' as const,
        display: true,
        position: 'right' as const,
        title: {
          display: true,
          text: 'Absolutní marže (Kč/ks)',
        },
        grid: {
          drawOnChartArea: false,
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

  // Check if we have any non-zero data
  const hasData = totalCostData.some(value => value > 0);

  return (
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
  );
};

export default MarginsChart;