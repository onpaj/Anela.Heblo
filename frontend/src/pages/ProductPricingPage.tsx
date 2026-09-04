import React, { useState } from "react";
import { DollarSign, Loader2, AlertCircle, RefreshCw } from "lucide-react";
import { useProductPrices, useTriggerPriceSync } from "../api/hooks/useProductPricing";
import { usePermissionsContext } from "../auth/PermissionsContext";
import { useScreenView } from "../telemetry/useScreenView";
import ProductPriceGrid from "../components/pricing/ProductPriceGrid";
import PriceDivergenceReport from "../components/pricing/PriceDivergenceReport";

const WRITE_PERMISSION = "products.catalog.write";

type Tab = "prices" | "divergence";

const TABS: { id: Tab; label: string }[] = [
  { id: "prices", label: "Ceny produktů" },
  { id: "divergence", label: "Kontrola cen" },
];

const ProductPricingPage: React.FC = () => {
  useScreenView("Catalog", "ProductPricing");

  const { hasPermission } = usePermissionsContext();
  const canWrite = hasPermission(WRITE_PERMISSION);
  const [activeTab, setActiveTab] = useState<Tab>("prices");

  const { data, isLoading, error } = useProductPrices();
  const triggerSync = useTriggerPriceSync();

  const prices = data ?? [];

  const handleSync = () => {
    triggerSync.mutate();
  };

  if (activeTab === "prices" && isLoading) {
    return (
      <div className="p-6 flex items-center justify-center h-64">
        <div className="flex items-center space-x-2">
          <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
          <div className="text-gray-500 dark:text-graphite-muted">Načítání cen produktů...</div>
        </div>
      </div>
    );
  }

  if (activeTab === "prices" && error) {
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
      <div className="flex items-center justify-between mb-4">
        <div className="flex items-center space-x-3">
          <DollarSign className="h-8 w-8 text-gray-700 dark:text-graphite-muted" />
          <div>
            <h1 className="text-2xl font-bold text-gray-900 dark:text-graphite-text">Ceny produktů</h1>
            <p className="text-sm text-gray-500 dark:text-graphite-muted">
              Správa maloobchodních cen a synchronizace se Shoptetem a Flexi
            </p>
          </div>
        </div>

        {activeTab === "prices" && canWrite && (
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

      <div className="border-b border-gray-200 dark:border-graphite-border mb-6">
        <nav className="flex gap-6" aria-label="Tabs">
          {TABS.map((tab) => (
            <button
              key={tab.id}
              onClick={() => setActiveTab(tab.id)}
              className={`py-2 text-sm font-medium border-b-2 transition-colors ${
                activeTab === tab.id
                  ? "border-blue-600 text-blue-600 dark:text-graphite-accent dark:border-graphite-accent"
                  : "border-transparent text-gray-500 hover:text-gray-700 dark:text-graphite-muted"
              }`}
            >
              {tab.label}
            </button>
          ))}
        </nav>
      </div>

      {activeTab === "prices" && (
        <>
          {triggerSync.isError && (
            <div
              data-testid="trigger-sync-error"
              role="alert"
              className="mb-4 px-4 py-3 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-900/40 rounded-md text-sm text-red-800 dark:text-red-300"
            >
              {triggerSync.error?.message ?? "Synchronizaci se nepodařilo spustit."}
            </div>
          )}

          {triggerSync.isSuccess && triggerSync.data && (
            <div className="mb-4 px-4 py-3 bg-blue-50 dark:bg-blue-900/20 border border-blue-200 dark:border-blue-900/40 rounded-md text-sm text-blue-800 dark:text-blue-300">
              Synchronizace dokončena: {triggerSync.data.pushed} odesláno, {triggerSync.data.conflicts} konfliktů,{" "}
              {triggerSync.data.failed} chyb, {triggerSync.data.seeded} založeno, {triggerSync.data.unchanged} beze
              změny.
            </div>
          )}

          <ProductPriceGrid prices={prices} canWrite={canWrite} />
        </>
      )}

      {activeTab === "divergence" && <PriceDivergenceReport />}
    </div>
  );
};

export default ProductPricingPage;
