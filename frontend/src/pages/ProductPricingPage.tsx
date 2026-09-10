import React from "react";
import { DollarSign } from "lucide-react";
import { useScreenView } from "../telemetry/useScreenView";
import PriceDivergenceReport from "../components/pricing/PriceDivergenceReport";
import { usePermissionsContext } from "../auth/PermissionsContext";

const ProductPricingPage: React.FC = () => {
  useScreenView("Catalog", "ProductPricing");
  const { hasPermission } = usePermissionsContext();

  return (
    <div className="p-6">
      <div className="flex items-center justify-between mb-4">
        <div className="flex items-center space-x-3">
          <DollarSign className="h-8 w-8 text-gray-700 dark:text-graphite-muted" />
          <div>
            <h1 className="text-2xl font-bold text-gray-900 dark:text-graphite-text">Ceny produktů</h1>
            <p className="text-sm text-gray-500 dark:text-graphite-muted">
              Porovnání maloobchodních cen mezi Shoptetem a Flexi
            </p>
          </div>
        </div>
      </div>

      <PriceDivergenceReport canWrite={hasPermission("products.catalog.write")} />
    </div>
  );
};

export default ProductPricingPage;
