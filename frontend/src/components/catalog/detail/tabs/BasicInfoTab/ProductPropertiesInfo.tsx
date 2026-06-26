import React from "react";
import { Layers, Settings } from "lucide-react";
import { CatalogItemDto } from "../../../../../api/hooks/useCatalog";
import { Cooling } from "../../../../../api/generated/api-client";

interface ProductPropertiesInfoProps {
  item: CatalogItemDto;
  onManufactureDifficultyClick: () => void;
}

const COOLING_LABELS: Record<Cooling, { label: string; className: string }> = {
  [Cooling.None]: {
    label: "Bez chlazení",
    className: "text-gray-400 dark:text-graphite-faint",
  },
  [Cooling.L1]: {
    label: "L1",
    className: "text-blue-600 dark:text-blue-400 font-semibold",
  },
  [Cooling.L2]: {
    label: "L2",
    className: "text-indigo-700 dark:text-graphite-accent font-semibold",
  },
};

const ProductPropertiesInfo: React.FC<ProductPropertiesInfoProps> = ({
  item,
  onManufactureDifficultyClick,
}) => {
  const cooling = item.properties?.cooling ?? Cooling.None;
  const coolingDisplay = COOLING_LABELS[cooling] ?? COOLING_LABELS[Cooling.None];

  return (
    <div className="space-y-4">
      <h3 className="text-lg font-medium text-gray-900 dark:text-graphite-text flex items-center">
        <Layers className="h-5 w-5 mr-2 text-gray-500 dark:text-graphite-muted" />
        Vlastnosti produktu
      </h3>

      <div className="bg-gray-50 dark:bg-graphite-surface-2 rounded-lg p-4">
        <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
          <div className="text-center">
            <span className="text-xs font-medium text-gray-600 dark:text-graphite-muted block mb-1">
              Optimální zásoby (dny)
            </span>
            <span className="text-lg font-semibold text-gray-900 dark:text-graphite-text">
              {item.properties?.optimalStockDaysSetup || "-"}
            </span>
          </div>

          <div className="text-center">
            <span className="text-xs font-medium text-gray-600 dark:text-graphite-muted block mb-1">
              Min. zásoba
            </span>
            <span className="text-lg font-semibold text-gray-900 dark:text-graphite-text">
              {item.properties?.stockMinSetup || "-"}
            </span>
          </div>

          <div className="text-center">
            <span className="text-xs font-medium text-gray-600 dark:text-graphite-muted block mb-1">
              Velikost šarže
            </span>
            <span className="text-lg font-semibold text-gray-900 dark:text-graphite-text">
              {item.properties?.batchSize || "-"}
            </span>
          </div>

          <div className="text-center">
            <span className="text-xs font-medium text-gray-600 dark:text-graphite-muted block mb-1">
              Náročnost výroby
            </span>
            <button
              onClick={onManufactureDifficultyClick}
              className="text-lg font-semibold text-indigo-600 dark:text-graphite-accent hover:text-indigo-700 hover:underline focus:outline-none focus:underline flex items-center space-x-1 mx-auto"
              title="Klikněte pro správu náročnosti výroby"
            >
              <span>
                {item.manufactureDifficulty && item.manufactureDifficulty > 0
                  ? item.manufactureDifficulty.toFixed(2)
                  : "Nenastaveno"}
              </span>
              <Settings className="h-3 w-3" />
            </button>
          </div>
        </div>

        <div className="mt-4 grid grid-cols-2 lg:grid-cols-4 gap-4">
          <div className="text-center">
            <span className="text-xs font-medium text-gray-600 dark:text-graphite-muted block mb-1">
              Chlazení
            </span>
            <span className={`text-lg ${coolingDisplay.className}`}>
              {coolingDisplay.label}
            </span>
          </div>
        </div>
      </div>
    </div>
  );
};

export default ProductPropertiesInfo;
