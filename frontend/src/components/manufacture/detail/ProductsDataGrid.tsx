import React from "react";

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
        <div className="bg-white rounded-lg overflow-hidden border border-green-200">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-green-100">
              <tr>
                <th className="px-4 py-3 text-left text-xs font-medium text-green-800 uppercase tracking-wider">
                  Kód
                </th>
                <th className="px-4 py-3 text-left text-xs font-medium text-green-800 uppercase tracking-wider">
                  Název
                </th>
                <th className="px-4 py-3 text-left text-xs font-medium text-green-800 uppercase tracking-wider">
                  Množství
                </th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {order.products.map((product: any, index: number) => (
                <tr key={index} className="hover:bg-gray-50">
                  <td className="px-4 py-3 text-sm font-medium text-gray-900">
                    {product.productCode || "-"}
                  </td>
                  <td className="px-4 py-3 text-sm text-gray-700">
                    {product.productName || "-"}
                  </td>
                  <td className="px-4 py-3 text-sm text-gray-700">
                    {canEditFields ? (
                      <input
                        type="number"
                        value={editableProductQuantities[index] || ""}
                        onChange={(e) => onProductQuantityChange(index, e.target.value)}
                        className="w-20 px-2 py-1 border border-gray-300 rounded text-center"
                        min="0"
                        step="1"
                      />
                    ) : (
                      <div>
                        {product.actualQuantity || product.plannedQuantity || "-"}
                        {product.actualQuantity && product.plannedQuantity && product.actualQuantity !== product.plannedQuantity && (
                          <span className="text-xs text-gray-500 ml-1">
                            (plán: {product.plannedQuantity})
                          </span>
                        )}
                      </div>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <div className="bg-white rounded-lg p-6 border border-green-200">
          <p className="text-gray-500 text-center text-sm italic">
            Žádné produkty nejsou definovány
          </p>
        </div>
      )}
    </>
  );
};

export default ProductsDataGrid;