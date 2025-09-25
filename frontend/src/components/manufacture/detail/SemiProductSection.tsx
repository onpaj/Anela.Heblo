import React from "react";

interface SemiProductSectionProps {
  order: any;
  canEditFields: boolean;
  editableSemiProductQuantity: string;
  onSemiProductQuantityChange: (value: string) => void;
}

export const SemiProductSection: React.FC<SemiProductSectionProps> = ({
  order,
  canEditFields,
  editableSemiProductQuantity,
  onSemiProductQuantityChange,
}) => {
  return (
    <div className="bg-blue-50 rounded-lg p-3">
      {order.semiProduct ? (
        <div className="flex items-center">
          <div className="flex-1">
            <div className="text-sm font-medium text-gray-900 mb-1">
              {order.semiProduct.productName || "Bez názvu"}
            </div>
            <div className="text-sm text-gray-600">
              {order.semiProduct.productCode || "Bez kódu"}
            </div>
          </div>
          <div className="ml-2">
            {canEditFields ? (
              <div className="flex items-center">
                <input
                  type="number"
                  value={editableSemiProductQuantity}
                  onChange={(e) => onSemiProductQuantityChange(e.target.value)}
                  className="text-lg font-bold text-gray-700 bg-white border border-gray-300 rounded px-2 py-1 w-25 text-center"
                  min="0"
                  step="1"
                />
                <span className="text-lg font-bold text-gray-900 ml-1">g</span>
              </div>
            ) : (
              <div className="text-lg font-bold text-gray-900">
                {order.semiProduct.actualQuantity || order.semiProduct.plannedQuantity || "0"}g
                {order.semiProduct.actualQuantity && order.semiProduct.plannedQuantity && order.semiProduct.actualQuantity !== order.semiProduct.plannedQuantity && (
                  <span className="text-xs text-gray-500 ml-1">
                    (plán: {order.semiProduct.plannedQuantity}g)
                  </span>
                )}
              </div>
            )}
          </div>
        </div>
      ) : (
        <p className="text-gray-500 text-center text-sm italic">
          Polotovar není definován
        </p>
      )}
    </div>
  );
};

export default SemiProductSection;