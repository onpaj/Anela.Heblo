import React from "react";
import { DollarSign, Loader2, AlertCircle, RefreshCw } from "lucide-react";
import { useProductPrices, useTriggerPriceSync } from "../api/hooks/useProductPricing";
import { usePermissionsContext } from "../auth/PermissionsContext";
import { useScreenView } from "../telemetry/useScreenView";
import ProductPriceGrid from "../components/pricing/ProductPriceGrid";

const WRITE_PERMISSION = "products.catalog.write";

const ProductPricingPage: React.FC = () => {
  useScreenView("Catalog", "ProductPricing");

  const { hasPermission } = usePermissionsContext();
  const canWrite = hasPermission(WRITE_PERMISSION);

  const { data, isLoading, error } = useProductPrices();
  const triggerSync = useTriggerPriceSync();

  const prices = data ?? [];

  const handleSync = () => {
    triggerSync.mutate();
  };

  if (isLoading) {
    return (
      <div className="p-6 flex items-center justify-center h-64">
        <div className="flex items-center space-x-2">
          <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
          <div className="text-gray-500 dark:text-graphite-muted">Načítání cen produktů...</div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="p-6 flex items-center justify-center h-64">
        <div className="flex items-center space-x-2 text-red-600 dark:text-red-400">
          <AlertCircle className="h-5 w-5" />
          <div>Chyba při načítání cen: {(error as Error).message}</div>
        </div>
      </div>
    );
  }

  return (
    <div className="p-6">
      <div className="flex items-center justify-between mb-6">
        <div className="flex items-center space-x-3">
          <DollarSign className="h-8 w-8 text-gray-700 dark:text-graphite-muted" />
          <div>
            <h1 className="text-2xl font-bold text-gray-900 dark:text-graphite-text">Ceny produktů</h1>
            <p className="text-sm text-gray-500 dark:text-graphite-muted">
              Správa maloobchodních cen a synchronizace se Shoptetem a Flexi
            </p>
          </div>
        </div>

        {canWrite && (
          <button
            onClick={handleSync}
            disabled={triggerSync.isPending}
            className="inline-flex items-center px-4 py-2 bg-indigo-600 hover:bg-indigo-700 text-white text-sm font-medium rounded-md transition-colors duration-200 disabled:opacity-50"
          >
            <RefreshCw className={`h-4 w-4 mr-2 ${triggerSync.isPending ? "animate-spin" : ""}`} />
            Synchronizovat
          </button>
        )}
      </div>

      {triggerSync.isSuccess && triggerSync.data && (
        <div className="mb-4 px-4 py-3 bg-blue-50 dark:bg-blue-900/20 border border-blue-200 dark:border-blue-900/40 rounded-md text-sm text-blue-800 dark:text-blue-300">
          Synchronizace dokončena: {triggerSync.data.pushed} odesláno, {triggerSync.data.conflicts} konfliktů,{" "}
          {triggerSync.data.failed} chyb, {triggerSync.data.seeded} založeno, {triggerSync.data.unchanged} beze
          změny.
        </div>
      )}

      <ProductPriceGrid prices={prices} />
    </div>
  );
};

export default ProductPricingPage;
