import React, { useState } from "react";
import { BarChart3, RefreshCw } from "lucide-react";
import { CatalogItemDto, useRecalculateMargin } from "../../../../../api/hooks/useCatalog";
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
  const recalculateMargin = useRecalculateMargin();
  const [isRecalculating, setIsRecalculating] = useState(false);

  // Ensure marginHistory is always an array
  const safeMarginHistory = marginHistory || [];

  const handleRecalculate = async () => {
    if (!item?.productCode) return;

    setIsRecalculating(true);
    try {
      await recalculateMargin.mutateAsync({
        productCode: item.productCode,
        monthsBack: 13,
      });
    } catch (error) {
      console.error("Failed to recalculate margins:", error);
    } finally {
      setIsRecalculating(false);
    }
  };

  // Calculate average M0-M3 data from margin history
  const hasM0M3Data = safeMarginHistory.length > 0 && safeMarginHistory.some(m => (m.m0?.percentage || 0) > 0);

  const averageM0Percentage = safeMarginHistory.length > 0
    ? safeMarginHistory.reduce((sum, m) => sum + (m.m0?.percentage || 0), 0) / safeMarginHistory.length
    : 0;
  const averageM1Percentage = safeMarginHistory.length > 0
    ? safeMarginHistory.reduce((sum, m) => sum + (m.m1_a?.percentage || 0), 0) / safeMarginHistory.length
    : 0;
  const averageM2Percentage = safeMarginHistory.length > 0
    ? safeMarginHistory.reduce((sum, m) => sum + (m.m2?.percentage || 0), 0) / safeMarginHistory.length
    : 0;
  const averageM3Percentage = safeMarginHistory.length > 0
    ? safeMarginHistory.reduce((sum, m) => sum + (m.m3?.percentage || 0), 0) / safeMarginHistory.length
    : 0;


  // Use pre-calculated margin values from backend
  const sellingPrice = item?.price?.eshopPrice?.priceWithoutVat || 0;
  const margin = averageM3Percentage;
  const marginAmount = safeMarginHistory.length > 0
    ? safeMarginHistory.reduce((sum, m) => sum + (m.m3?.amount || 0), 0) / safeMarginHistory.length
    : 0;


  return (
    <div className="bg-white rounded-lg border border-gray-200 p-4 shadow-sm">
      <div className="flex items-center justify-between mb-3">
        <h4 className="text-md font-medium text-gray-900 flex items-center">
          <BarChart3 className="h-4 w-4 mr-2 text-gray-500" />
          Přehled nákladů a marže
        </h4>
        <button
          onClick={handleRecalculate}
          disabled={isRecalculating}
          className="flex items-center space-x-2 px-3 py-1.5 text-sm font-medium text-indigo-600 hover:text-indigo-700 hover:bg-indigo-50 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          title="Přepočítat marže za posledních 13 měsíců"
        >
          <RefreshCw
            className={`h-4 w-4 ${isRecalculating ? "animate-spin" : ""}`}
          />
          <span>{isRecalculating ? "Přepočítávám..." : "Přepočítat"}</span>
        </button>
      </div>


      {/* Table Layout: M0-M3 rows with Absolute, Percentage, and Cost columns */}
      {hasM0M3Data ? (
        <div className="bg-white rounded-lg border border-gray-200 overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead className="bg-gray-50">
                <tr>
                  <th className="px-4 py-3 text-left text-sm font-medium text-gray-700">Marže</th>
                  <th className="px-4 py-3 text-center text-sm font-medium text-gray-700">Náklady úrovně (Kč/ks)</th>
                  <th className="px-4 py-3 text-center text-sm font-medium text-gray-700">Kumulované náklady (Kč/ks)</th>
                  <th className="px-4 py-3 text-center text-sm font-medium text-gray-700">Procentuální marže (%)</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-200">
                {/* M0 Row - Green */}
                <tr className="bg-green-50 border-l-4 border-green-400">
                  <td className="px-4 py-3">
                    <div className="flex items-center">
                      <div className="w-3 h-3 rounded-full bg-green-500 mr-2"></div>
                      <span className="font-medium text-green-900">M0</span>
                    </div>
                  </td>
                  <td className="px-4 py-3 text-center">
                    <span className="text-lg font-bold text-green-900">
                      {(safeMarginHistory.length > 0
                        ? safeMarginHistory.reduce((sum, m) => sum + (m.m0?.costLevel || 0), 0) / safeMarginHistory.length
                        : 0).toLocaleString("cs-CZ", {
                        minimumFractionDigits: 2,
                        maximumFractionDigits: 2,
                      })}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-center">
                    <span className="text-lg font-bold text-green-900">
                      {(safeMarginHistory.length > 0
                        ? safeMarginHistory.reduce((sum, m) => sum + (m.m0?.costTotal || 0), 0) / safeMarginHistory.length
                        : 0).toLocaleString("cs-CZ", {
                        minimumFractionDigits: 2,
                        maximumFractionDigits: 2,
                      })}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-center">
                    <span className="text-lg font-bold text-green-900">
                      {averageM0Percentage.toLocaleString("cs-CZ", {
                        minimumFractionDigits: 1,
                        maximumFractionDigits: 1,
                      })}%
                    </span>
                  </td>
                </tr>

                {/* M1 Row - Yellow */}
                <tr className="bg-yellow-50 border-l-4 border-yellow-400">
                  <td className="px-4 py-3">
                    <div className="flex items-center">
                      <div className="w-3 h-3 rounded-full bg-yellow-500 mr-2"></div>
                      <span className="font-medium text-yellow-900">M1 (baseline)</span>
                    </div>
                  </td>
                  <td className="px-4 py-3 text-center">
                    <span className="text-lg font-bold text-yellow-900">
                      {(safeMarginHistory.length > 0
                        ? safeMarginHistory.reduce((sum, m) => sum + (m.m1_a?.costLevel || 0), 0) / safeMarginHistory.length
                        : 0).toLocaleString("cs-CZ", {
                        minimumFractionDigits: 2,
                        maximumFractionDigits: 2,
                      })}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-center">
                    <span className="text-lg font-bold text-yellow-900">
                      {(safeMarginHistory.length > 0
                        ? safeMarginHistory.reduce((sum, m) => sum + (m.m1_a?.costTotal || 0), 0) / safeMarginHistory.length
                        : 0).toLocaleString("cs-CZ", {
                        minimumFractionDigits: 2,
                        maximumFractionDigits: 2,
                      })}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-center">
                    <span className="text-lg font-bold text-yellow-900">
                      {averageM1Percentage.toLocaleString("cs-CZ", {
                        minimumFractionDigits: 1,
                        maximumFractionDigits: 1,
                      })}%
                    </span>
                  </td>
                </tr>

                {/* M2 Row - Orange */}
                <tr className="bg-orange-50 border-l-4 border-orange-400">
                  <td className="px-4 py-3">
                    <div className="flex items-center">
                      <div className="w-3 h-3 rounded-full bg-orange-500 mr-2"></div>
                      <span className="font-medium text-orange-900">M2</span>
                    </div>
                  </td>
                  <td className="px-4 py-3 text-center">
                    <span className="text-lg font-bold text-orange-900">
                      {(safeMarginHistory.length > 0
                        ? safeMarginHistory.reduce((sum, m) => sum + (m.m2?.costLevel || 0), 0) / safeMarginHistory.length
                        : 0).toLocaleString("cs-CZ", {
                        minimumFractionDigits: 2,
                        maximumFractionDigits: 2,
                      })}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-center">
                    <span className="text-lg font-bold text-orange-900">
                      {(safeMarginHistory.length > 0
                        ? safeMarginHistory.reduce((sum, m) => sum + (m.m2?.costTotal || 0), 0) / safeMarginHistory.length
                        : 0).toLocaleString("cs-CZ", {
                        minimumFractionDigits: 2,
                        maximumFractionDigits: 2,
                      })}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-center">
                    <span className="text-lg font-bold text-orange-900">
                      {averageM2Percentage.toLocaleString("cs-CZ", {
                        minimumFractionDigits: 1,
                        maximumFractionDigits: 1,
                      })}%
                    </span>
                  </td>
                </tr>

                {/* M3 Row - Red */}
                <tr className="bg-red-50 border-l-4 border-red-400">
                  <td className="px-4 py-3">
                    <div className="flex items-center">
                      <div className="w-3 h-3 rounded-full bg-red-500 mr-2"></div>
                      <span className="font-medium text-red-900">M3</span>
                    </div>
                  </td>
                  <td className="px-4 py-3 text-center">
                    <span className="text-lg font-bold text-red-900">
                      {(safeMarginHistory.length > 0
                        ? safeMarginHistory.reduce((sum, m) => sum + (m.m3?.costLevel || 0), 0) / safeMarginHistory.length
                        : 0).toLocaleString("cs-CZ", {
                        minimumFractionDigits: 2,
                        maximumFractionDigits: 2,
                      })}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-center">
                    <span className="text-lg font-bold text-red-900">
                      {(safeMarginHistory.length > 0
                        ? safeMarginHistory.reduce((sum, m) => sum + (m.m3?.costTotal || 0), 0) / safeMarginHistory.length
                        : 0).toLocaleString("cs-CZ", {
                        minimumFractionDigits: 2,
                        maximumFractionDigits: 2,
                      })}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-center">
                    <span className="text-lg font-bold text-red-900">
                      {averageM3Percentage.toLocaleString("cs-CZ", {
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
          <h5 className="text-sm font-medium text-gray-700 mb-2">Marže</h5>
          <div className="grid grid-cols-2 gap-4">
            <div className="text-center p-3 bg-amber-50 rounded-lg border border-amber-200">
              <div className="text-sm font-medium text-gray-600 mb-1">
                Marže v %
              </div>
              <div
                className={`text-2xl font-bold ${margin >= 0 ? "text-amber-900" : "text-red-900"}`}
              >
                {margin.toLocaleString("cs-CZ", {
                  minimumFractionDigits: 1,
                  maximumFractionDigits: 1,
                })}
                %
              </div>
              <div className="text-xs text-gray-500 mt-1">
                {margin >= 0 ? "zisk" : "ztráta"}
              </div>
            </div>

            <div className="text-center p-3 bg-amber-50 rounded-lg border border-amber-200">
              <div className="text-sm font-medium text-gray-600 mb-1">
                Marže v Kč
              </div>
              <div
                className={`text-2xl font-bold ${marginAmount >= 0 ? "text-amber-900" : "text-red-900"}`}
              >
                {marginAmount.toLocaleString("cs-CZ", {
                  minimumFractionDigits: 2,
                  maximumFractionDigits: 2,
                })}{" "}
                Kč
              </div>
              <div className="text-xs text-gray-500 mt-1">za kus</div>
            </div>
          </div>
        </div>
      )}

      {sellingPrice === 0 && (
        <div className="mt-2 text-center text-xs text-amber-600 bg-amber-50 p-2 rounded">
          Není dostupná prodejní cena - marže nelze vypočítat
        </div>
      )}
    </div>
  );
};

export default MarginsSummary;
