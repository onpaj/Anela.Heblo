import React, { useState, useEffect } from "react";
import {
  Calculator,
  Package,
  Settings,
  FileText,
} from "lucide-react";
import {
  useBatchPlanningMutation,
  CalculateBatchPlanRequest,
  BatchPlanControlMode,
  ProductSizeConstraint,
} from "../../api/hooks/useBatchPlanning";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";
import CatalogAutocomplete from "../common/CatalogAutocomplete";
import { CatalogItemDto, ProductType, CreateManufactureOrderRequest, CreateManufactureOrderProductRequest } from "../../api/generated/api-client";
import { useCreateManufactureOrder } from "../../api/hooks/useManufactureOrders";
import { useSearchParams } from "react-router-dom";
import ManufactureOrderDetail from "./ManufactureOrderDetail";

const BatchPlanningCalculator: React.FC = () => {
  // Selected semiproduct state
  const [selectedSemiproduct, setSelectedSemiproduct] = useState<CatalogItemDto | null>(null);
  
  // Form state
  const [mmqMultiplier, setMmqMultiplier] = useState<number>(1.0);
  const [totalBatchSize, setTotalBatchSize] = useState<number>(0);
  const [targetDaysCoverage, setTargetDaysCoverage] = useState<number>(30);
  const [inputMode, setInputMode] = useState<'multiplier' | 'total'>('multiplier'); // Which field user is editing
  
  // Control mode selection
  const [controlMode, setControlMode] = useState<BatchPlanControlMode>(BatchPlanControlMode.MmqMultiplier);
  
  // Sales settings state
  const [salesMultiplier, setSalesMultiplier] = useState<number>(1.3);
  const [fromDate, setFromDate] = useState<Date>(new Date(new Date().getFullYear() - 1, new Date().getMonth(), new Date().getDate()));
  const [toDate, setToDate] = useState<Date>(new Date());
  
  // Product constraints state (for fixed quantities)
  const [productConstraints, setProductConstraints] = useState<Map<string, { isFixed: boolean; quantity: number }>>(new Map());
  
  // Modal state for showing manufacture order detail
  const [showOrderModal, setShowOrderModal] = useState(false);
  const [createdOrderId, setCreatedOrderId] = useState<number | null>(null);
  
  // Mutation
  const batchPlanMutation = useBatchPlanningMutation();
  const createOrderMutation = useCreateManufactureOrder();
  const [searchParams, setSearchParams] = useSearchParams();

  // Get API response data
  const response = batchPlanMutation.data;
  
  // Get real MMQ from API response or fallback to 0 if no response yet
  const productMMQ = response?.success && response.semiproduct 
    ? response.semiproduct.minimalManufactureQuantity 
    : 0;
  
  // Get API product sizes
  const apiProductSizes = response?.productSizes || [];

  // Handle prefilled data from URL parameters (from ManufactureBatchCalculator)
  useEffect(() => {
    const productCode = searchParams.get('productCode');
    const productName = searchParams.get('productName');
    const batchSize = searchParams.get('batchSize');
    
    if (productCode && productName && batchSize) {
      console.log('Processing prefilled data from URL:', { 
        productCode, 
        productName,
        batchSize: parseFloat(batchSize)
      });
      
      // Create CatalogItemDto from URL parameters
      const prefilledProduct = new CatalogItemDto({
        productCode,
        productName,
        type: ProductType.SemiProduct
      });
      
      // Set the prefilled product
      setSelectedSemiproduct(prefilledProduct);
      
      // Set control mode to total weight and set the prefilled batch size
      setControlMode(BatchPlanControlMode.TotalWeight);
      setTotalBatchSize(parseFloat(batchSize));
      
      // Clear URL parameters to clean up the URL
      setSearchParams({});
      
      // Trigger the API call directly
      const requestData: any = {
        semiproductCode: productCode,
        controlMode: BatchPlanControlMode.TotalWeight,
        fromDate: fromDate,
        toDate: toDate,
        salesMultiplier: salesMultiplier,
        totalWeightToUse: parseFloat(batchSize),
      };

      console.log('Triggering batch plan calculation with:', requestData);
      const request = new CalculateBatchPlanRequest(requestData);
      batchPlanMutation.mutate(request);
    }
  }, [searchParams]); // Only depend on searchParams to avoid infinite loops

  // Update local state when API response changes
  useEffect(() => {
    if (response?.summary) {
      // Update values from API response
      if (response.summary.effectiveMmqMultiplier != null) {
        setMmqMultiplier(response.summary.effectiveMmqMultiplier);
      }
      if (response.summary.actualTotalWeight != null) {
        setTotalBatchSize(response.summary.actualTotalWeight);
      }
    }
  }, [response]);
  
  // Convert API data to grid format
  const productGridData = apiProductSizes.map((product) => ({
    productCode: product.productCode || '',
    productName: product.productName || '',
    netWeight: product.weightPerUnit || 0,
    stockEshopTotal: product.currentStock || 0,
    dailySales: product.dailySalesRate || 0,
    currentCoverage: product.currentDaysCoverage || 0,
    recommendedQuantity: product.recommendedUnitsToProduceHumanReadable || 0,
    futureCoverage: product.futureDaysCoverage || 0,
    enabled: product.enabled ?? true // Add enabled field from API
  }));

  // Show loading state or actual data
  const displayProductSizes = batchPlanMutation.isPending && selectedSemiproduct 
    ? [] // Empty while loading
    : productGridData;


  // Quick date range selectors
  const handleQuickDateRange = (
    type: "lastq" | "y2y" | "nextq",
  ) => {
    const now = new Date();
    let fromDateNew: Date;
    let toDateNew: Date;

    switch (type) {
      case "lastq":
        // Previous quarter (last 3 months)
        fromDateNew = new Date(now.getFullYear(), now.getMonth() - 3, 1);
        toDateNew = new Date(now.getFullYear(), now.getMonth(), 0);
        break;
      case "y2y":
        // Year to year (12 months back)
        fromDateNew = new Date(now.getFullYear() - 1, now.getMonth(), now.getDate());
        toDateNew = new Date();
        break;
      case "nextq":
        // Next quarter from previous year (3 months forward from same period last year)
        const lastYear = now.getFullYear() - 1;
        fromDateNew = new Date(lastYear, now.getMonth(), 1);
        toDateNew = new Date(lastYear, now.getMonth() + 3, 0);
        break;
    }

    setFromDate(fromDateNew);
    setToDate(toDateNew);
  };

  // Get tooltip text for date range buttons
  const getDateRangeTooltip = (type: "lastq" | "y2y" | "nextq") => {
    const now = new Date();
    let fromDateNew: Date;
    let toDateNew: Date;

    switch (type) {
      case "lastq":
        fromDateNew = new Date(now.getFullYear(), now.getMonth() - 3, 1);
        toDateNew = new Date(now.getFullYear(), now.getMonth(), 0);
        break;
      case "y2y":
        fromDateNew = new Date(now.getFullYear() - 1, now.getMonth(), now.getDate());
        toDateNew = new Date();
        break;
      case "nextq":
        const lastYear = now.getFullYear() - 1;
        fromDateNew = new Date(lastYear, now.getMonth(), 1);
        toDateNew = new Date(lastYear, now.getMonth() + 3, 0);
        break;
    }

    return `${fromDateNew.toLocaleDateString("cs-CZ")} - ${toDateNew.toLocaleDateString("cs-CZ")}`;
  };

  const calculateBatchPlan = (semiproductCode: string, fromDateParam?: Date, toDateParam?: Date) => {
    const requestData: any = {
      semiproductCode: semiproductCode,
      controlMode: controlMode,
      fromDate: fromDateParam || fromDate,
      toDate: toDateParam || toDate,
      salesMultiplier: salesMultiplier,
    };

    // Add mode-specific parameters
    switch (controlMode) {
      case BatchPlanControlMode.MmqMultiplier:
        requestData.mmqMultiplier = mmqMultiplier;
        break;
      case BatchPlanControlMode.TotalWeight:
        requestData.totalWeightToUse = totalBatchSize;
        break;
      case BatchPlanControlMode.TargetDaysCoverage:
        requestData.targetDaysCoverage = targetDaysCoverage;
        break;
    }

    const request = new CalculateBatchPlanRequest(requestData);
    batchPlanMutation.mutate(request);
  };

  const calculateBatchPlanWithConstraints = (semiproductCode: string, fromDateParam?: Date, toDateParam?: Date) => {
    // Convert constraints Map to ProductSizeConstraint array
    const constraints = Array.from(productConstraints.entries()).map(([productCode, constraint]) => 
      new ProductSizeConstraint({
        productCode,
        isFixed: constraint.isFixed,
        fixedQuantity: constraint.quantity
      })
    );

    const requestData: any = {
      semiproductCode: semiproductCode,
      controlMode: controlMode,
      fromDate: fromDateParam || fromDate,
      toDate: toDateParam || toDate,
      salesMultiplier: salesMultiplier,
      productConstraints: constraints
    };

    // Add mode-specific parameters
    switch (controlMode) {
      case BatchPlanControlMode.MmqMultiplier:
        requestData.mmqMultiplier = mmqMultiplier;
        break;
      case BatchPlanControlMode.TotalWeight:
        requestData.totalWeightToUse = totalBatchSize;
        break;
      case BatchPlanControlMode.TargetDaysCoverage:
        requestData.targetDaysCoverage = targetDaysCoverage;
        break;
    }

    const request = new CalculateBatchPlanRequest(requestData);
    batchPlanMutation.mutate(request);
  };

  const handleSemiproductSelect = (product: CatalogItemDto | null) => {
    setSelectedSemiproduct(product);
    // Reset form when changing product
    setMmqMultiplier(1.0);
    setTotalBatchSize(0);
    setTargetDaysCoverage(30);
    setInputMode('multiplier');
    setControlMode(BatchPlanControlMode.MmqMultiplier); // Reset to default mode
    // Clear constraints when changing product
    setProductConstraints(new Map());
    
    // Auto-trigger calculation immediately after product selection
    if (product?.productCode) {
      const requestData: any = {
        semiproductCode: product.productCode,
        controlMode: BatchPlanControlMode.MmqMultiplier,
        fromDate: fromDate,
        toDate: toDate,
        salesMultiplier: salesMultiplier,
        mmqMultiplier: 1.0, // Use reset value
      };

      const request = new CalculateBatchPlanRequest(requestData);
      batchPlanMutation.mutate(request);
    }
  };


  const handleProductFixedChange = (productCode: string, isFixed: boolean, currentRecommendedQuantity: number) => {
    setProductConstraints(prev => {
      const newConstraints = new Map(prev);
      if (isFixed) {
        newConstraints.set(productCode, { isFixed: true, quantity: currentRecommendedQuantity });
      } else {
        newConstraints.delete(productCode);
      }
      return newConstraints;
    });
  };

  const handleProductQuantityChange = (productCode: string, quantity: number) => {
    setProductConstraints(prev => {
      const newConstraints = new Map(prev);
      const existing = newConstraints.get(productCode);
      if (existing?.isFixed) {
        newConstraints.set(productCode, { isFixed: true, quantity });
      }
      return newConstraints;
    });
  };

  // Manual calculation trigger
  const handleManualCalculate = () => {
    if (selectedSemiproduct?.productCode) {
      if (productConstraints.size > 0) {
        calculateBatchPlanWithConstraints(selectedSemiproduct.productCode, fromDate, toDate);
      } else {
        calculateBatchPlan(selectedSemiproduct.productCode, fromDate, toDate);
      }
    }
  };

  // Handle create manufacture order from batch planning
  const handleCreateOrder = async () => {
    if (!response?.success || !response.semiproduct || !response.summary || !selectedSemiproduct) {
      return;
    }

    // Get products with quantity > 0
    const productsToManufacture = response.productSizes
      ?.filter(product => (product.recommendedUnitsToProduceHumanReadable || 0) > 0)
      ?.map(product => ({
        productCode: product.productCode || "",
        productName: product.productName || "",
        plannedQuantity: product.recommendedUnitsToProduceHumanReadable || 0
      })) || [];

    if (productsToManufacture.length === 0) {
      alert("Žádné produkty nemají plánované množství > 0");
      return;
    }

    // Default dates (tomorrow and day after tomorrow)
    const tomorrow = new Date();
    tomorrow.setDate(tomorrow.getDate() + 1);
    const dayAfterTomorrow = new Date();
    dayAfterTomorrow.setDate(dayAfterTomorrow.getDate() + 2);

    try {
      const orderRequest = new CreateManufactureOrderRequest({
        productCode: response.semiproduct.productCode || "",
        productName: response.semiproduct.productName || "",
        originalBatchSize: response.semiproduct.minimalManufactureQuantity || 0,
        newBatchSize: response.summary.actualTotalWeight || 0,
        scaleFactor: response.summary.effectiveMmqMultiplier || 1.0,
        products: productsToManufacture.map(p => new CreateManufactureOrderProductRequest({
          productCode: p.productCode,
          productName: p.productName,
          plannedQuantity: p.plannedQuantity
        })),
        semiProductPlannedDate: tomorrow,
        productPlannedDate: dayAfterTomorrow,
        responsiblePerson: undefined
      });

      const orderResponse = await createOrderMutation.mutateAsync(orderRequest);
      
      if (orderResponse.success && orderResponse.id) {
        // Open the manufacture order detail modal
        setCreatedOrderId(orderResponse.id);
        setShowOrderModal(true);
      }
    } catch (error) {
      console.error("Error creating manufacture order:", error);
      alert("Chyba při vytváření zakázky. Zkuste to prosím znovu.");
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
            {/* Semiproduct Selection and Batch Settings */}
            <div className="bg-white rounded-lg shadow-sm border p-6 relative z-50">
              <h2 className="text-lg font-medium text-gray-900 mb-4 flex items-center">
                <Package className="w-5 h-5 text-gray-500 mr-2" />
                Výběr polotovaru a nastavení
              </h2>
              
              <div className="space-y-4">
                {/* Top Row: Product Selection and Sales Settings */}
                <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
                  {/* Left: Product Selection */}
                  <div>
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

                  {/* Right: Sales Settings */}
                  <div className="space-y-3">
                    <h4 className="text-xs font-medium text-gray-600 flex items-center">
                      <Settings className="w-3 h-3 text-gray-500 mr-1" />
                      Nastavení prodejů
                    </h4>
                    
                    {/* First row: Sales Multiplier + Quick Date Range Buttons */}
                    <div className="flex items-center gap-3">
                      {/* Sales Multiplier */}
                      <div className="flex items-center gap-2">
                        <label className="text-xs text-gray-600 whitespace-nowrap">Multiplikátor:</label>
                        <input
                          type="number"
                          step="0.1"
                          min="0.1"
                          max="9.9"
                          value={salesMultiplier.toFixed(1)}
                          onChange={(e) => {
                            setSalesMultiplier(Number(e.target.value));
                          }}
                          className="w-20 px-2 py-1 text-sm border border-gray-300 rounded text-center focus:outline-none focus:ring-1 focus:ring-indigo-500"
                          title="Sales Multiplier (1.0-9.9)"
                        />
                      </div>

                      {/* Spacer to push buttons to align with date pickers */}
                      <div className="flex-1"></div>

                      {/* Quick date range buttons - aligned with date pickers */}
                      <div className="flex gap-2">
                        <button
                          onClick={() => handleQuickDateRange("lastq")}
                          className="px-3 py-1 text-xs bg-gray-100 hover:bg-gray-200 text-gray-700 rounded border border-gray-300 transition-colors"
                          title={getDateRangeTooltip("lastq")}
                        >
                          LastQ
                        </button>
                        <button
                          onClick={() => handleQuickDateRange("y2y")}
                          className="px-3 py-1 text-xs bg-gray-100 hover:bg-gray-200 text-gray-700 rounded border border-gray-300 transition-colors"
                          title={getDateRangeTooltip("y2y")}
                        >
                          Y2Y
                        </button>
                        <button
                          onClick={() => handleQuickDateRange("nextq")}
                          className="px-3 py-1 text-xs bg-gray-100 hover:bg-gray-200 text-gray-700 rounded border border-gray-300 transition-colors"
                          title={getDateRangeTooltip("nextq")}
                        >
                          NextQ
                        </button>
                      </div>
                    </div>

                    {/* Second row: Date Pickers */}
                    <div className="flex gap-3 items-center">
                      <label className="text-xs text-gray-600 whitespace-nowrap">Datum od:</label>
                      <input
                        type="date"
                        value={fromDate.toISOString().split('T')[0]}
                        onChange={(e) => {
                          const newFromDate = new Date(e.target.value);
                          setFromDate(newFromDate);
                        }}
                        className="w-44 px-2 py-1 text-sm border border-gray-300 rounded focus:outline-none focus:ring-1 focus:ring-indigo-500"
                      />
                      
                      <label className="text-xs text-gray-600 whitespace-nowrap">do:</label>
                      <input
                        type="date"
                        value={toDate.toISOString().split('T')[0]}
                        onChange={(e) => {
                          const newToDate = new Date(e.target.value);
                          setToDate(newToDate);
                        }}
                        className="w-44 px-2 py-1 text-sm border border-gray-300 rounded focus:outline-none focus:ring-1 focus:ring-indigo-500"
                      />
                    </div>
                  </div>
                </div>

                {/* Bottom Row: Batch Calculation Settings (only when product is selected) */}
                {selectedSemiproduct && (
                  <div className="space-y-3">
                    {/* Control Mode Selection */}
                    <div className="flex items-center gap-4">
                      <label className="text-sm font-medium text-gray-700">Režim řízení:</label>
                      <div className="flex gap-4">
                        <label className="flex items-center gap-2">
                          <input
                            type="radio"
                            name="controlMode"
                            checked={controlMode === BatchPlanControlMode.MmqMultiplier}
                            onChange={() => setControlMode(BatchPlanControlMode.MmqMultiplier)}
                            className="h-4 w-4 text-indigo-600 focus:ring-indigo-500 border-gray-300"
                          />
                          <span className="text-sm text-gray-700">MMQ Multiplikátor</span>
                        </label>
                        <label className="flex items-center gap-2">
                          <input
                            type="radio"
                            name="controlMode"
                            checked={controlMode === BatchPlanControlMode.TotalWeight}
                            onChange={() => setControlMode(BatchPlanControlMode.TotalWeight)}
                            className="h-4 w-4 text-indigo-600 focus:ring-indigo-500 border-gray-300"
                          />
                          <span className="text-sm text-gray-700">Celková hmotnost</span>
                        </label>
                        <label className="flex items-center gap-2">
                          <input
                            type="radio"
                            name="controlMode"
                            checked={controlMode === BatchPlanControlMode.TargetDaysCoverage}
                            onChange={() => setControlMode(BatchPlanControlMode.TargetDaysCoverage)}
                            className="h-4 w-4 text-indigo-600 focus:ring-indigo-500 border-gray-300"
                          />
                          <span className="text-sm text-gray-700">Cílová zásoba (dny)</span>
                        </label>
                      </div>
                    </div>

                    {/* Mode-specific inputs */}
                    <div className="flex items-center gap-3">
                      {/* MMQ Multiplier Mode */}
                      {controlMode === BatchPlanControlMode.MmqMultiplier && (
                        <>
                          {/* MMQ */}
                          <div className="flex-none">
                            <input
                              type="number"
                              value={productMMQ || ""}
                              readOnly
                              placeholder={batchPlanMutation.isPending ? "MMQ..." : "MMQ"}
                              className="w-24 px-3 py-2 text-sm border border-gray-300 rounded-md bg-gray-50 text-gray-600 text-center"
                            />
                          </div>
                          
                          {/* Multiplier with × symbol */}
                          <div className="flex items-center gap-2 flex-none">
                            <span className="text-gray-400 text-lg">×</span>
                            <input
                              type="number"
                              step="0.5"
                              min="0"
                              value={mmqMultiplier.toFixed(1)}
                              onChange={(e) => setMmqMultiplier(Number(e.target.value))}
                              placeholder="Multiplikátor"
                              className="w-24 px-3 py-2 text-sm border border-indigo-300 bg-white text-gray-900 rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500 text-center"
                            />
                          </div>
                          
                          {/* Result */}
                          <div className="flex items-center gap-2 flex-none">
                            <span className="text-gray-400 text-lg">=</span>
                            <input
                              type="number"
                              value={productMMQ ? (productMMQ * mmqMultiplier).toFixed(0) : ""}
                              readOnly
                              placeholder="Výsledek"
                              className="w-28 px-3 py-2 text-sm border border-indigo-300 bg-indigo-50 text-indigo-800 rounded-md text-center font-medium"
                            />
                          </div>
                        </>
                      )}

                      {/* Total Weight Mode */}
                      {controlMode === BatchPlanControlMode.TotalWeight && (
                        <div className="flex items-center gap-2">
                          <label className="text-sm text-gray-700">Celková hmotnost:</label>
                          <input
                            type="number"
                            min="0"
                            value={totalBatchSize}
                            onChange={(e) => setTotalBatchSize(Number(e.target.value))}
                            placeholder="Celková hmotnost"
                            className="w-32 px-3 py-2 text-sm border border-indigo-300 bg-white text-gray-900 rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500 text-center"
                          />
                          <span className="text-sm text-gray-600">ml/g</span>
                        </div>
                      )}

                      {/* Target Days Coverage Mode */}
                      {controlMode === BatchPlanControlMode.TargetDaysCoverage && (
                        <div className="flex items-center gap-2">
                          <label className="text-sm text-gray-700">Cílová zásoba:</label>
                          <input
                            type="number"
                            min="1"
                            value={targetDaysCoverage}
                            onChange={(e) => setTargetDaysCoverage(Number(e.target.value))}
                            placeholder="Počet dní"
                            className="w-20 px-3 py-2 text-sm border border-indigo-300 bg-white text-gray-900 rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500 text-center"
                          />
                          <span className="text-sm text-gray-600">dní</span>
                        </div>
                      )}

                      {/* Calculate Button */}
                      <div className="flex-none ml-2">
                        <button
                          onClick={handleManualCalculate}
                          disabled={!selectedSemiproduct || batchPlanMutation.isPending}
                          className="px-4 py-2 bg-indigo-600 text-white text-sm font-medium rounded-md hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-indigo-500 disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2"
                        >
                          {batchPlanMutation.isPending ? (
                            <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white"></div>
                          ) : (
                            <Calculator className="w-4 h-4" />
                          )}
                          Přepočítat
                        </button>
                      </div>
                    </div>
                  </div>
                )}
              </div>
            </div>

            

            {/* Product Grid - Only show if semiproduct is selected */}
            {selectedSemiproduct && (
                <div className="bg-white rounded-lg shadow-sm border">
                  <div className="px-6 py-4 border-b">
                    <div className="flex items-center justify-between">
                      <div>
                        <h3 className="text-lg font-medium text-gray-900 flex items-center">
                          <Package className="w-5 h-5 text-green-500 mr-2" />
                          Velikosti produktů
                        </h3>
                        <p className="text-sm text-gray-600 mt-1">
                          Produkty vyráběné z polotovaru {selectedSemiproduct.productName}
                        </p>
                      </div>
                      
                      {/* Create Order Button */}
                      {response?.success && response.productSizes?.some(p => (p.recommendedUnitsToProduceHumanReadable || 0) > 0) && (
                        <button
                          onClick={handleCreateOrder}
                          disabled={createOrderMutation.isPending}
                          className="bg-emerald-600 text-white px-4 py-2 rounded-md text-sm font-medium hover:bg-emerald-700 focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 disabled:opacity-50 flex items-center gap-2"
                        >
                          {createOrderMutation.isPending ? (
                            <>
                              <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white"></div>
                              Vytváří se...
                            </>
                          ) : (
                            <>
                              <FileText className="h-4 w-4" />
                              Vytvořit zakázku
                            </>
                          )}
                        </button>
                      )}
                    </div>
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
                          <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Množství / Fixed</th>
                          <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Budoucí zásoba (dny)</th>
                        </tr>
                      </thead>
                      <tbody className="bg-white divide-y divide-gray-200">
                        {batchPlanMutation.isPending && selectedSemiproduct ? (
                          <tr>
                            <td colSpan={9} className="px-4 py-8 text-center text-gray-500">
                              <div className="flex items-center justify-center">
                                <div className="animate-spin rounded-full h-6 w-6 border-b-2 border-indigo-600"></div>
                                <span className="ml-2">Načítání dat...</span>
                              </div>
                            </td>
                          </tr>
                        ) : displayProductSizes.length === 0 && selectedSemiproduct ? (
                          <tr>
                            <td colSpan={9} className="px-4 py-8 text-center text-gray-500">
                              Žádné produkty nenalezeny
                            </td>
                          </tr>
                        ) : (
                          displayProductSizes.map((product) => (
                            <tr key={product.productCode} className={`hover:bg-gray-50 ${!product.enabled ? 'bg-gray-100 text-gray-500' : ''}`}>
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
                                {product.dailySales.toFixed(2)} ks
                              </td>
                              <td className="px-4 py-3">
                                <span className={`text-sm px-2 py-1 rounded-full ${
                                  product.currentCoverage > 2000
                                    ? 'bg-gray-100 text-gray-600'
                                    : product.currentCoverage < 7 
                                      ? 'bg-red-100 text-red-800' 
                                      : product.currentCoverage < 14 
                                        ? 'bg-yellow-100 text-yellow-800'
                                        : 'bg-green-100 text-green-800'
                                }`}>
                                  {product.currentCoverage > 2000 ? 'NA' : product.currentCoverage.toFixed(1)}
                                </span>
                              </td>
                              <td className="px-4 py-3">
                                <div className="flex items-center space-x-2">
                                  <input
                                    type="number"
                                    min="0"
                                    value={productConstraints.get(product.productCode || '')?.quantity ?? product.recommendedQuantity ?? 0}
                                    onChange={(e) => handleProductQuantityChange(product.productCode || '', parseInt(e.target.value) || 0)}
                                    readOnly={!(productConstraints.get(product.productCode || '')?.isFixed ?? false)}
                                    className={`w-16 px-2 py-1 text-sm border rounded focus:ring-indigo-500 focus:border-indigo-500 ${
                                      !(productConstraints.get(product.productCode || '')?.isFixed ?? false) 
                                        ? 'border-gray-300 bg-gray-50 text-gray-600 cursor-not-allowed' 
                                        : response?.success === false && (productConstraints.get(product.productCode || '')?.isFixed ?? false)
                                        ? 'border-red-300 bg-red-50 text-red-900' // Red styling for fixed products when there's an error
                                        : 'border-gray-300 bg-white text-gray-900'
                                    }`}
                                  />
                                  <span className="text-sm text-gray-500">ks</span>
                                  <input
                                    type="checkbox"
                                    checked={productConstraints.get(product.productCode || '')?.isFixed ?? false}
                                    onChange={(e) => handleProductFixedChange(product.productCode || '', e.target.checked, product.recommendedQuantity ?? 0)}
                                    className="h-4 w-4 text-indigo-600 focus:ring-indigo-500 border-gray-300 rounded"
                                    title="Fixovat množství"
                                  />
                                </div>
                              </td>
                              <td className="px-4 py-3">
                                <span className={`text-sm px-2 py-1 rounded-full ${
                                  product.futureCoverage > 2000
                                    ? 'bg-gray-100 text-gray-600'
                                    : product.futureCoverage < 7 
                                      ? 'bg-red-100 text-red-800' 
                                      : product.futureCoverage < 14 
                                        ? 'bg-yellow-100 text-yellow-800'
                                        : 'bg-green-100 text-green-800'
                                }`}>
                                  {product.futureCoverage > 2000 ? 'NA' : product.futureCoverage.toFixed(1)}
                                </span>
                              </td>
                            </tr>
                          ))
                        )}
                      </tbody>
                    </table>
                  </div>
                </div>
            )}
          </div>
        </div>
      </div>

      {/* Manufacture Order Detail Modal */}
      {showOrderModal && createdOrderId && (
        <ManufactureOrderDetail
          orderId={createdOrderId}
          isOpen={showOrderModal}
          onClose={() => {
            setShowOrderModal(false);
            setCreatedOrderId(null);
          }}
        />
      )}
    </div>
  );
};

export default BatchPlanningCalculator;