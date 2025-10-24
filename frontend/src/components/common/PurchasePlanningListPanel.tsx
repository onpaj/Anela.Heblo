import React, { useState } from "react";
import { X, ShoppingCart, Package } from "lucide-react";
import { usePurchasePlanningList } from "../../contexts/PurchasePlanningListContext";

interface PurchasePlanningListPanelProps {
  isVisible: boolean;
  onItemClick?: (item: { productCode: string; productName: string; supplier: string }) => void;
}

const PurchasePlanningListPanel: React.FC<PurchasePlanningListPanelProps> = ({
  isVisible,
  onItemClick,
}) => {
  const { items, removeItem, hasItems } = usePurchasePlanningList();
  const [isHovered, setIsHovered] = useState(false);

  // Don't render if no items
  if (!hasItems) {
    return null;
  }

  // Show panel on hover when there are items
  const showPanel = isVisible || isHovered;

  return (
    <div
      className="fixed right-0 top-20 z-40"
      onMouseEnter={() => setIsHovered(true)}
      onMouseLeave={() => setIsHovered(false)}
    >
      {/* Panel */}
      <div
        className={`bg-white shadow-xl border-l border-gray-200 transition-transform duration-300 ease-in-out ${
          showPanel ? "translate-x-0" : "translate-x-[calc(100%-10px)]"
        }`}
        style={{ width: "320px", maxHeight: "80vh" }}
      >
        {/* Header */}
        <div className="flex items-center justify-between p-3 border-b border-gray-200 bg-blue-50">
          <div className="flex items-center space-x-2">
            <ShoppingCart className="h-4 w-4 text-blue-600" />
            <span className="text-sm font-medium text-gray-800">
              Seznam k objednání
            </span>
            <span className="text-xs text-gray-500">
              ({items.length}/{20})
            </span>
          </div>
        </div>

        {/* Content */}
        <div className="overflow-y-auto" style={{ maxHeight: "calc(80vh - 3rem)" }}>
          {items.length === 0 ? (
            <div className="p-4 text-center text-gray-500">
              <Package className="h-8 w-8 mx-auto mb-2 text-gray-400" />
              <p className="text-sm">Seznam je prázdný</p>
            </div>
          ) : (
            <div className="p-2 space-y-1">
              {items.map((item) => (
                <div
                  key={item.productCode}
                  className="flex items-center justify-between p-2 rounded border border-gray-100 hover:bg-gray-50 group transition-colors"
                >
                  <div
                    className="flex-1 min-w-0 cursor-pointer"
                    onClick={() => onItemClick?.(item)}
                  >
                    <div className="text-xs font-medium text-gray-900 truncate">
                      {item.productName}
                    </div>
                    <div className="text-xs text-gray-500">{item.productCode}</div>
                    <div className="text-xs text-blue-600 truncate">
                      {item.supplier}
                    </div>
                  </div>
                  <button
                    onClick={() => removeItem(item.productCode)}
                    className="ml-2 p-1 text-gray-400 hover:text-red-600 opacity-0 group-hover:opacity-100 transition-opacity"
                    title="Odebrat ze seznamu"
                  >
                    <X className="h-3 w-3" />
                  </button>
                </div>
              ))}
            </div>
          )}
        </div>

        {/* Footer with instruction */}
        <div className="p-2 bg-blue-50 border-t border-gray-200">
          <p className="text-xs text-gray-600 text-center">
            Klikněte na materiál pro vytvoření objednávky
          </p>
        </div>
      </div>

      {/* Hover trigger - thin strip when panel is partially visible */}
      {!showPanel && (
        <div
          className="absolute right-0 top-0 w-2 h-full bg-blue-500 opacity-70 cursor-pointer"
          title="Seznam k objednání"
        />
      )}
    </div>
  );
};

export default PurchasePlanningListPanel;