import React, { useState } from "react";
import { BarChart3 } from "lucide-react";
import { Chart } from "react-chartjs-2";
import {
  MarginHistoryDto,
  JournalEntryDto,
} from "../../../../../api/generated/api-client";
import {
  generatePointStyling,
  generateTooltipCallback,
} from "../../charts/ChartHelpers";

interface MarginsChartProps {
  marginHistory: MarginHistoryDto[];
  journalEntries: JournalEntryDto[];
}

const MarginsChart: React.FC<MarginsChartProps> = ({
  marginHistory,
  journalEntries,
}) => {
  // Toggle for showing M1_B actual monthly costs
  const [showM1B, setShowM1B] = useState(false);

  // Generate month labels excluding current month (last 12 months)
  const generateMonthLabelsExcludingCurrent = (): string[] => {
    const months = [];
    const now = new Date();

    // Start from 12 months back, excluding current month (i starts at 12 instead of 0)
    for (let i = 12; i >= 1; i--) {
      const date = new Date(now.getFullYear(), now.getMonth() - i, 1);
      months.push(
        date.toLocaleDateString("cs-CZ", { month: "short", year: "numeric" }),
      );
    }

    return months;
  };

  const monthLabels = generateMonthLabelsExcludingCurrent();

  // Map margin history data to monthly arrays (excluding current month)
  const mapMarginDataToMonthlyArrays = () => {
    const m0PercentageData = new Array(12).fill(0);
    const m1_APercentageData = new Array(12).fill(0);
    const m2PercentageData = new Array(12).fill(0);
    const m3PercentageData = new Array(12).fill(0);
    const m0CostLevelData = new Array(12).fill(0);
    const m1_ACostLevelData = new Array(12).fill(0);
    const m2CostLevelData = new Array(12).fill(0);
    const m3CostLevelData = new Array(12).fill(0);
    const now = new Date();
    const currentYear = now.getFullYear();
    const currentMonth = now.getMonth() + 1;

    // Create maps for quick lookup of margin data by year-month key
    const m0PercentageMap = new Map<string, number>();
    const m1_APercentageMap = new Map<string, number>();
    const m2PercentageMap = new Map<string, number>();
    const m3PercentageMap = new Map<string, number>();
    const m0CostLevelMap = new Map<string, number>();
    const m1_ACostLevelMap = new Map<string, number>();
    const m2CostLevelMap = new Map<string, number>();
    const m3CostLevelMap = new Map<string, number>();

    marginHistory.forEach((record) => {
      if (record.date) {
        const recordDate = new Date(record.date);
        const recordYear = recordDate.getFullYear();
        const recordMonth = recordDate.getMonth() + 1;

        // Skip current month data
        if (recordYear === currentYear && recordMonth === currentMonth) {
          return;
        }

        const key = `${recordYear}-${recordMonth}`;

        // M0-M3 percentage properties
        m0PercentageMap.set(key, record.m0?.percentage || 0);
        m1PercentageMap.set(key, record.m1?.percentage || 0);
        m2PercentageMap.set(key, record.m2?.percentage || 0);
        m3PercentageMap.set(key, record.m3?.percentage || 0);

        // M0-M3 CostLevel properties
        m0CostLevelMap.set(key, record.m0?.costLevel || 0);
        m1CostLevelMap.set(key, record.m1?.costLevel || 0);
        m2CostLevelMap.set(key, record.m2?.costLevel || 0);
        m3CostLevelMap.set(key, record.m3?.costLevel || 0);
      }
    });

    // Fill the arrays with data for the last 12 months (excluding current month)
    for (let i = 0; i < 12; i++) {
      const monthsBack = 12 - i;
      let adjustedYear = currentYear;
      let adjustedMonth = currentMonth - monthsBack;

      // Handle year transitions
      if (adjustedMonth <= 0) {
        adjustedYear--;
        adjustedMonth += 12;
      }

      const key = `${adjustedYear}-${adjustedMonth}`;
      m0PercentageData[i] = m0PercentageMap.get(key) || 0;
      m1PercentageData[i] = m1PercentageMap.get(key) || 0;
      m2PercentageData[i] = m2PercentageMap.get(key) || 0;
      m3PercentageData[i] = m3PercentageMap.get(key) || 0;
      m0CostLevelData[i] = m0CostLevelMap.get(key) || 0;
      m1CostLevelData[i] = m1CostLevelMap.get(key) || 0;
      m2CostLevelData[i] = m2CostLevelMap.get(key) || 0;
      m3CostLevelData[i] = m3CostLevelMap.get(key) || 0;
    }

    return {
      m0PercentageData,
      m1PercentageData,
      m2PercentageData,
      m3PercentageData,
      m0CostLevelData,
      m1CostLevelData,
      m2CostLevelData,
      m3CostLevelData
    };
  };

  const {
    m0PercentageData,
    m1PercentageData,
    m2PercentageData,
    m3PercentageData,
    m0CostLevelData,
    m1CostLevelData,
    m2CostLevelData,
    m3CostLevelData
  } = mapMarginDataToMonthlyArrays();

  // Check if we have M0-M3 data
  const hasM0M3Data = m0PercentageData.some(value => value > 0) ||
                      m1PercentageData.some(value => value > 0) ||
                      m2PercentageData.some(value => value > 0) ||
                      m3PercentageData.some(value => value > 0);

  // Generate point styling for percentage line charts (12 months without current)
  const m0Styling = generatePointStyling(12, journalEntries, "rgba(34, 197, 94, 1)"); // Green
  const m1Styling = generatePointStyling(12, journalEntries, "rgba(234, 179, 8, 1)"); // Yellow
  const m2Styling = generatePointStyling(12, journalEntries, "rgba(249, 115, 22, 1)"); // Orange
  const m3Styling = generatePointStyling(12, journalEntries, "rgba(239, 68, 68, 1)"); // Red

  // Build stacked bar chart datasets for CostLevel (M0 bottom, M3 top)
  const costLevelDatasets = hasM0M3Data ? [
    {
      type: 'bar' as const,
      label: "M0 - Náklady materiálu (Kč/ks)",
      data: m0CostLevelData,
      backgroundColor: "rgba(34, 197, 94, 0.7)", // Green
      borderColor: "rgba(34, 197, 94, 1)",
      borderWidth: 1,
      yAxisID: "y",
      stack: 'costs',
    },
    {
      type: 'bar' as const,
      label: "M1 - Náklady výroby (Kč/ks)",
      data: m1CostLevelData,
      backgroundColor: "rgba(234, 179, 8, 0.7)", // Yellow
      borderColor: "rgba(234, 179, 8, 1)",
      borderWidth: 1,
      yAxisID: "y",
      stack: 'costs',
    },
    {
      type: 'bar' as const,
      label: "M2 - Náklady prodeje (Kč/ks)",
      data: m2CostLevelData,
      backgroundColor: "rgba(249, 115, 22, 0.7)", // Orange
      borderColor: "rgba(249, 115, 22, 1)",
      borderWidth: 1,
      yAxisID: "y",
      stack: 'costs',
    },
    {
      type: 'bar' as const,
      label: "M3 - Režijní náklady (Kč/ks)",
      data: m3CostLevelData,
      backgroundColor: "rgba(239, 68, 68, 0.7)", // Red
      borderColor: "rgba(239, 68, 68, 1)",
      borderWidth: 1,
      yAxisID: "y",
      stack: 'costs',
    },
  ] : [];

  // Add percentage line charts on secondary Y axis
  const percentageDatasets = hasM0M3Data ? [
    {
      type: 'line' as const,
      label: "M0 - Marže materiál (%)",
      data: m0PercentageData,
      backgroundColor: "rgba(34, 197, 94, 0.1)",
      borderColor: "rgba(34, 197, 94, 1)",
      borderWidth: 2,
      tension: 0.1,
      pointBackgroundColor: m0Styling.pointBackgroundColors,
      pointBorderColor: m0Styling.pointBackgroundColors,
      pointRadius: m0Styling.pointRadiuses,
      pointHoverRadius: m0Styling.pointHoverRadiuses,
      yAxisID: "y1",
      fill: false,
    },
    {
      type: 'line' as const,
      label: "M1 - Marže + výroba (%)",
      data: m1PercentageData,
      backgroundColor: "rgba(234, 179, 8, 0.1)",
      borderColor: "rgba(234, 179, 8, 1)",
      borderWidth: 2,
      tension: 0.1,
      pointBackgroundColor: m1Styling.pointBackgroundColors,
      pointBorderColor: m1Styling.pointBackgroundColors,
      pointRadius: m1Styling.pointRadiuses,
      pointHoverRadius: m1Styling.pointHoverRadiuses,
      yAxisID: "y1",
      fill: false,
    },
    {
      type: 'line' as const,
      label: "M2 - Marže + prodej (%)",
      data: m2PercentageData,
      backgroundColor: "rgba(249, 115, 22, 0.1)",
      borderColor: "rgba(249, 115, 22, 1)",
      borderWidth: 2,
      tension: 0.1,
      pointBackgroundColor: m2Styling.pointBackgroundColors,
      pointBorderColor: m2Styling.pointBackgroundColors,
      pointRadius: m2Styling.pointRadiuses,
      pointHoverRadius: m2Styling.pointHoverRadiuses,
      yAxisID: "y1",
      fill: false,
    },
    {
      type: 'line' as const,
      label: "M3 - Finální marže (%)",
      data: m3PercentageData,
      backgroundColor: "rgba(239, 68, 68, 0.1)",
      borderColor: "rgba(239, 68, 68, 1)",
      borderWidth: 2,
      tension: 0.1,
      pointBackgroundColor: m3Styling.pointBackgroundColors,
      pointBorderColor: m3Styling.pointBackgroundColors,
      pointRadius: m3Styling.pointRadiuses,
      pointHoverRadius: m3Styling.pointHoverRadiuses,
      yAxisID: "y1",
      fill: false,
    },
  ] : [];

  const chartData = {
    labels: monthLabels,
    datasets: [...costLevelDatasets, ...percentageDatasets],
  };

  const chartOptions = {
    responsive: true,
    maintainAspectRatio: false,
    interaction: {
      mode: "index" as const,
      intersect: false,
    },
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
      y: {
        type: "linear" as const,
        display: true,
        position: "left" as const,
        beginAtZero: true,
        stacked: true,
        title: {
          display: true,
          text: "Náklady (Kč/ks)",
        },
      },
      y1: {
        type: "linear" as const,
        display: true,
        position: "right" as const,
        beginAtZero: true,
        title: {
          display: true,
          text: "Marže (%)",
        },
        grid: {
          drawOnChartArea: false,
        },
      },
      x: {
        stacked: true,
        title: {
          display: true,
          text: "Měsíc",
        },
      },
    },
  };

  // Check if we have any non-zero data
  const hasData = hasM0M3Data;

  return (
    <div className="flex-1 bg-gray-50 rounded-lg p-4 mb-4">
      {hasData ? (
        <>
          <div className="flex items-center justify-between mb-4">
            <h3 className="text-lg font-medium text-gray-900">Vývoj nákladů a marží</h3>
          </div>
          <div className="h-96">
            <Chart type="bar" data={chartData} options={chartOptions} />
          </div>
        </>
      ) : (
        <div className="flex items-center justify-center h-96">
          <div className="text-center text-gray-500">
            <BarChart3 className="h-12 w-12 mx-auto mb-2 text-gray-300" />
            <p>Žádná data pro zobrazení grafu</p>
            <p className="text-sm">Náklady a marže za posledních 12 měsíců (bez aktuálního měsíce)</p>
          </div>
        </div>
      )}
    </div>
  );
};

export default MarginsChart;
