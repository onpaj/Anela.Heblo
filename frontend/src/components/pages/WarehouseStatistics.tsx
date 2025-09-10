import React from "react";
import { useWarehouseStatistics } from "../../api/hooks/useWarehouseStatistics";
import { Package, Weight, Gauge, Hash, Clock, AlertTriangle } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "../ui/card";

const WarehouseStatistics: React.FC = () => {
  const { data: statistics, isLoading, error } = useWarehouseStatistics();

  if (isLoading) {
    return (
      <div className="container mx-auto p-6 max-w-7xl">
        <div className="flex justify-center items-center py-12">
          <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-blue"></div>
          <span className="ml-2 text-neutral-gray">Načítám statistiky skladu...</span>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="container mx-auto p-6 max-w-7xl">
        <div className="flex justify-center items-center py-12">
          <div className="text-center">
            <AlertTriangle className="h-12 w-12 text-red-500 mx-auto mb-4" />
            <h3 className="text-lg font-medium text-neutral-slate mb-2">
              Chyba při načítání statistik
            </h3>
            <p className="text-neutral-gray">
              {error instanceof Error ? error.message : "Neznámá chyba"}
            </p>
          </div>
        </div>
      </div>
    );
  }

  if (!statistics) {
    return (
      <div className="container mx-auto p-6 max-w-7xl">
        <div className="text-center py-12">
          <Package className="h-12 w-12 text-neutral-gray mx-auto mb-4" />
          <h3 className="text-lg font-medium text-neutral-slate">
            Žádné statistiky skladu
          </h3>
        </div>
      </div>
    );
  }

  const formatNumber = (value: number) => {
    return new Intl.NumberFormat("cs-CZ").format(value);
  };

  const formatWeight = (weight: number) => {
    if (weight >= 1000) {
      return `${(weight / 1000).toFixed(1)} t`;
    }
    return `${weight.toFixed(1)} kg`;
  };

  const formatPercentage = (percentage: number) => {
    return `${percentage.toFixed(1)}%`;
  };

  const getUtilizationColor = (percentage: number) => {
    if (percentage > 100) return "text-red-600";
    if (percentage > 80) return "text-yellow-600";
    return "text-green-600";
  };

  const getUtilizationBgColor = (percentage: number) => {
    if (percentage > 100) return "bg-red-100";
    if (percentage > 80) return "bg-yellow-100";
    return "bg-green-100";
  };

  const getProgressBarColor = (percentage: number) => {
    if (percentage > 100) return "bg-red-500";
    if (percentage > 80) return "bg-yellow-500";
    return "bg-green-500";
  };

  const lastUpdated = statistics.lastUpdated?.toLocaleString("cs-CZ") ?? "Neznámé";

  return (
    <div className="container mx-auto p-6 max-w-7xl">
      {/* Page Header */}
      <div className="flex justify-between items-center mb-6">
        <div>
          <h1 className="text-3xl font-bold text-neutral-slate">
            Statistiky skladu
          </h1>
          <p className="text-neutral-gray mt-1">
            Přehled stavu a naplněnosti skladu
          </p>
        </div>
        <div className="flex items-center text-sm text-neutral-gray">
          <Clock className="h-4 w-4 mr-2" />
          Aktualizováno: {lastUpdated}
        </div>
      </div>

      {/* Statistics Cards */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6 mb-8">
        {/* Total Quantity */}
        <Card>
          <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
            <CardTitle className="text-sm font-medium text-neutral-gray">
              Celkové množství kusů
            </CardTitle>
            <Hash className="h-4 w-4 text-neutral-gray" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold text-neutral-slate">
              {formatNumber(statistics.totalQuantity ?? 0)}
            </div>
            <p className="text-xs text-neutral-gray">
              kusů na skladě (eshop)
            </p>
          </CardContent>
        </Card>

        {/* Total Weight */}
        <Card>
          <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
            <CardTitle className="text-sm font-medium text-neutral-gray">
              Celková hmotnost
            </CardTitle>
            <Weight className="h-4 w-4 text-neutral-gray" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold text-neutral-slate">
              {formatWeight(statistics.totalWeight ?? 0)}
            </div>
            <p className="text-xs text-neutral-gray">
              celková hmotnost skladových zásob
            </p>
          </CardContent>
        </Card>

        {/* Warehouse Utilization */}
        <Card className={getUtilizationBgColor(statistics.warehouseUtilizationPercentage ?? 0)}>
          <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
            <CardTitle className="text-sm font-medium text-neutral-gray">
              Naplněnost skladu
            </CardTitle>
            <Gauge className="h-4 w-4 text-neutral-gray" />
          </CardHeader>
          <CardContent>
            <div className={`text-2xl font-bold ${getUtilizationColor(statistics.warehouseUtilizationPercentage ?? 0)}`}>
              {formatPercentage(statistics.warehouseUtilizationPercentage ?? 0)}
            </div>
            <p className="text-xs text-neutral-gray">
              kapacita {formatWeight(statistics.warehouseCapacityKg ?? 0)}
            </p>
          </CardContent>
        </Card>

        {/* Total Product Count */}
        <Card>
          <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
            <CardTitle className="text-sm font-medium text-neutral-gray">
              Počet produktů
            </CardTitle>
            <Package className="h-4 w-4 text-neutral-gray" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold text-neutral-slate">
              {formatNumber(statistics.totalProductCount ?? 0)}
            </div>
            <p className="text-xs text-neutral-gray">
              různých produktů v katalogu
            </p>
          </CardContent>
        </Card>
      </div>

      {/* Detailed Utilization Card */}
      <Card className="mb-8">
        <CardHeader>
          <CardTitle className="text-lg font-semibold text-neutral-slate">
            Detail naplněnosti skladu
          </CardTitle>
        </CardHeader>
        <CardContent>
          <div className="space-y-4">
            {/* Capacity Bar */}
            <div>
              <div className="flex justify-between items-center mb-2">
                <span className="text-sm font-medium text-neutral-slate">
                  Využití kapacity
                </span>
                <span className={`text-sm font-semibold ${getUtilizationColor(statistics.warehouseUtilizationPercentage ?? 0)}`}>
                  {formatPercentage(statistics.warehouseUtilizationPercentage ?? 0)}
                </span>
              </div>
              <div className="w-full bg-gray-200 rounded-full h-3">
                <div
                  className={`h-3 rounded-full transition-all duration-500 ${getProgressBarColor(statistics.warehouseUtilizationPercentage ?? 0)}`}
                  style={{
                    width: `${Math.min(statistics.warehouseUtilizationPercentage ?? 0, 100)}%`,
                  }}
                ></div>
              </div>
              {(statistics.warehouseUtilizationPercentage ?? 0) > 100 && (
                <div className="mt-2 text-sm text-red-600 font-medium">
                  ⚠️ Sklad překračuje maximální kapacitu!
                </div>
              )}
            </div>

            {/* Weight Details */}
            <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
              <div className="text-center p-4 bg-gray-50 rounded-lg">
                <div className="text-lg font-semibold text-neutral-slate">
                  {formatWeight(statistics.totalWeight ?? 0)}
                </div>
                <div className="text-xs text-neutral-gray">Aktuální hmotnost</div>
              </div>
              <div className="text-center p-4 bg-gray-50 rounded-lg">
                <div className="text-lg font-semibold text-neutral-slate">
                  {formatWeight(statistics.warehouseCapacityKg ?? 0)}
                </div>
                <div className="text-xs text-neutral-gray">Maximální kapacita</div>
              </div>
              <div className="text-center p-4 bg-gray-50 rounded-lg">
                <div className="text-lg font-semibold text-neutral-slate">
                  {formatWeight((statistics.warehouseCapacityKg ?? 0) - (statistics.totalWeight ?? 0))}
                </div>
                <div className="text-xs text-neutral-gray">
                  {(statistics.totalWeight ?? 0) > (statistics.warehouseCapacityKg ?? 0)
                    ? "Překročeno o" 
                    : "Zbývající kapacita"}
                </div>
              </div>
            </div>
          </div>
        </CardContent>
      </Card>
    </div>
  );
};

export default WarehouseStatistics;