import React from "react";
import { ShoppingCart } from "lucide-react";
import { CatalogPurchaseRecordDto } from "../../../../api/hooks/useCatalog";

interface PurchaseHistoryTabProps {
  purchaseHistory: CatalogPurchaseRecordDto[];
  isLoading: boolean;
}

const PurchaseHistoryTab: React.FC<PurchaseHistoryTabProps> = ({
  purchaseHistory,
  isLoading,
}) => {
  // Sort history by date (most recent first)
  const sortedHistory = [...purchaseHistory].sort((a, b) => {
    const dateA = new Date(a.date || 0);
    const dateB = new Date(b.date || 0);
    return dateB.getTime() - dateA.getTime();
  });

  // Format date for display - using numeric month for better readability
  const formatDate = (dateString: string | Date | undefined) => {
    if (!dateString) return "-";
    const date = new Date(dateString);
    return date.toLocaleDateString("cs-CZ", {
      day: "2-digit",
      month: "2-digit",
      year: "numeric",
    });
  };

  if (sortedHistory.length === 0) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="text-center text-gray-500">
          <ShoppingCart className="h-12 w-12 mx-auto mb-2 text-gray-300" />
          <p className="text-lg font-medium">Žádná historie nákupů</p>
          <p className="text-sm">
            Pro tento produkt není k dispozici historie nákupů
          </p>
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
      </div>

      {/* Purchase history grid with fixed height and scrollbar */}
      <div className="bg-white rounded-lg border border-gray-200 overflow-hidden">
        <div className="h-96 overflow-y-auto">
          <table className="w-full text-sm">
            <thead className="sticky top-0 z-10 bg-gray-50 border-b border-gray-200">
              <tr>
                <th className="text-left py-3 px-4 font-medium text-gray-700">
                  Datum
                </th>
                <th className="text-left py-3 px-4 font-medium text-gray-700">
                  Dodavatel
                </th>
                <th className="text-right py-3 px-4 font-medium text-gray-700">
                  Množství
                </th>
                <th className="text-right py-3 px-4 font-medium text-gray-700">
                  Cena/ks
                </th>
                <th className="text-right py-3 px-4 font-medium text-gray-700">
                  Celkem
                </th>
                <th className="text-center py-3 px-4 font-medium text-gray-700">
                  Doklad
                </th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {sortedHistory.map((record, index) => (
                <tr key={index} className="hover:bg-gray-50">
                  <td className="py-3 px-4 text-gray-900">
                    {formatDate(record.date)}
                  </td>
                  <td
                    className="py-3 px-4 text-gray-900 max-w-xs truncate"
                    title={record.supplierName || "-"}
                  >
                    {record.supplierName || "-"}
                  </td>
                  <td className="py-3 px-4 text-right text-gray-900 font-medium">
                    {record.amount
                      ? Math.round(record.amount * 100) / 100
                      : "-"}
                  </td>
                  <td className="py-3 px-4 text-right text-gray-900 font-medium">
                    {record.pricePerPiece
                      ? `${record.pricePerPiece.toLocaleString("cs-CZ", { minimumFractionDigits: 2 })} Kč`
                      : "-"}
                  </td>
                  <td className="py-3 px-4 text-right text-gray-900 font-semibold">
                    {record.priceTotal
                      ? `${record.priceTotal.toLocaleString("cs-CZ", { minimumFractionDigits: 2 })} Kč`
                      : "-"}
                  </td>
                  <td className="py-3 px-4 text-center font-mono text-gray-500 text-xs">
                    {record.documentNumber || "-"}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {/* Summary */}
      <div className="bg-blue-50 rounded-lg p-4">
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
          <div className="text-center">
            <div className="text-sm font-medium text-gray-600 mb-1">
              Celkové nákupy
            </div>
            <div className="text-xl font-bold text-blue-900">
              {sortedHistory
                .reduce((sum, record) => sum + (record.amount || 0), 0)
                .toLocaleString("cs-CZ")}
            </div>
            <div className="text-xs text-gray-500">kusů celkem</div>
          </div>

          <div className="text-center">
            <div className="text-sm font-medium text-gray-600 mb-1">
              Celková hodnota
            </div>
            <div className="text-xl font-bold text-blue-900">
              {sortedHistory
                .reduce((sum, record) => sum + (record.priceTotal || 0), 0)
                .toLocaleString("cs-CZ", { minimumFractionDigits: 2 })}{" "}
              Kč
            </div>
            <div className="text-xs text-gray-500">celkové náklady</div>
          </div>

          <div className="text-center">
            <div className="text-sm font-medium text-gray-600 mb-1">
              Průměrná cena
            </div>
            <div className="text-xl font-bold text-blue-900">
              {sortedHistory.length > 0
                ? (
                    sortedHistory.reduce(
                      (sum, record) => sum + (record.pricePerPiece || 0),
                      0,
                    ) / sortedHistory.length
                  ).toLocaleString("cs-CZ", { minimumFractionDigits: 2 })
                : "0"}{" "}
              Kč
            </div>
            <div className="text-xs text-gray-500">za kus</div>
          </div>
        </div>
      </div>
    </div>
  );
};

export default PurchaseHistoryTab;
