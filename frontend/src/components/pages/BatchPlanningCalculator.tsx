import React, { useState } from "react";
import {
  Calculator,
  Package,
  Settings,
  Zap,
  Weight,
  Target,
  CheckCircle,
  AlertCircle,
  Info,
} from "lucide-react";
import {
  useBatchPlanningMutation,
  CalculateBatchPlanRequest,
  BatchPlanControlMode,
  ProductSizeConstraint,
  getControlModeDisplayName,
  formatVolume,
  formatDays,
  formatPercentage,
} from "../../api/hooks/useBatchPlanning";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";

const BatchPlanningCalculator: React.FC = () => {
  // Form state
  const [semiproductCode, setSemiproductCode] = useState("");
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");
  const [controlMode, setControlMode] = useState<BatchPlanControlMode>(BatchPlanControlMode.MmqMultiplier);
  const [mmqMultiplier, setMmqMultiplier] = useState<number>(1.0);
  const [totalWeight, setTotalWeight] = useState<number>(0);
  const [targetCoverage, setTargetCoverage] = useState<number>(30);
  const [productConstraints, setProductConstraints] = useState<ProductSizeConstraint[]>([]);

  // Mutation
  const batchPlanMutation = useBatchPlanningMutation();

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    
    const request: CalculateBatchPlanRequest = {
      semiproductCode: semiproductCode.trim(),
      fromDate: fromDate || undefined,
      toDate: toDate || undefined,
      controlMode,
      mmqMultiplier: controlMode === BatchPlanControlMode.MmqMultiplier ? mmqMultiplier : undefined,
      totalWeightToUse: controlMode === BatchPlanControlMode.TotalWeight ? totalWeight : undefined,
      targetDaysCoverage: controlMode === BatchPlanControlMode.TargetDaysCoverage ? targetCoverage : undefined,
      productConstraints,
    };

    batchPlanMutation.mutate(request);
  };

  const addProductConstraint = () => {
    setProductConstraints([
      ...productConstraints,
      { productCode: "", isFixed: false, fixedQuantity: undefined }
    ]);
  };

  const updateProductConstraint = (index: number, updates: Partial<ProductSizeConstraint>) => {
    const updated = [...productConstraints];
    updated[index] = { ...updated[index], ...updates };
    setProductConstraints(updated);
  };

  const removeProductConstraint = (index: number) => {
    setProductConstraints(productConstraints.filter((_, i) => i !== index));
  };

  const response = batchPlanMutation.data;

  return (
    <div className={`flex flex-col ${PAGE_CONTAINER_HEIGHT} bg-gray-50`}>
      {/* Header */}
      <div className="bg-white border-b px-6 py-4 flex-shrink-0">
        <div className="flex items-center space-x-3">
          <Calculator className="w-6 h-6 text-indigo-600" />
          <h1 className="text-xl font-semibold text-gray-900">
            Batch Planning Calculator
          </h1>
        </div>
        <p className="text-sm text-gray-600 mt-1">
          Optimize semiproduct distribution across different product sizes
        </p>
      </div>

      {/* Main Content */}
      <div className="flex-1 overflow-y-auto">
        <div className="p-6">
          <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
            {/* Left Column - Input Form */}
            <div className="space-y-6">
              {/* Basic Settings Card */}
              <div className="bg-white rounded-lg shadow-sm border p-6">
                <h2 className="text-lg font-medium text-gray-900 mb-4 flex items-center">
                  <Settings className="w-5 h-5 text-gray-500 mr-2" />
                  Basic Settings
                </h2>

                <form onSubmit={handleSubmit} className="space-y-4">
                  {/* Semiproduct Selection */}
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">
                      Semiproduct Code <span className="text-red-500">*</span>
                    </label>
                    <input
                      type="text"
                      value={semiproductCode}
                      onChange={(e) => setSemiproductCode(e.target.value)}
                      placeholder="Enter semiproduct code..."
                      className="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent"
                      required
                    />
                  </div>

                  {/* Time Period */}
                  <div className="grid grid-cols-2 gap-4">
                    <div>
                      <label className="block text-sm font-medium text-gray-700 mb-1">
                        From Date
                      </label>
                      <input
                        type="date"
                        value={fromDate}
                        onChange={(e) => setFromDate(e.target.value)}
                        className="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent"
                      />
                    </div>
                    <div>
                      <label className="block text-sm font-medium text-gray-700 mb-1">
                        To Date
                      </label>
                      <input
                        type="date"
                        value={toDate}
                        onChange={(e) => setToDate(e.target.value)}
                        className="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent"
                      />
                    </div>
                  </div>

                  {/* Control Mode Selection */}
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-2">
                      Control Mode
                    </label>
                    <div className="space-y-3">
                      {/* MMQ Multiplier */}
                      <div className="flex items-center space-x-3">
                        <input
                          type="radio"
                          id="mmq-mode"
                          name="controlMode"
                          value={BatchPlanControlMode.MmqMultiplier}
                          checked={controlMode === BatchPlanControlMode.MmqMultiplier}
                          onChange={(e) => setControlMode(Number(e.target.value) as BatchPlanControlMode)}
                          className="text-indigo-600 focus:ring-indigo-500"
                        />
                        <label htmlFor="mmq-mode" className="flex items-center space-x-2">
                          <Zap className="w-4 h-4 text-yellow-500" />
                          <span>MMQ Multiplier:</span>
                        </label>
                        <input
                          type="number"
                          step="0.1"
                          min="0.1"
                          value={mmqMultiplier}
                          onChange={(e) => setMmqMultiplier(Number(e.target.value))}
                          disabled={controlMode !== BatchPlanControlMode.MmqMultiplier}
                          className="w-20 px-2 py-1 text-sm border border-gray-300 rounded focus:outline-none focus:ring-1 focus:ring-indigo-500 disabled:bg-gray-100"
                        />
                        <span className="text-sm text-gray-500">x</span>
                      </div>

                      {/* Total Weight */}
                      <div className="flex items-center space-x-3">
                        <input
                          type="radio"
                          id="weight-mode"
                          name="controlMode"
                          value={BatchPlanControlMode.TotalWeight}
                          checked={controlMode === BatchPlanControlMode.TotalWeight}
                          onChange={(e) => setControlMode(Number(e.target.value) as BatchPlanControlMode)}
                          className="text-indigo-600 focus:ring-indigo-500"
                        />
                        <label htmlFor="weight-mode" className="flex items-center space-x-2">
                          <Weight className="w-4 h-4 text-blue-500" />
                          <span>Total Weight:</span>
                        </label>
                        <input
                          type="number"
                          min="0"
                          value={totalWeight}
                          onChange={(e) => setTotalWeight(Number(e.target.value))}
                          disabled={controlMode !== BatchPlanControlMode.TotalWeight}
                          className="w-24 px-2 py-1 text-sm border border-gray-300 rounded focus:outline-none focus:ring-1 focus:ring-indigo-500 disabled:bg-gray-100"
                        />
                        <span className="text-sm text-gray-500">ml</span>
                      </div>

                      {/* Target Coverage */}
                      <div className="flex items-center space-x-3">
                        <input
                          type="radio"
                          id="coverage-mode"
                          name="controlMode"
                          value={BatchPlanControlMode.TargetDaysCoverage}
                          checked={controlMode === BatchPlanControlMode.TargetDaysCoverage}
                          onChange={(e) => setControlMode(Number(e.target.value) as BatchPlanControlMode)}
                          className="text-indigo-600 focus:ring-indigo-500"
                        />
                        <label htmlFor="coverage-mode" className="flex items-center space-x-2">
                          <Target className="w-4 h-4 text-green-500" />
                          <span>Target Coverage:</span>
                        </label>
                        <input
                          type="number"
                          min="1"
                          value={targetCoverage}
                          onChange={(e) => setTargetCoverage(Number(e.target.value))}
                          disabled={controlMode !== BatchPlanControlMode.TargetDaysCoverage}
                          className="w-20 px-2 py-1 text-sm border border-gray-300 rounded focus:outline-none focus:ring-1 focus:ring-indigo-500 disabled:bg-gray-100"
                        />
                        <span className="text-sm text-gray-500">days</span>
                      </div>
                    </div>
                  </div>

                  {/* Submit Button */}
                  <button
                    type="submit"
                    disabled={batchPlanMutation.isPending || !semiproductCode.trim()}
                    className="w-full bg-indigo-600 text-white py-2 px-4 rounded-md hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    {batchPlanMutation.isPending ? "Calculating..." : "Calculate Batch Plan"}
                  </button>
                </form>
              </div>

              {/* Product Constraints Card */}
              <div className="bg-white rounded-lg shadow-sm border p-6">
                <div className="flex items-center justify-between mb-4">
                  <h3 className="text-lg font-medium text-gray-900 flex items-center">
                    <Package className="w-5 h-5 text-gray-500 mr-2" />
                    Product Constraints
                  </h3>
                  <button
                    type="button"
                    onClick={addProductConstraint}
                    className="text-sm bg-gray-100 text-gray-700 px-3 py-1 rounded hover:bg-gray-200"
                  >
                    Add Constraint
                  </button>
                </div>

                {productConstraints.length === 0 ? (
                  <p className="text-sm text-gray-500 text-center py-4">
                    No product constraints defined. All products will be optimized.
                  </p>
                ) : (
                  <div className="space-y-3">
                    {productConstraints.map((constraint, index) => (
                      <div key={index} className="border border-gray-200 rounded p-3">
                        <div className="grid grid-cols-12 gap-2 items-center">
                          <div className="col-span-5">
                            <input
                              type="text"
                              placeholder="Product code"
                              value={constraint.productCode}
                              onChange={(e) => updateProductConstraint(index, { productCode: e.target.value })}
                              className="w-full px-2 py-1 text-sm border border-gray-300 rounded focus:outline-none focus:ring-1 focus:ring-indigo-500"
                            />
                          </div>
                          <div className="col-span-2 flex items-center">
                            <label className="flex items-center">
                              <input
                                type="checkbox"
                                checked={constraint.isFixed}
                                onChange={(e) => updateProductConstraint(index, { 
                                  isFixed: e.target.checked,
                                  fixedQuantity: e.target.checked ? constraint.fixedQuantity || 0 : undefined
                                })}
                                className="text-indigo-600 focus:ring-indigo-500"
                              />
                              <span className="ml-1 text-sm">Fixed</span>
                            </label>
                          </div>
                          <div className="col-span-3">
                            <input
                              type="number"
                              placeholder="Quantity"
                              min="0"
                              value={constraint.fixedQuantity || ''}
                              onChange={(e) => updateProductConstraint(index, { fixedQuantity: Number(e.target.value) || undefined })}
                              disabled={!constraint.isFixed}
                              className="w-full px-2 py-1 text-sm border border-gray-300 rounded focus:outline-none focus:ring-1 focus:ring-indigo-500 disabled:bg-gray-100"
                            />
                          </div>
                          <div className="col-span-2">
                            <button
                              type="button"
                              onClick={() => removeProductConstraint(index)}
                              className="text-red-600 hover:text-red-800 text-sm px-2 py-1"
                            >
                              Remove
                            </button>
                          </div>
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </div>

            {/* Right Column - Results */}
            <div className="space-y-6">
              {/* Error Display */}
              {batchPlanMutation.isError && (
                <div className="bg-red-50 border border-red-200 rounded-lg p-4">
                  <div className="flex items-center">
                    <AlertCircle className="w-5 h-5 text-red-500 mr-2" />
                    <h3 className="text-sm font-medium text-red-800">Calculation Failed</h3>
                  </div>
                  <p className="text-sm text-red-700 mt-1">
                    {batchPlanMutation.error?.message || "An error occurred while calculating the batch plan."}
                  </p>
                </div>
              )}

              {/* Results */}
              {response && response.success && (
                <>
                  {/* Summary Card */}
                  <div className="bg-white rounded-lg shadow-sm border p-6">
                    <h3 className="text-lg font-medium text-gray-900 mb-4 flex items-center">
                      <Info className="w-5 h-5 text-blue-500 mr-2" />
                      Summary
                    </h3>
                    
                    <div className="grid grid-cols-2 gap-4 text-sm">
                      <div>
                        <span className="text-gray-600">Semiproduct:</span>
                        <p className="font-medium">{response.semiproduct.productName}</p>
                      </div>
                      <div>
                        <span className="text-gray-600">Available Stock:</span>
                        <p className="font-medium">{formatVolume(response.semiproduct.availableStock)}</p>
                      </div>
                      <div>
                        <span className="text-gray-600">Control Mode:</span>
                        <p className="font-medium">{getControlModeDisplayName(response.summary.usedControlMode)}</p>
                      </div>
                      <div>
                        <span className="text-gray-600">Volume Used:</span>
                        <p className="font-medium">{formatVolume(response.summary.totalVolumeUsed)}</p>
                      </div>
                      <div>
                        <span className="text-gray-600">Utilization:</span>
                        <p className="font-medium">{formatPercentage(response.summary.volumeUtilizationPercentage)}</p>
                      </div>
                      <div>
                        <span className="text-gray-600">Avg. Coverage:</span>
                        <p className="font-medium">{formatDays(response.summary.achievedAverageCoverage)}</p>
                      </div>
                      <div>
                        <span className="text-gray-600">Fixed Products:</span>
                        <p className="font-medium">{response.summary.fixedProductsCount}</p>
                      </div>
                      <div>
                        <span className="text-gray-600">Optimized Products:</span>
                        <p className="font-medium">{response.summary.optimizedProductsCount}</p>
                      </div>
                    </div>
                  </div>

                  {/* Product Results Table */}
                  <div className="bg-white rounded-lg shadow-sm border overflow-hidden">
                    <div className="px-6 py-4 border-b">
                      <h3 className="text-lg font-medium text-gray-900 flex items-center">
                        <Package className="w-5 h-5 text-green-500 mr-2" />
                        Product Planning Results
                      </h3>
                    </div>
                    
                    <div className="overflow-x-auto">
                      <table className="min-w-full divide-y divide-gray-200">
                        <thead className="bg-gray-50">
                          <tr>
                            <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Product</th>
                            <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Current Stock</th>
                            <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Recommended</th>
                            <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Future Coverage</th>
                            <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Volume</th>
                            <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Status</th>
                          </tr>
                        </thead>
                        <tbody className="bg-white divide-y divide-gray-200">
                          {response.productSizes.map((product) => (
                            <tr key={product.productCode} className="hover:bg-gray-50">
                              <td className="px-4 py-3">
                                <div>
                                  <p className="text-sm font-medium text-gray-900">{product.productName}</p>
                                  <p className="text-xs text-gray-500">{product.productCode} ({product.productSize})</p>
                                </div>
                              </td>
                              <td className="px-4 py-3">
                                <div>
                                  <p className="text-sm text-gray-900">{product.currentStock.toFixed(0)} pcs</p>
                                  <p className="text-xs text-gray-500">{formatDays(product.currentDaysCoverage)}</p>
                                </div>
                              </td>
                              <td className="px-4 py-3">
                                <div>
                                  <p className="text-sm font-medium text-gray-900">
                                    {product.recommendedUnitsToProduceHumanReadable} pcs
                                  </p>
                                  {product.isFixed && (
                                    <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-blue-100 text-blue-800">
                                      Fixed
                                    </span>
                                  )}
                                  {product.wasOptimized && (
                                    <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-green-100 text-green-800">
                                      Optimized
                                    </span>
                                  )}
                                </div>
                              </td>
                              <td className="px-4 py-3">
                                <div>
                                  <p className="text-sm text-gray-900">{product.futureStock.toFixed(0)} pcs</p>
                                  <p className="text-xs text-gray-500">{formatDays(product.futureDaysCoverage)}</p>
                                </div>
                              </td>
                              <td className="px-4 py-3">
                                <p className="text-sm text-gray-900">{formatVolume(product.totalVolumeRequired)}</p>
                              </td>
                              <td className="px-4 py-3">
                                <div className="flex items-center">
                                  {product.isFixed ? (
                                    <AlertCircle className="w-4 h-4 text-blue-500 mr-1" />
                                  ) : (
                                    <CheckCircle className="w-4 h-4 text-green-500 mr-1" />
                                  )}
                                  <span className="text-xs text-gray-600" title={product.optimizationNote}>
                                    {product.optimizationNote}
                                  </span>
                                </div>
                              </td>
                            </tr>
                          ))}
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
    </div>
  );
};

export default BatchPlanningCalculator;