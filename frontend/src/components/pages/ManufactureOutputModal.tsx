import React, { useState } from "react";
import { X, Calendar, Package, TrendingUp } from "lucide-react";
import {
  ManufactureOutputMonth,
  ProductContribution,
  formatMonthDisplay,
} from "../../api/hooks/useManufactureOutput";

interface ManufactureOutputModalProps {
  isOpen: boolean;
  onClose: () => void;
  monthData: ManufactureOutputMonth | null;
}

const ManufactureOutputModal: React.FC<ManufactureOutputModalProps> = ({
  isOpen,
  onClose,
  monthData,
}) => {
  const [selectedProduct, setSelectedProduct] =
    useState<ProductContribution | null>(null);

  if (!isOpen || !monthData) return null;

  const productionRecordsForProduct = selectedProduct
    ? monthData.productionDetails.filter(
        (detail) => detail.productCode === selectedProduct.productCode,
      )
    : [];

  const formatDate = (dateStr: string) => {
    return new Date(dateStr).toLocaleDateString("cs-CZ");
  };

  const formatCurrency = (amount: number) => {
    return new Intl.NumberFormat("cs-CZ", {
      style: "currency",
      currency: "CZK",
      minimumFractionDigits: 0,
      maximumFractionDigits: 2,
    }).format(amount);
  };

  return (
    <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50 p-4">
      <div className="bg-white dark:bg-graphite-surface rounded-lg shadow-xl dark:shadow-soft-dark max-w-[95vw] w-full h-[90vh] flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between p-6 border-b border-gray-200 dark:border-graphite-border">
          <div className="flex items-center space-x-3">
            <Calendar className="h-6 w-6 text-indigo-600 dark:text-graphite-accent" />
            <div>
              <h2 className="text-xl font-semibold text-gray-900 dark:text-graphite-text">
                Detaily výroby - {formatMonthDisplay(monthData.month)}
              </h2>
              <p className="text-sm text-gray-600 dark:text-graphite-muted">
                Celkový vážený výtlak: {monthData.totalOutput.toFixed(1)}
              </p>
            </div>
          </div>
          <button
            onClick={onClose}
            className="text-gray-400 dark:text-graphite-faint hover:text-gray-600 transition-colors"
          >
            <X className="h-6 w-6" />
          </button>
        </div>

        {/* Content - Two panels */}
        <div className="flex-1 flex min-h-0">
          {/* Left panel - Products summary */}
          <div className="w-1/2 border-r border-gray-200 dark:border-graphite-border flex flex-col">
            <div className="p-4 bg-gray-50 dark:bg-graphite-surface-2 border-b border-gray-200 dark:border-graphite-border">
              <h3 className="text-lg font-medium text-gray-900 dark:text-graphite-text flex items-center">
                <Package className="h-5 w-5 mr-2" />
                Produkty ({monthData.products.length})
              </h3>
              <p className="text-sm text-gray-600 dark:text-graphite-muted mt-1">
                Kliknutím na produkt zobrazíte detailní výrobní záznamy
              </p>
            </div>
            <div className="flex-1 overflow-auto">
              <table className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
                <thead className="bg-gray-50 dark:bg-graphite-surface-2 sticky top-0">
                  <tr>
                    <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                      Produkt
                    </th>
                    <th className="px-4 py-2 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                      Vyrobeno
                    </th>
                    <th className="px-4 py-2 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                      Náročnost
                    </th>
                    <th className="px-4 py-2 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                      Celková náročnost
                    </th>
                  </tr>
                </thead>
                <tbody className="bg-white dark:bg-graphite-surface divide-y divide-gray-200 dark:divide-graphite-border">
                  {monthData.products.map((product) => (
                    <tr
                      key={product.productCode}
                      className={`cursor-pointer transition-colors ${
                        selectedProduct?.productCode === product.productCode
                          ? "bg-indigo-50 dark:bg-graphite-accent/10 border-l-4 border-indigo-500 dark:border-graphite-accent"
                          : "hover:bg-gray-50 dark:hover:bg-white/5"
                      }`}
                      onClick={() => setSelectedProduct(product)}
                    >
                      <td className="px-4 py-3">
                        <div>
                          <div className="text-sm font-medium text-gray-900 dark:text-graphite-text">
                            {product.productName}
                          </div>
                          <div className="text-sm text-gray-500 dark:text-graphite-muted">
                            {product.productCode}
                          </div>
                        </div>
                      </td>
                      <td className="px-4 py-3 text-sm text-right text-gray-900 dark:text-graphite-text">
                        {product.quantity.toFixed(1)}
                      </td>
                      <td className="px-4 py-3 text-sm text-right text-gray-900 dark:text-graphite-text">
                        {product.difficulty.toFixed(1)}
                      </td>
                      <td className="px-4 py-3 text-sm text-right font-medium text-gray-900 dark:text-graphite-text">
                        {product.weightedValue.toFixed(1)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>

          {/* Right panel - Production records */}
          <div className="w-1/2 flex flex-col">
            <div className="p-4 bg-gray-50 dark:bg-graphite-surface-2 border-b border-gray-200 dark:border-graphite-border">
              <h3 className="text-lg font-medium text-gray-900 dark:text-graphite-text flex items-center">
                <TrendingUp className="h-5 w-5 mr-2" />
                Výrobní záznamy
                {selectedProduct && (
                  <span className="ml-2 text-sm font-normal text-gray-600 dark:text-graphite-muted">
                    ({productionRecordsForProduct.length})
                  </span>
                )}
              </h3>
              {selectedProduct ? (
                <p className="text-sm text-gray-600 dark:text-graphite-muted mt-1">
                  {selectedProduct.productName}
                </p>
              ) : (
                <p className="text-sm text-gray-600 dark:text-graphite-muted mt-1">
                  Vyberte produkt vlevo pro zobrazení detailů
                </p>
              )}
            </div>
            <div className="flex-1 overflow-auto">
              {selectedProduct ? (
                <table className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
                  <thead className="bg-gray-50 dark:bg-graphite-surface-2 sticky top-0">
                    <tr>
                      <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                        Datum
                      </th>
                      <th className="px-4 py-2 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                        Množství
                      </th>
                      <th className="px-4 py-2 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                        Cena/ks
                      </th>
                      <th className="px-4 py-2 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                        Celkem
                      </th>
                      <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                        Doklad
                      </th>
                    </tr>
                  </thead>
                  <tbody className="bg-white dark:bg-graphite-surface divide-y divide-gray-200 dark:divide-graphite-border">
                    {productionRecordsForProduct.map((record, index) => (
                      <tr key={index} className="hover:bg-gray-50 dark:hover:bg-white/5">
                        <td className="px-4 py-2 text-sm text-gray-900 dark:text-graphite-text">
                          {formatDate(record.date)}
                        </td>
                        <td className="px-4 py-2 text-sm text-right text-gray-900 dark:text-graphite-text">
                          {record.amount.toFixed(1)}
                        </td>
                        <td className="px-4 py-2 text-sm text-right text-gray-900 dark:text-graphite-text">
                          {formatCurrency(record.pricePerPiece)}
                        </td>
                        <td className="px-4 py-2 text-sm text-right font-medium text-gray-900 dark:text-graphite-text">
                          {formatCurrency(record.priceTotal)}
                        </td>
                        <td className="px-4 py-2 text-sm text-gray-900 dark:text-graphite-text">
                          {record.documentNumber}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              ) : (
                <div className="flex items-center justify-center h-full text-gray-500 dark:text-graphite-muted">
                  <div className="text-center">
                    <Package className="h-12 w-12 mx-auto mb-3 text-gray-300 dark:text-graphite-faint" />
                    <p>Vyberte produkt pro zobrazení výrobních záznamů</p>
                  </div>
                </div>
              )}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};

export default ManufactureOutputModal;
