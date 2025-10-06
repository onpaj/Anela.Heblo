import React from "react";
import { BarChart3 } from "lucide-react";
import { CatalogItemDto } from "../../../../../api/hooks/useCatalog";
import { ManufactureCostDto } from "../../../../../api/generated/api-client";

interface MarginsSummaryProps {
  item: CatalogItemDto | null;
  manufactureCostHistory: ManufactureCostDto[];
}

const MarginsSummary: React.FC<MarginsSummaryProps> = ({
  item,
  manufactureCostHistory,
}) => {
  // Calculate average manufacturing costs for display
  const averageMaterialCost =
    manufactureCostHistory.length > 0
      ? manufactureCostHistory.reduce(
          (sum, record) => sum + (record.materialCost || 0),
          0,
        ) / manufactureCostHistory.length
      : 0;

  const averageHandlingCost =
    manufactureCostHistory.length > 0
      ? manufactureCostHistory.reduce(
          (sum, record) => sum + (record.handlingCost || 0),
          0,
        ) / manufactureCostHistory.length
      : 0;

  const averageTotalCost =
    manufactureCostHistory.length > 0
      ? manufactureCostHistory.reduce(
          (sum, record) => sum + (record.total || 0),
          0,
        ) / manufactureCostHistory.length
      : 0;

  // Use pre-calculated margin values from backend
  const sellingPrice = item?.price?.eshopPrice?.priceWithoutVat || 0;
  const margin = item?.marginPercentage || 0;
  const marginAmount = item?.marginAmount || 0;

  // Check if M0-M3 properties are available (from future backend updates)
  const hasM0M3Data = 'm0Percentage' in (item || {});
  const m0Percentage = (item as any)?.m0Percentage || 0;
  const m1Percentage = (item as any)?.m1Percentage || 0;
  const m2Percentage = (item as any)?.m2Percentage || 0;
  const m3Percentage = (item as any)?.m3Percentage || 0;

  // Get margin color based on percentage
  const getMarginColor = (marginPercent: number) => {
    if (marginPercent < 30) return "text-red-900 bg-red-50 border-red-200";
    if (marginPercent < 50) return "text-orange-900 bg-orange-50 border-orange-200";
    if (marginPercent < 80) return "text-yellow-900 bg-yellow-50 border-yellow-200";
    return "text-green-900 bg-green-50 border-green-200";
  };

  return (
    <div className="bg-white rounded-lg border border-gray-200 p-4 shadow-sm">
      <h4 className="text-md font-medium text-gray-900 mb-3 flex items-center">
        <BarChart3 className="h-4 w-4 mr-2 text-gray-500" />
        Přehled nákladů a marže
      </h4>

      {/* Compact cost breakdown */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-3 mb-4">
        <div className="text-center p-2 bg-green-50 rounded border border-green-200">
          <div className="text-xs font-medium text-gray-600 mb-1">Materiál</div>
          <div className="text-lg font-bold text-green-900">
            {averageMaterialCost.toLocaleString("cs-CZ", {
              minimumFractionDigits: 2,
              maximumFractionDigits: 2,
            })}
          </div>
          <div className="text-xs text-gray-500">Kč/ks</div>
        </div>

        <div className="text-center p-2 bg-blue-50 rounded border border-blue-200">
          <div className="text-xs font-medium text-gray-600 mb-1">
            Zpracování
          </div>
          <div className="text-lg font-bold text-blue-900">
            {averageHandlingCost.toLocaleString("cs-CZ", {
              minimumFractionDigits: 2,
              maximumFractionDigits: 2,
            })}
          </div>
          <div className="text-xs text-gray-500">Kč/ks</div>
        </div>

        <div className="text-center p-2 bg-purple-50 rounded border border-purple-200">
          <div className="text-xs font-medium text-gray-600 mb-1">
            Celkem náklady
          </div>
          <div className="text-lg font-bold text-purple-900">
            {averageTotalCost.toLocaleString("cs-CZ", {
              minimumFractionDigits: 2,
              maximumFractionDigits: 2,
            })}
          </div>
          <div className="text-xs text-gray-500">Kč/ks</div>
        </div>

        <div className="text-center p-2 bg-orange-50 rounded border border-orange-200">
          <div className="text-xs font-medium text-gray-600 mb-1">
            Prodej (bez DPH)
          </div>
          <div className="text-lg font-bold text-orange-900">
            {sellingPrice.toLocaleString("cs-CZ", {
              minimumFractionDigits: 2,
              maximumFractionDigits: 2,
            })}
          </div>
          <div className="text-xs text-gray-500">Kč/ks</div>
        </div>
      </div>

      {/* Margin summary - M0-M3 levels or legacy format */}
      {hasM0M3Data ? (
        <div>
          <h5 className="text-sm font-medium text-gray-700 mb-2">Úrovně marže</h5>
          <div className="grid grid-cols-2 gap-3">
            <div className={`text-center p-3 rounded-lg border ${getMarginColor(m0Percentage)}`}>
              <div className="text-sm font-medium text-gray-600 mb-1">M0 - Materiál</div>
              <div className="text-xl font-bold">
                {m0Percentage.toLocaleString("cs-CZ", {
                  minimumFractionDigits: 1,
                  maximumFractionDigits: 1,
                })}%
              </div>
              <div className="text-xs text-gray-500 mt-1">základní marže</div>
            </div>

            <div className={`text-center p-3 rounded-lg border ${getMarginColor(m1Percentage)}`}>
              <div className="text-sm font-medium text-gray-600 mb-1">M1 - + Výroba</div>
              <div className="text-xl font-bold">
                {m1Percentage.toLocaleString("cs-CZ", {
                  minimumFractionDigits: 1,
                  maximumFractionDigits: 1,
                })}%
              </div>
              <div className="text-xs text-gray-500 mt-1">s výrobou</div>
            </div>

            <div className={`text-center p-3 rounded-lg border ${getMarginColor(m2Percentage)}`}>
              <div className="text-sm font-medium text-gray-600 mb-1">M2 - + Prodej</div>
              <div className="text-xl font-bold">
                {m2Percentage.toLocaleString("cs-CZ", {
                  minimumFractionDigits: 1,
                  maximumFractionDigits: 1,
                })}%
              </div>
              <div className="text-xs text-gray-500 mt-1">s prodejem</div>
            </div>

            <div className={`text-center p-3 rounded-lg border ${getMarginColor(m3Percentage)}`}>
              <div className="text-sm font-medium text-gray-600 mb-1">M3 - Celkem</div>
              <div className="text-xl font-bold">
                {m3Percentage.toLocaleString("cs-CZ", {
                  minimumFractionDigits: 1,
                  maximumFractionDigits: 1,
                })}%
              </div>
              <div className="text-xs text-gray-500 mt-1">finální marže</div>
            </div>
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
  );
};

export default MarginsSummary;
