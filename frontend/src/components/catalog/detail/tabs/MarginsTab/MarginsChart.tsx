import React, { useState } from "react";
import { BarChart3, ToggleLeft, ToggleRight } from "lucide-react";
import { Line } from "react-chartjs-2";
import {
  ManufactureCostDto,
  MarginHistoryDto,
  JournalEntryDto,
} from "../../../../../api/generated/api-client";
import {
  generateMonthLabels,
  generatePointStyling,
  generateTooltipCallback,
} from "../../charts/ChartHelpers";

interface MarginsChartProps {
  manufactureCostHistory: ManufactureCostDto[];
  marginHistory: MarginHistoryDto[];
  journalEntries: JournalEntryDto[];
}

const MarginsChart: React.FC<MarginsChartProps> = ({
  manufactureCostHistory,
  marginHistory,
  journalEntries,
}) => {
  const [showPercentage, setShowPercentage] = useState(true);
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

    manufactureCostHistory.forEach((record) => {
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
    const m0PercentageData = new Array(13).fill(0);
    const m1PercentageData = new Array(13).fill(0);
    const m2PercentageData = new Array(13).fill(0);
    const m3PercentageData = new Array(13).fill(0);
    const m0AmountData = new Array(13).fill(0);
    const m1AmountData = new Array(13).fill(0);
    const m2AmountData = new Array(13).fill(0);
    const m3AmountData = new Array(13).fill(0);
    const now = new Date();
    const currentYear = now.getFullYear();
    const currentMonth = now.getMonth() + 1;

    // Create maps for quick lookup of margin data by year-month key
    const marginMap = new Map<string, number>();
    const m0Map = new Map<string, number>();
    const m1Map = new Map<string, number>();
    const m2Map = new Map<string, number>();
    const m3Map = new Map<string, number>();
    const m0AmountMap = new Map<string, number>();
    const m1AmountMap = new Map<string, number>();
    const m2AmountMap = new Map<string, number>();
    const m3AmountMap = new Map<string, number>();

    marginHistory.forEach((record) => {
      if (record.date) {
        const recordDate = new Date(record.date);
        const key = `${recordDate.getFullYear()}-${recordDate.getMonth() + 1}`;
        marginMap.set(key, record.m3Amount || 0);
        
        // M0-M3 percentage properties
        m0Map.set(key, record.m0Percentage || 0);
        m1Map.set(key, record.m1Percentage || 0);
        m2Map.set(key, record.m2Percentage || 0);
        m3Map.set(key, record.m3Percentage || 0);
        
        // M0-M3 amount properties
        m0AmountMap.set(key, record.m0Amount || 0);
        m1AmountMap.set(key, record.m1Amount || 0);
        m2AmountMap.set(key, record.m2Amount || 0);
        m3AmountMap.set(key, record.m3Amount || 0);
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
      marginAmountData[i] = marginMap.get(key) || 0;
      m0PercentageData[i] = m0Map.get(key) || 0;
      m1PercentageData[i] = m1Map.get(key) || 0;
      m2PercentageData[i] = m2Map.get(key) || 0;
      m3PercentageData[i] = m3Map.get(key) || 0;
      m0AmountData[i] = m0AmountMap.get(key) || 0;
      m1AmountData[i] = m1AmountMap.get(key) || 0;
      m2AmountData[i] = m2AmountMap.get(key) || 0;
      m3AmountData[i] = m3AmountMap.get(key) || 0;
    }

    return { 
      marginAmountData, 
      m0PercentageData, 
      m1PercentageData, 
      m2PercentageData, 
      m3PercentageData,
      m0AmountData,
      m1AmountData,
      m2AmountData,
      m3AmountData
    };
  };

  const { materialCostData, handlingCostData, totalCostData } =
    mapCostDataToMonthlyArrays();
  const { 
    marginAmountData, 
    m0PercentageData, 
    m1PercentageData, 
    m2PercentageData, 
    m3PercentageData,
    m0AmountData,
    m1AmountData,
    m2AmountData,
    m3AmountData
  } = mapMarginDataToMonthlyArrays();

  // Check if we have M0-M3 data
  const hasM0M3Data = m0PercentageData.some(value => value > 0) || 
                      m1PercentageData.some(value => value > 0) || 
                      m2PercentageData.some(value => value > 0) || 
                      m3PercentageData.some(value => value > 0);

  // Generate point styling for each dataset
  const materialStyling = generatePointStyling(
    13,
    journalEntries,
    "rgba(34, 197, 94, 1)",
  );
  const handlingStyling = generatePointStyling(
    13,
    journalEntries,
    "rgba(59, 130, 246, 1)",
  );
  const totalStyling = generatePointStyling(
    13,
    journalEntries,
    "rgba(168, 85, 247, 1)",
  );
  const marginStyling = generatePointStyling(
    13,
    journalEntries,
    "rgba(245, 158, 11, 1)",
  );
  
  // M0-M3 styling - Reversed spectrum
  const m0Styling = generatePointStyling(13, journalEntries, "rgba(34, 197, 94, 1)"); // Green
  const m1Styling = generatePointStyling(13, journalEntries, "rgba(234, 179, 8, 1)"); // Yellow  
  const m2Styling = generatePointStyling(13, journalEntries, "rgba(249, 115, 22, 1)"); // Orange
  const m3Styling = generatePointStyling(13, journalEntries, "rgba(239, 68, 68, 1)"); // Red

  // Check if we have cost data
  const hasCostData = totalCostData.some((value) => value > 0);
  
  // Build datasets conditionally based on available data
  const costDatasets = hasCostData ? [
    {
      label: "Materiálové náklady (Kč/ks)",
      data: materialCostData,
      backgroundColor: "rgba(34, 197, 94, 0.2)", // Green for material costs
      borderColor: "rgba(34, 197, 94, 1)",
      borderWidth: 2,
      tension: 0.1,
      pointBackgroundColor: materialStyling.pointBackgroundColors,
      pointBorderColor: materialStyling.pointBackgroundColors,
      pointRadius: materialStyling.pointRadiuses,
      pointHoverRadius: materialStyling.pointHoverRadiuses,
      yAxisID: "y",
    },
    {
      label: "Náklady na zpracování (Kč/ks)",
      data: handlingCostData,
      backgroundColor: "rgba(59, 130, 246, 0.2)", // Blue for handling costs
      borderColor: "rgba(59, 130, 246, 1)",
      borderWidth: 2,
      tension: 0.1,
      pointBackgroundColor: handlingStyling.pointBackgroundColors,
      pointBorderColor: handlingStyling.pointBackgroundColors,
      pointRadius: handlingStyling.pointRadiuses,
      pointHoverRadius: handlingStyling.pointHoverRadiuses,
      yAxisID: "y",
    },
    {
      label: "Celkové náklady (Kč/ks)",
      data: totalCostData,
      backgroundColor: "rgba(168, 85, 247, 0.2)", // Purple for total costs
      borderColor: "rgba(168, 85, 247, 1)",
      borderWidth: 2,
      tension: 0.1,
      pointBackgroundColor: totalStyling.pointBackgroundColors,
      pointBorderColor: totalStyling.pointBackgroundColors,
      pointRadius: totalStyling.pointRadiuses,
      pointHoverRadius: totalStyling.pointHoverRadiuses,
      yAxisID: "y",
    },
  ] : [];

  // Add margin datasets conditionally
  const marginDatasets = hasM0M3Data ? [
    {
      label: showPercentage ? "M0 - Marže materiál (%)" : "M0 - Marže materiál (Kč/ks)",
      data: showPercentage ? m0PercentageData : m0AmountData,
      backgroundColor: "rgba(34, 197, 94, 0.2)", // Green
      borderColor: "rgba(34, 197, 94, 1)",
      borderWidth: 2,
      tension: 0.1,
      pointBackgroundColor: m0Styling.pointBackgroundColors,
      pointBorderColor: m0Styling.pointBackgroundColors,
      pointRadius: m0Styling.pointRadiuses,
      pointHoverRadius: m0Styling.pointHoverRadiuses,
      yAxisID: "y1",
    },
    {
      label: showPercentage ? "M1 - Marže + výroba (%)" : "M1 - Marže + výroba (Kč/ks)",
      data: showPercentage ? m1PercentageData : m1AmountData,
      backgroundColor: "rgba(234, 179, 8, 0.2)", // Yellow
      borderColor: "rgba(234, 179, 8, 1)",
      borderWidth: 2,
      tension: 0.1,
      pointBackgroundColor: m1Styling.pointBackgroundColors,
      pointBorderColor: m1Styling.pointBackgroundColors,
      pointRadius: m1Styling.pointRadiuses,
      pointHoverRadius: m1Styling.pointHoverRadiuses,
      yAxisID: "y1",
    },
    {
      label: showPercentage ? "M2 - Marže + prodej (%)" : "M2 - Marže + prodej (Kč/ks)",
      data: showPercentage ? m2PercentageData : m2AmountData,
      backgroundColor: "rgba(249, 115, 22, 0.2)", // Orange
      borderColor: "rgba(249, 115, 22, 1)",
      borderWidth: 2,
      tension: 0.1,
      pointBackgroundColor: m2Styling.pointBackgroundColors,
      pointBorderColor: m2Styling.pointBackgroundColors,
      pointRadius: m2Styling.pointRadiuses,
      pointHoverRadius: m2Styling.pointHoverRadiuses,
      yAxisID: "y1",
    },
    {
      label: showPercentage ? "M3 - Finální marže (%)" : "M3 - Finální marže (Kč/ks)",
      data: showPercentage ? m3PercentageData : m3AmountData,
      backgroundColor: "rgba(239, 68, 68, 0.2)", // Red
      borderColor: "rgba(239, 68, 68, 1)",
      borderWidth: 2,
      tension: 0.1,
      pointBackgroundColor: m3Styling.pointBackgroundColors,
      pointBorderColor: m3Styling.pointBackgroundColors,
      pointRadius: m3Styling.pointRadiuses,
      pointHoverRadius: m3Styling.pointHoverRadiuses,
      yAxisID: "y1",
    },
  ] : [
    {
      label: "Absolutní marže (Kč/ks)",
      data: marginAmountData,
      backgroundColor: "rgba(245, 158, 11, 0.2)", // Amber for margin
      borderColor: "rgba(245, 158, 11, 1)",
      borderWidth: 2,
      tension: 0.1,
      pointBackgroundColor: marginStyling.pointBackgroundColors,
      pointBorderColor: marginStyling.pointBackgroundColors,
      pointRadius: marginStyling.pointRadiuses,
      pointHoverRadius: marginStyling.pointHoverRadiuses,
      yAxisID: "y1",
    },
  ];

  const chartData = {
    labels: monthLabels,
    datasets: [...costDatasets, ...marginDatasets],
  };

  const chartOptions = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
      legend: {
        position: "top" as const,
      },
      title: {
        display: false,
      },
      tooltip: {
        mode: "index" as const,
        intersect: false,
        callbacks: generateTooltipCallback(journalEntries),
      },
    },
    scales: {
      ...(hasCostData ? {
        y: {
          type: "linear" as const,
          display: true,
          position: "left" as const,
          beginAtZero: true,
          title: {
            display: true,
            text: "Náklady na výrobu (Kč/ks)",
          },
        },
      } : {}),
      y1: {
        type: "linear" as const,
        display: true,
        position: hasCostData ? ("right" as const) : ("left" as const),
        title: {
          display: true,
          text: hasM0M3Data 
            ? (showPercentage ? "Marže (%)" : "Marže (Kč/ks)")
            : "Absolutní marže (Kč/ks)",
        },
        grid: {
          drawOnChartArea: hasCostData ? false : true,
        },
      },
      x: {
        title: {
          display: true,
          text: "Měsíc",
        },
      },
    },
  };

  // Check if we have any non-zero data
  const hasData = totalCostData.some((value) => value > 0) || marginAmountData.some((value) => value > 0);

  return (
    <div className="flex-1 bg-gray-50 rounded-lg p-4 mb-4">
      {hasData ? (
        <>
          {hasM0M3Data && (
            <div className="flex items-center justify-between mb-4">
              <h3 className="text-lg font-medium text-gray-900">Vývoj marží</h3>
              <div className="flex items-center gap-3">
                <span className="text-sm text-gray-600">Procenta</span>
                <button
                  onClick={() => setShowPercentage(!showPercentage)}
                  className="flex items-center"
                >
                  {showPercentage ? (
                    <ToggleRight className="h-6 w-6 text-blue-600" />
                  ) : (
                    <ToggleLeft className="h-6 w-6 text-gray-400" />
                  )}
                </button>
                <span className="text-sm text-gray-600">Absolut. hodnoty</span>
              </div>
            </div>
          )}
          <div className="h-96">
            <Line data={chartData} options={chartOptions} />
          </div>
        </>
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
