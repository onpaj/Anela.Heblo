import React from "react";
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
import { mapMarginDataToMonthlyArrays } from "./MarginsChart.utils";

interface MarginsChartProps {
  marginHistory: MarginHistoryDto[];
  journalEntries: JournalEntryDto[];
}

const MarginsChart: React.FC<MarginsChartProps> = ({
  marginHistory,
  journalEntries,
}) => {
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

  const {
    m0PercentageData,
    m1PercentageData,
    m2PercentageData,
    m3PercentageData,
    m0CostLevelData,
    m1CostLevelData,
    m2CostLevelData,
    m3CostLevelData
  } = mapMarginDataToMonthlyArrays(marginHistory);

  // Check if we have M0-M3 data
  const hasM0M2Data = m0PercentageData.some(value => value > 0) ||
                      m1PercentageData.some(value => value > 0) ||
                      m2PercentageData.some(value => value > 0) ||
                      m3PercentageData.some(value => value > 0);

  // Generate point styling for percentage line charts (12 months without current)
  const m0Styling = generatePointStyling(12, journalEntries, "rgba(34, 197, 94, 1)"); // Green
  const m1Styling = generatePointStyling(12, journalEntries, "rgba(234, 179, 8, 1)"); // Yellow
  const m2Styling = generatePointStyling(12, journalEntries, "rgba(249, 115, 22, 1)"); // Orange
  const m3Styling = generatePointStyling(12, journalEntries, "rgba(239, 68, 68, 1)"); // Red

  // Build stacked bar chart datasets for CostLevel (M0 bottom, M3 top)
  const costLevelDatasets = hasM0M2Data ? [
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
      label: "M2R - Náklady prodeje (Kč/ks)",
      data: m2CostLevelData,
      backgroundColor: "rgba(249, 115, 22, 0.7)", // Orange
      borderColor: "rgba(249, 115, 22, 1)",
      borderWidth: 1,
      yAxisID: "y",
      stack: 'costs',
    },
    {
      type: 'bar' as const,
      label: "M3R - Režijní náklady (Kč/ks)",
      data: m3CostLevelData,
      backgroundColor: "rgba(239, 68, 68, 0.7)", // Red
      borderColor: "rgba(239, 68, 68, 1)",
      borderWidth: 1,
      yAxisID: "y",
      stack: 'costs',
    },
  ] : [];

  // Add percentage line charts on secondary Y axis
  const percentageDatasets = hasM0M2Data ? [
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
      label: "M2R - Marže + prodej (%)",
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
      label: "M3R - Finální marže (%)",
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
  const hasData = hasM0M2Data;

  return (
    <div className="flex-1 bg-gray-50 dark:bg-graphite-surface-2 rounded-lg p-4 mb-4">
      {hasData ? (
        <>
          <div className="flex items-center justify-between mb-4">
            <h3 className="text-lg font-medium text-gray-900 dark:text-graphite-text">Vývoj nákladů a marží</h3>
          </div>
          <div className="h-96">
            <Chart type="bar" data={chartData} options={chartOptions} />
          </div>
        </>
      ) : (
        <div className="flex items-center justify-center h-96">
          <div className="text-center text-gray-500 dark:text-graphite-muted">
            <BarChart3 className="h-12 w-12 mx-auto mb-2 text-gray-300 dark:text-graphite-faint" />
            <p>Žádná data pro zobrazení grafu</p>
            <p className="text-sm">Náklady a marže za posledních 12 měsíců (bez aktuálního měsíce)</p>
          </div>
        </div>
      )}
    </div>
  );
};

export default MarginsChart;
