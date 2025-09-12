import React, { useState } from "react";
import {
  Calculator,
  Package,
  Settings,
  AlertCircle,
} from "lucide-react";
import {
  useBatchPlanningMutation,
  CalculateBatchPlanRequest,
  BatchPlanControlMode,
} from "../../api/hooks/useBatchPlanning";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";
import CatalogAutocomplete from "../common/CatalogAutocomplete";
import { CatalogItemDto, ProductType } from "../../api/generated/api-client";

const BatchPlanningCalculator: React.FC = () => {
  // Selected semiproduct state
  const [selectedSemiproduct, setSelectedSemiproduct] = useState<CatalogItemDto | null>(null);
  
  // Form state
  const [mmqMultiplier, setMmqMultiplier] = useState<number>(1.0);
  const [totalBatchSize, setTotalBatchSize] = useState<number>(0);
  const [inputMode, setInputMode] = useState<'multiplier' | 'total'>('multiplier'); // Which field user is editing
  
  // Mock MMQ for selected product (in real app, this would come from API)
  const getProductMMQ = (product: CatalogItemDto | null): number => {
    // Mock data - in real implementation this would come from the product data
    return product ? 100 : 0;
  };

  const productMMQ = getProductMMQ(selectedSemiproduct);
  
  // Calculate derived values
  const calculatedBatchSize = inputMode === 'multiplier' ? productMMQ * mmqMultiplier : totalBatchSize;
  const calculatedMultiplier = inputMode === 'total' && productMMQ > 0 ? totalBatchSize / productMMQ : mmqMultiplier;

  // Mutation
  const batchPlanMutation = useBatchPlanningMutation();

  // Get API response data
  const response = batchPlanMutation.data;
  const apiProductSizes = response && response.success ? response.productSizes : [];
  
  // Convert API data to grid format
  const productGridData = apiProductSizes.map((product) => ({
    productCode: product.productCode,
    productName: product.productName,
    netWeight: product.productSize ? parseFloat(product.productSize.replace(/[^\d.]/g, '')) : 0, // Extract numeric value from size
    stockEshopTotal: product.currentStock,
    dailySales: product.currentStock / (product.currentDaysCoverage || 1), // Calculate daily sales from stock and coverage
    currentCoverage: product.currentDaysCoverage,
    recommendedQuantity: product.recommendedUnitsToProduceHumanReadable,
    futureCoverage: product.futureDaysCoverage
  }));

  // Show loading state or actual data
  const displayProductSizes = batchPlanMutation.isPending && selectedSemiproduct 
    ? [] // Empty while loading
    : productGridData;

  const handleMultiplierChange = (value: number) => {
    setMmqMultiplier(value);
    setTotalBatchSize(productMMQ * value);
    setInputMode('multiplier');
    
    // Recalculate when multiplier changes
    if (selectedSemiproduct?.productCode) {
      calculateBatchPlan(selectedSemiproduct.productCode, value);
    }
  };

  const handleTotalBatchSizeChange = (value: number) => {
    setTotalBatchSize(value);
    const newMultiplier = productMMQ > 0 ? value / productMMQ : 1.0;
    setMmqMultiplier(newMultiplier);
    setInputMode('total');
    
    // Recalculate when total batch size changes
    if (selectedSemiproduct?.productCode) {
      calculateBatchPlan(selectedSemiproduct.productCode, newMultiplier);
    }
  };

  const calculateBatchPlan = (semiproductCode: string, multiplier: number) => {
    const request: CalculateBatchPlanRequest = {
      semiproductCode: semiproductCode,
      controlMode: BatchPlanControlMode.MmqMultiplier,
      mmqMultiplier: multiplier,
    };
    
    batchPlanMutation.mutate(request);
  };

  const handleSemiproductSelect = (product: CatalogItemDto | null) => {
    setSelectedSemiproduct(product);
    // Reset form when changing product
    setMmqMultiplier(1.0);
    setTotalBatchSize(product ? getProductMMQ(product) * 1.0 : 0);
    setInputMode('multiplier');
    
    // Call API to calculate batch plan
    if (product?.productCode) {
      calculateBatchPlan(product.productCode, 1.0);
    }
  };

  return (
    <div className={`flex flex-col ${PAGE_CONTAINER_HEIGHT} bg-gray-50`}>
      {/* Header */}
      <div className="bg-white border-b px-6 py-4 flex-shrink-0">
        <div className="flex items-center space-x-3">
          <Calculator className="w-6 h-6 text-indigo-600" />
          <h1 className="text-xl font-semibold text-gray-900">
            Plánovač výrobních dávek
          </h1>
        </div>
        <p className="text-sm text-gray-600 mt-1">
          Optimalizace distribuce polotovaru napříč různými velikostmi produktů
        </p>
      </div>

      {/* Main Content */}
      <div className="flex-1 overflow-y-auto">
        <div className="p-6">
          <div className="space-y-6">
            {/* Semiproduct Selection */}
            <div className="bg-white rounded-lg shadow-sm border p-6 relative z-10">
              <h2 className="text-lg font-medium text-gray-900 mb-4 flex items-center">
                <Package className="w-5 h-5 text-gray-500 mr-2" />
                Výběr polotovaru
              </h2>
              
              <div className="relative">
                <label className="block text-sm font-medium text-gray-700 mb-2">
                  Polotovar <span className="text-red-500">*</span>
                </label>
                <div className="relative z-50">
                  <CatalogAutocomplete
                    value={selectedSemiproduct}
                    onSelect={handleSemiproductSelect}
                    placeholder="Vyberte polotovar..."
                    productTypes={[ProductType.SemiProduct]}
                    size="md"
                    clearable={true}
                  />
                </div>
              </div>
            </div>

            {/* Error Display */}
            {batchPlanMutation.isError && (
              <div className="bg-red-50 border border-red-200 rounded-lg p-4">
                <div className="flex items-center">
                  <AlertCircle className="w-5 h-5 text-red-500 mr-2" />
                  <h3 className="text-sm font-medium text-red-800">Chyba při načítání dat</h3>
                </div>
                <p className="text-sm text-red-700 mt-1">
                  {batchPlanMutation.error?.message || "Došlo k chybě při načítání plánu výroby."}
                </p>
              </div>
            )}

            {/* Configuration and Results - Only show if semiproduct is selected */}
            {selectedSemiproduct && (
              <>
                {/* Batch Configuration */}
                <div className="bg-white rounded-lg shadow-sm border p-6">
                  <h3 className="text-lg font-medium text-gray-900 mb-4 flex items-center">
                    <Settings className="w-5 h-5 text-gray-500 mr-2" />
                    Nastavení dávky
                  </h3>
                  
                  <div className="grid grid-cols-1 md:grid-cols-3 gap-4 items-end">
                    <div>
                      <label className="block text-sm font-medium text-gray-700 mb-1">
                        MMQ
                      </label>
                      <input
                        type="number"
                        value={productMMQ}
                        readOnly
                        className="w-full px-3 py-2 border border-gray-300 rounded-md bg-gray-50 text-gray-600"
                      />
                      <p className="text-xs text-gray-500 mt-1">ml</p>
                    </div>
                    
                    <div>
                      <label className="block text-sm font-medium text-gray-700 mb-1">
                        <input
                          type="radio"
                          checked={inputMode === 'multiplier'}
                          onChange={() => setInputMode('multiplier')}
                          className="mr-2"
                        />
                        Multiplikátor
                      </label>
                      <input
                        type="number"
                        step="0.1"
                        min="0.1"
                        value={calculatedMultiplier.toFixed(2)}
                        onChange={(e) => handleMultiplierChange(Number(e.target.value))}
                        readOnly={inputMode !== 'multiplier'}
                        className={`w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500 ${
                          inputMode !== 'multiplier' ? 'bg-gray-50 text-gray-600' : ''
                        }`}
                      />
                      <p className="text-xs text-gray-500 mt-1">×</p>
                    </div>
                    
                    <div>
                      <label className="block text-sm font-medium text-gray-700 mb-1">
                        <input
                          type="radio"
                          checked={inputMode === 'total'}
                          onChange={() => setInputMode('total')}
                          className="mr-2"
                        />
                        Celkové množství
                      </label>
                      <input
                        type="number"
                        min="0"
                        value={calculatedBatchSize.toFixed(0)}
                        onChange={(e) => handleTotalBatchSizeChange(Number(e.target.value))}
                        readOnly={inputMode !== 'total'}
                        className={`w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500 ${
                          inputMode !== 'total' ? 'bg-indigo-50 text-indigo-800 font-medium' : ''
                        }`}
                      />
                      <p className="text-xs text-gray-500 mt-1">ml</p>
                    </div>
                  </div>
                </div>

                {/* Product Grid */}
                <div className="bg-white rounded-lg shadow-sm border overflow-hidden">
                  <div className="px-6 py-4 border-b">
                    <h3 className="text-lg font-medium text-gray-900 flex items-center">
                      <Package className="w-5 h-5 text-green-500 mr-2" />
                      Velikosti produktů
                    </h3>
                    <p className="text-sm text-gray-600 mt-1">
                      Produkty vyráběné z polotovaru {selectedSemiproduct.productName}
                    </p>
                  </div>
                  
                  <div className="overflow-x-auto">
                    <table className="min-w-full divide-y divide-gray-200">
                      <thead className="bg-gray-50">
                        <tr>
                          <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Kód produktu</th>
                          <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Název produktu</th>
                          <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Hmotnost</th>
                          <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Sklad eshop celkem</th>
                          <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Denní prodeje</th>
                          <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Zásoba (dny)</th>
                          <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Doporučené množství</th>
                          <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Budoucí zásoba (dny)</th>
                        </tr>
                      </thead>
                      <tbody className="bg-white divide-y divide-gray-200">
                        {batchPlanMutation.isPending && selectedSemiproduct ? (
                          <tr>
                            <td colSpan={8} className="px-4 py-8 text-center text-gray-500">
                              <div className="flex items-center justify-center">
                                <div className="animate-spin rounded-full h-6 w-6 border-b-2 border-indigo-600"></div>
                                <span className="ml-2">Načítání dat...</span>
                              </div>
                            </td>
                          </tr>
                        ) : displayProductSizes.length === 0 && selectedSemiproduct ? (
                          <tr>
                            <td colSpan={8} className="px-4 py-8 text-center text-gray-500">
                              Žádné produkty nenalezeny
                            </td>
                          </tr>
                        ) : (
                          displayProductSizes.map((product) => (
                            <tr key={product.productCode} className="hover:bg-gray-50">
                              <td className="px-4 py-3 text-sm font-mono text-gray-900">
                                {product.productCode}
                              </td>
                              <td className="px-4 py-3 text-sm text-gray-900">
                                {product.productName}
                              </td>
                              <td className="px-4 py-3 text-sm text-gray-900">
                                {product.netWeight.toFixed(0)} g
                              </td>
                              <td className="px-4 py-3 text-sm text-gray-900">
                                {product.stockEshopTotal.toFixed(0)} ks
                              </td>
                              <td className="px-4 py-3 text-sm text-gray-900">
                                {product.dailySales.toFixed(1)} ks
                              </td>
                              <td className="px-4 py-3">
                                <span className={`text-sm px-2 py-1 rounded-full ${
                                  product.currentCoverage < 7 
                                    ? 'bg-red-100 text-red-800' 
                                    : product.currentCoverage < 14 
                                      ? 'bg-yellow-100 text-yellow-800'
                                      : 'bg-green-100 text-green-800'
                                }`}>
                                  {product.currentCoverage.toFixed(1)} dní
                                </span>
                              </td>
                              <td className="px-4 py-3 text-sm font-medium text-indigo-600">
                                {product.recommendedQuantity} ks
                              </td>
                              <td className="px-4 py-3">
                                <span className={`text-sm px-2 py-1 rounded-full ${
                                  product.futureCoverage < 7 
                                    ? 'bg-red-100 text-red-800' 
                                    : product.futureCoverage < 14 
                                      ? 'bg-yellow-100 text-yellow-800'
                                      : 'bg-green-100 text-green-800'
                                }`}>
                                  {product.futureCoverage.toFixed(1)} dní
                                </span>
                              </td>
                            </tr>
                          ))
                        )}
                      </tbody>
                    </table>
                  </div>
                </div>
              </>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};

export default BatchPlanningCalculator;