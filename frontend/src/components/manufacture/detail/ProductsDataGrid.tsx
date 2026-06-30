import React from "react";
import { ManufactureType } from "../../../api/generated/api-client";

interface ProductsDataGridProps {
  order: any;
  canEditFields: boolean;
  editableProductQuantities: Record<number, string>;
  onProductQuantityChange: (index: number, value: string) => void;
}

export const ProductsDataGrid: React.FC<ProductsDataGridProps> = ({
  order,
  canEditFields,
  editableProductQuantities,
  onProductQuantityChange,
}) => {
  return (
    <>
      {order.products && order.products.length > 0 ? (
        <div className="bg-white dark:bg-graphite-surface rounded-lg overflow-hidden border border-green-200 dark:border-emerald-900/40">
          <table className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
            <thead className="bg-green-100 dark:bg-emerald-900/30">
              <tr>
                <th className="px-4 py-3 text-left text-xs font-medium text-green-800 dark:text-emerald-300 uppercase tracking-wider">
                  Kód
                </th>
                <th className="px-4 py-3 text-left text-xs font-medium text-green-800 dark:text-emerald-300 uppercase tracking-wider">
                  Název
                </th>
                <th className="px-4 py-3 text-left text-xs font-medium text-green-800 dark:text-emerald-300 uppercase tracking-wider">
                  Množství
                </th>
              </tr>
            </thead>
            <tbody className="bg-white dark:bg-graphite-surface divide-y divide-gray-200 dark:divide-graphite-border">
              {order.products.map((product: any, index: number) => {
                // Direct semiproduct output ("Přímý výstup") only exists for MultiPhase orders.
                // For SinglePhase the semiproduct is a placeholder pointing at the first product,
                // so this sentinel would wrongly tag the real product as a bulk/grams row.
                const isDirectRow = order.manufactureType !== ManufactureType.SinglePhase &&
                  product.productCode && order.semiProduct?.productCode &&
                  product.productCode === order.semiProduct.productCode;
                return (
                  <tr key={index} className={isDirectRow ? "bg-amber-50 hover:bg-amber-100 dark:bg-amber-900/20 dark:hover:bg-amber-900/30" : "hover:bg-gray-50 dark:hover:bg-white/5"}>
                    <td className="px-4 py-3 text-sm font-medium text-gray-900 dark:text-graphite-text">
                      {product.productCode || "-"}
                    </td>
                    <td className="px-4 py-3 text-sm text-gray-700 dark:text-graphite-muted">
                      <div className="flex items-center gap-2">
                        {product.productName || "-"}
                        {isDirectRow && (
                          <span className="inline-flex items-center px-1.5 py-0.5 rounded text-xs font-medium bg-amber-200 text-amber-800 dark:bg-amber-900/30 dark:text-amber-300">
                            Přímý výstup
                          </span>
                        )}
                      </div>
                    </td>
                    <td className="px-4 py-3 text-sm text-gray-700 dark:text-graphite-muted">
                      {canEditFields ? (
                        <input
                          type="number"
                          value={editableProductQuantities[index] || ""}
                          onChange={(e) => onProductQuantityChange(index, e.target.value)}
                          className="w-20 px-2 py-1 border border-gray-300 rounded text-center dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text dark:placeholder-graphite-faint"
                          min="0"
                          step={isDirectRow ? "0.1" : "1"}
                        />
                      ) : (
                        <div>
                          {(() => {
                            const hasActual = product.actualQuantity !== null && product.actualQuantity !== undefined;
                            const hasPlanned = product.plannedQuantity !== null && product.plannedQuantity !== undefined;

                            if (hasActual && hasPlanned && product.actualQuantity !== product.plannedQuantity) {
                              return (
                                <>
                                  {product.actualQuantity}{isDirectRow ? "g" : ""}
                                  <span className="text-xs text-gray-500 dark:text-graphite-muted ml-1">
                                    (plán: {product.plannedQuantity}{isDirectRow ? "g" : ""})
                                  </span>
                                </>
                              );
                            } else if (hasActual) {
                              return <>{product.actualQuantity}{isDirectRow ? "g" : ""}</>;
                            } else if (hasPlanned) {
                              return <>{product.plannedQuantity}{isDirectRow ? "g" : ""}</>;
                            } else {
                              return "-";
                            }
                          })()}
                        </div>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      ) : (
        <div className="bg-white dark:bg-graphite-surface rounded-lg p-6 border border-green-200 dark:border-emerald-900/40">
          <p className="text-gray-500 dark:text-graphite-muted text-center text-sm italic">
            Žádné produkty nejsou definovány
          </p>
        </div>
      )}
    </>
  );
};

export default ProductsDataGrid;