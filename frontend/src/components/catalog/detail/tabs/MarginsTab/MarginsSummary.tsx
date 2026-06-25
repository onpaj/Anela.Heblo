import React from "react";
import { BarChart3 } from "lucide-react";
import { CatalogItemDto } from "../../../../../api/hooks/useCatalog";
import { ManufactureCostDto, MarginHistoryDto } from "../../../../../api/generated/api-client";

interface MarginsSummaryProps {
  item: CatalogItemDto | null;
  manufactureCostHistory: ManufactureCostDto[];
  marginHistory: MarginHistoryDto[];
}

const MarginsSummary: React.FC<MarginsSummaryProps> = ({
  item,
  manufactureCostHistory,
  marginHistory,
}) => {
  // Ensure marginHistory is always an array
  const safeMarginHistory = marginHistory || [];

  // Calculate average M0-M2 data from margin history
  const hasM0M2Data = safeMarginHistory.length > 0 && safeMarginHistory.some(m => (m.m0?.percentage || 0) > 0);

  const averageM0Percentage = safeMarginHistory.length > 0
    ? safeMarginHistory.reduce((sum, m) => sum + (m.m0?.percentage || 0), 0) / safeMarginHistory.length
    : 0;
  const averageM1Percentage = safeMarginHistory.length > 0
    ? safeMarginHistory.reduce((sum, m) => sum + (m.m1?.percentage || 0), 0) / safeMarginHistory.length
    : 0;
  const averageM2Percentage = safeMarginHistory.length > 0
    ? safeMarginHistory.reduce((sum, m) => sum + (m.m2?.percentage || 0), 0) / safeMarginHistory.length
    : 0;


  // Use pre-calculated margin values from backend (M2 is now the final margin level)
  const sellingPrice = item?.price?.eshopPrice?.priceWithoutVat || 0;
  const margin = averageM2Percentage;
  const marginAmount = safeMarginHistory.length > 0
    ? safeMarginHistory.reduce((sum, m) => sum + (m.m2?.amount || 0), 0) / safeMarginHistory.length
    : 0;


  return (
    <div className="bg-white dark:bg-graphite-surface rounded-lg border border-gray-200 dark:border-graphite-border p-4 shadow-sm dark:shadow-soft-dark">
      <h4 className="text-md font-medium text-gray-900 dark:text-graphite-text mb-3 flex items-center">
        <BarChart3 className="h-4 w-4 mr-2 text-gray-500 dark:text-graphite-muted" />
        Přehled nákladů a marže
      </h4>


      {/* Table Layout: M0-M2 rows with Absolute, Percentage, and Cost columns */}
      {hasM0M2Data ? (
        <div className="bg-white dark:bg-graphite-surface rounded-lg border border-gray-200 dark:border-graphite-border overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead className="bg-gray-50 dark:bg-graphite-surface-2">
                <tr>
                  <th className="px-4 py-3 text-left text-sm font-medium text-gray-700 dark:text-graphite-muted">Marže</th>
                  <th className="px-4 py-3 text-center text-sm font-medium text-gray-700 dark:text-graphite-muted">Náklady úrovně (Kč/ks)</th>
                  <th className="px-4 py-3 text-center text-sm font-medium text-gray-700 dark:text-graphite-muted">Kumulované náklady (Kč/ks)</th>
                  <th className="px-4 py-3 text-center text-sm font-medium text-gray-700 dark:text-graphite-muted">Procentuální marže (%)</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-200 dark:divide-graphite-border">
                {/* M0 Row - Green */}
                <tr className="bg-green-50 border-l-4 border-green-400 dark:bg-emerald-900/30 dark:border-emerald-400">
                  <td className="px-4 py-3">
                    <div className="flex items-center">
                      <div className="w-3 h-3 rounded-full bg-green-500 mr-2"></div>
                      <span className="font-medium text-green-900 dark:text-emerald-300">M0</span>
                    </div>
                  </td>
                  <td className="px-4 py-3 text-center">
                    <span className="text-lg font-bold text-green-900 dark:text-emerald-300">
                      {(safeMarginHistory.length > 0
                        ? safeMarginHistory.reduce((sum, m) => sum + (m.m0?.costLevel || 0), 0) / safeMarginHistory.length
                        : 0).toLocaleString("cs-CZ", {
                        minimumFractionDigits: 2,
                        maximumFractionDigits: 2,
                      })}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-center">
                    <span className="text-lg font-bold text-green-900 dark:text-emerald-300">
                      {(safeMarginHistory.length > 0
                        ? safeMarginHistory.reduce((sum, m) => sum + (m.m0?.costTotal || 0), 0) / safeMarginHistory.length
                        : 0).toLocaleString("cs-CZ", {
                        minimumFractionDigits: 2,
                        maximumFractionDigits: 2,
                      })}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-center">
                    <span className="text-lg font-bold text-green-900 dark:text-emerald-300">
                      {averageM0Percentage.toLocaleString("cs-CZ", {
                        minimumFractionDigits: 1,
                        maximumFractionDigits: 1,
                      })}%
                    </span>
                  </td>
                </tr>

                {/* M1 Row - Yellow */}
                <tr className="bg-yellow-50 border-l-4 border-yellow-400 dark:bg-amber-900/30 dark:border-amber-400">
                  <td className="px-4 py-3">
                    <div className="flex items-center">
                      <div className="w-3 h-3 rounded-full bg-yellow-500 mr-2"></div>
                      <span className="font-medium text-yellow-900 dark:text-amber-300">M1</span>
                    </div>
                  </td>
                  <td className="px-4 py-3 text-center">
                    <span className="text-lg font-bold text-yellow-900 dark:text-amber-300">
                      {(safeMarginHistory.length > 0
                        ? safeMarginHistory.reduce((sum, m) => sum + (m.m1?.costLevel || 0), 0) / safeMarginHistory.length
                        : 0).toLocaleString("cs-CZ", {
                        minimumFractionDigits: 2,
                        maximumFractionDigits: 2,
                      })}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-center">
                    <span className="text-lg font-bold text-yellow-900 dark:text-amber-300">
                      {(safeMarginHistory.length > 0
                        ? safeMarginHistory.reduce((sum, m) => sum + (m.m1?.costTotal || 0), 0) / safeMarginHistory.length
                        : 0).toLocaleString("cs-CZ", {
                        minimumFractionDigits: 2,
                        maximumFractionDigits: 2,
                      })}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-center">
                    <span className="text-lg font-bold text-yellow-900 dark:text-amber-300">
                      {averageM1Percentage.toLocaleString("cs-CZ", {
                        minimumFractionDigits: 1,
                        maximumFractionDigits: 1,
                      })}%
                    </span>
                  </td>
                </tr>

                {/* M2 Row - Orange (Final Margin Level) */}
                <tr className="bg-orange-50 border-l-4 border-orange-400 dark:bg-orange-900/30 dark:border-orange-400">
                  <td className="px-4 py-3">
                    <div className="flex items-center">
                      <div className="w-3 h-3 rounded-full bg-orange-500 mr-2"></div>
                      <span className="font-medium text-orange-900 dark:text-orange-300">M2</span>
                    </div>
                  </td>
                  <td className="px-4 py-3 text-center">
                    <span className="text-lg font-bold text-orange-900 dark:text-orange-300">
                      {(safeMarginHistory.length > 0
                        ? safeMarginHistory.reduce((sum, m) => sum + (m.m2?.costLevel || 0), 0) / safeMarginHistory.length
                        : 0).toLocaleString("cs-CZ", {
                        minimumFractionDigits: 2,
                        maximumFractionDigits: 2,
                      })}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-center">
                    <span className="text-lg font-bold text-orange-900 dark:text-orange-300">
                      {(safeMarginHistory.length > 0
                        ? safeMarginHistory.reduce((sum, m) => sum + (m.m2?.costTotal || 0), 0) / safeMarginHistory.length
                        : 0).toLocaleString("cs-CZ", {
                        minimumFractionDigits: 2,
                        maximumFractionDigits: 2,
                      })}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-center">
                    <span className="text-lg font-bold text-orange-900 dark:text-orange-300">
                      {averageM2Percentage.toLocaleString("cs-CZ", {
                        minimumFractionDigits: 1,
                        maximumFractionDigits: 1,
                      })}%
                    </span>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      ) : (
        <div>
          <h5 className="text-sm font-medium text-gray-700 dark:text-graphite-muted mb-2">Marže</h5>
          <div className="grid grid-cols-2 gap-4">
            <div className="text-center p-3 bg-amber-50 rounded-lg border border-amber-200 dark:bg-amber-900/30 dark:border-amber-800/40">
              <div className="text-sm font-medium text-gray-600 dark:text-graphite-muted mb-1">
                Marže v %
              </div>
              <div
                className={`text-2xl font-bold ${margin >= 0 ? "text-amber-900 dark:text-amber-300" : "text-red-900 dark:text-red-300"}`}
              >
                {margin.toLocaleString("cs-CZ", {
                  minimumFractionDigits: 1,
                  maximumFractionDigits: 1,
                })}
                %
              </div>
              <div className="text-xs text-gray-500 dark:text-graphite-muted mt-1">
                {margin >= 0 ? "zisk" : "ztráta"}
              </div>
            </div>

            <div className="text-center p-3 bg-amber-50 rounded-lg border border-amber-200 dark:bg-amber-900/30 dark:border-amber-800/40">
              <div className="text-sm font-medium text-gray-600 dark:text-graphite-muted mb-1">
                Marže v Kč
              </div>
              <div
                className={`text-2xl font-bold ${marginAmount >= 0 ? "text-amber-900 dark:text-amber-300" : "text-red-900 dark:text-red-300"}`}
              >
                {marginAmount.toLocaleString("cs-CZ", {
                  minimumFractionDigits: 2,
                  maximumFractionDigits: 2,
                })}{" "}
                Kč
              </div>
              <div className="text-xs text-gray-500 dark:text-graphite-muted mt-1">za kus</div>
            </div>
          </div>
        </div>
      )}

      {sellingPrice === 0 && (
        <div className="mt-2 text-center text-xs text-amber-600 bg-amber-50 p-2 rounded dark:text-amber-400 dark:bg-amber-900/30">
          Není dostupná prodejní cena - marže nelze vypočítat
        </div>
      )}
    </div>
  );
};

export default MarginsSummary;
