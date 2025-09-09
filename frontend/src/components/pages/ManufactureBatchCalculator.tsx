import React, { useState } from "react";
import { Calculator, RotateCcw, Package, Beaker } from "lucide-react";
import CatalogAutocomplete from "../common/CatalogAutocomplete";
import { CatalogItemDto, ProductType } from "../../api/generated/api-client";
import { useManufactureBatch } from "../../api/hooks/useManufactureBatch";

interface CalculationResult {
  productCode: string;
  productName: string;
  originalBatchSize: number;
  newBatchSize: number;
  scaleFactor: number;
  ingredients: CalculatedIngredient[];
}

interface CalculatedIngredient {
  productCode: string;
  productName: string;
  originalAmount: number;
  calculatedAmount: number;
  price: number;
}

type CalculationMode = "batch-size" | "ingredient";

const ManufactureBatchCalculator: React.FC = () => {
  const [selectedProduct, setSelectedProduct] = useState<CatalogItemDto | null>(
    null,
  );
  const [calculationMode, setCalculationMode] =
    useState<CalculationMode>("batch-size");

  // Batch size calculation state
  const [desiredBatchSize, setDesiredBatchSize] = useState<string>("");

  // Ingredient calculation state
  const [selectedIngredientCode, setSelectedIngredientCode] =
    useState<string>("");
  const [desiredIngredientAmount, setDesiredIngredientAmount] =
    useState<string>("");

  // Results state
  const [calculationResult, setCalculationResult] =
    useState<CalculationResult | null>(null);
  const [template, setTemplate] = useState<any>(null);

  const {
    getBatchTemplate,
    calculateBySize,
    calculateByIngredient,
    isLoading,
  } = useManufactureBatch();

  const handleProductSelect = async (product: CatalogItemDto | null) => {
    setSelectedProduct(product);
    setCalculationResult(null);
    setTemplate(null);
    setSelectedIngredientCode("");

    if (product) {
      try {
        const templateData = await getBatchTemplate(product.productCode || "");
        if (templateData.success) {
          setTemplate(templateData);
        }
      } catch (error) {
        console.error("Error loading template:", error);
      }
    }
  };

  const handleCalculateBySize = async () => {
    if (!selectedProduct || !desiredBatchSize) return;

    try {
      const result = await calculateBySize(
        selectedProduct.productCode || "",
        parseFloat(desiredBatchSize),
      );

      if (result.success) {
        setCalculationResult({
          productCode: result.productCode,
          productName: result.productName,
          originalBatchSize: result.originalBatchSize,
          newBatchSize: result.newBatchSize,
          scaleFactor: result.scaleFactor,
          ingredients: result.ingredients,
        });
      }
    } catch (error) {
      console.error("Error calculating by size:", error);
    }
  };

  const handleCalculateByIngredient = async () => {
    if (!selectedProduct || !selectedIngredientCode || !desiredIngredientAmount)
      return;

    try {
      const result = await calculateByIngredient(
        selectedProduct.productCode || "",
        selectedIngredientCode,
        parseFloat(desiredIngredientAmount),
      );

      if (result.success) {
        setCalculationResult({
          productCode: result.productCode,
          productName: result.productName,
          originalBatchSize: result.originalBatchSize,
          newBatchSize: result.newBatchSize,
          scaleFactor: result.scaleFactor,
          ingredients: result.ingredients,
        });
      }
    } catch (error) {
      console.error("Error calculating by ingredient:", error);
    }
  };

  const resetCalculation = () => {
    setCalculationResult(null);
    setDesiredBatchSize("");
    setDesiredIngredientAmount("");
    setSelectedIngredientCode("");
  };

  return (
    <div className="flex flex-col h-full w-full">
      {/* Header */}
      <div className="flex-shrink-0 mb-3">
        <h1 className="text-lg font-semibold text-gray-900 flex items-center gap-2">
          <Calculator className="h-5 w-5" />
          Kalkulačka dávek pro výrobu
        </h1>
        <p className="text-sm text-gray-600 mt-1">
          Přepočítejte množství ingrediencí podle požadované velikosti dávky
          nebo množství konkrétní ingredience
        </p>
      </div>

      {/* Controls */}
      <div className="flex-shrink-0 bg-white shadow rounded-lg p-4 mb-4">
        <div className="space-y-4">
          {/* Product Selection */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-2">
              Vyberte polotovar
            </label>
            <CatalogAutocomplete
              value={selectedProduct}
              onSelect={handleProductSelect}
              placeholder="Vyhledejte polotovar..."
              productTypes={[ProductType.Semiproduct]}
              className="max-w-md"
            />
          </div>

          {/* Calculation Mode Toggle */}
          {template && (
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-2">
                Způsob výpočtu
              </label>
              <div className="flex space-x-4">
                <label className="flex items-center">
                  <input
                    type="radio"
                    name="calculationMode"
                    value="batch-size"
                    checked={calculationMode === "batch-size"}
                    onChange={(e) =>
                      setCalculationMode(e.target.value as CalculationMode)
                    }
                    className="mr-2"
                  />
                  <span className="text-sm">Podle velikosti dávky</span>
                </label>
                <label className="flex items-center">
                  <input
                    type="radio"
                    name="calculationMode"
                    value="ingredient"
                    checked={calculationMode === "ingredient"}
                    onChange={(e) =>
                      setCalculationMode(e.target.value as CalculationMode)
                    }
                    className="mr-2"
                  />
                  <span className="text-sm">Podle ingredience</span>
                </label>
              </div>
            </div>
          )}

          {/* Batch Size Calculation */}
          {template && calculationMode === "batch-size" && (
            <div className="flex gap-4 items-end">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Požadovaná velikost dávky (g)
                </label>
                <input
                  type="number"
                  step="0.01"
                  min="0"
                  value={desiredBatchSize}
                  onChange={(e) => setDesiredBatchSize(e.target.value)}
                  className="border border-gray-300 rounded-md px-3 py-2 text-sm w-32"
                  placeholder="0.00"
                />
              </div>
              <button
                onClick={handleCalculateBySize}
                disabled={!desiredBatchSize || isLoading}
                className="bg-indigo-600 text-white px-4 py-2 rounded-md text-sm font-medium hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2"
              >
                <Calculator className="h-4 w-4" />
                Vypočítat
              </button>
              <button
                onClick={resetCalculation}
                className="bg-gray-500 text-white px-4 py-2 rounded-md text-sm font-medium hover:bg-gray-600 flex items-center gap-2"
              >
                <RotateCcw className="h-4 w-4" />
                Reset
              </button>
            </div>
          )}

          {/* Ingredient Calculation */}
          {template && calculationMode === "ingredient" && (
            <div className="flex gap-4 items-end">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Ingredience
                </label>
                <select
                  value={selectedIngredientCode}
                  onChange={(e) => setSelectedIngredientCode(e.target.value)}
                  className="border border-gray-300 rounded-md px-3 py-2 text-sm w-64"
                >
                  <option value="">Vyberte ingredienci...</option>
                  {template.ingredients?.map((ingredient: any) => (
                    <option
                      key={ingredient.productCode}
                      value={ingredient.productCode}
                    >
                      {ingredient.productName} ({ingredient.productCode})
                    </option>
                  ))}
                </select>
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Požadované množství (g)
                </label>
                <input
                  type="number"
                  step="0.01"
                  min="0"
                  value={desiredIngredientAmount}
                  onChange={(e) => setDesiredIngredientAmount(e.target.value)}
                  className="border border-gray-300 rounded-md px-3 py-2 text-sm w-32"
                  placeholder="0.00"
                />
              </div>
              <button
                onClick={handleCalculateByIngredient}
                disabled={
                  !selectedIngredientCode ||
                  !desiredIngredientAmount ||
                  isLoading
                }
                className="bg-indigo-600 text-white px-4 py-2 rounded-md text-sm font-medium hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2"
              >
                <Calculator className="h-4 w-4" />
                Vypočítat
              </button>
              <button
                onClick={resetCalculation}
                className="bg-gray-500 text-white px-4 py-2 rounded-md text-sm font-medium hover:bg-gray-600 flex items-center gap-2"
              >
                <RotateCcw className="h-4 w-4" />
                Reset
              </button>
            </div>
          )}
        </div>
      </div>

      {/* Template Display */}
      {template && !calculationResult && (
        <div className="flex-shrink-0 bg-white shadow rounded-lg p-4 mb-4">
          <h3 className="text-md font-medium text-gray-900 mb-3 flex items-center gap-2">
            <Package className="h-4 w-4" />
            Originální recept
          </h3>
          <p className="text-sm text-gray-600 mb-3">
            <strong>{template.productName}</strong> ({template.productCode}) -
            Dávka: {template.batchSize}g
          </p>
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50">
                <tr>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Ingredience
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Kód
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Množství (g)
                  </th>
                </tr>
              </thead>
              <tbody className="bg-white divide-y divide-gray-200">
                {template.ingredients?.map((ingredient: any, index: number) => (
                  <tr
                    key={ingredient.productCode}
                    className={index % 2 === 0 ? "bg-white" : "bg-gray-50"}
                  >
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                      {ingredient.productName}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500 font-mono">
                      {ingredient.productCode}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                      {ingredient.amount.toFixed(2)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Results */}
      {calculationResult && (
        <div className="flex-1 bg-white shadow rounded-lg overflow-hidden flex flex-col min-h-0">
          <div className="p-4 border-b border-gray-200">
            <h3 className="text-md font-medium text-gray-900 flex items-center gap-2">
              <Beaker className="h-4 w-4" />
              Přepočítaný recept
            </h3>
            <div className="mt-2 text-sm text-gray-600">
              <p>
                <strong>Produkt:</strong> {calculationResult.productName} (
                {calculationResult.productCode})
              </p>
              <p>
                <strong>Velikost dávky:</strong>{" "}
                {calculationResult.originalBatchSize}g →{" "}
                {calculationResult.newBatchSize}g
              </p>
              <p>
                <strong>Faktor přepočtu:</strong>{" "}
                {calculationResult.scaleFactor.toFixed(3)}x
              </p>
            </div>
          </div>

          <div className="flex-1 overflow-auto">
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50 sticky top-0">
                <tr>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Ingredience
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Kód
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Původní množství
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Přepočítané množství
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Rozdíl
                  </th>
                </tr>
              </thead>
              <tbody className="bg-white divide-y divide-gray-200">
                {calculationResult.ingredients.map((ingredient, index) => {
                  const difference =
                    ingredient.calculatedAmount - ingredient.originalAmount;
                  const isIncrease = difference > 0;

                  return (
                    <tr
                      key={ingredient.productCode}
                      className={index % 2 === 0 ? "bg-white" : "bg-gray-50"}
                    >
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                        {ingredient.productName}
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500 font-mono">
                        {ingredient.productCode}
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                        {ingredient.originalAmount.toFixed(2)}g
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                        {ingredient.calculatedAmount.toFixed(2)}g
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm">
                        <span
                          className={`inline-flex items-center ${
                            isIncrease
                              ? "text-green-600"
                              : difference < 0
                                ? "text-red-600"
                                : "text-gray-500"
                          }`}
                        >
                          {isIncrease ? "+" : ""}
                          {difference.toFixed(2)}g
                        </span>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Empty State */}
      {!template && selectedProduct && (
        <div className="flex-1 bg-white shadow rounded-lg flex items-center justify-center">
          <div className="text-center text-gray-500">
            <Package className="h-12 w-12 mx-auto mb-4 text-gray-400" />
            <p className="text-lg font-medium">Načítání receptu...</p>
            <p className="text-sm">
              Prosím počkejte, načítáme data pro vybraný produkt.
            </p>
          </div>
        </div>
      )}

      {!selectedProduct && (
        <div className="flex-1 bg-white shadow rounded-lg flex items-center justify-center">
          <div className="text-center text-gray-500">
            <Calculator className="h-12 w-12 mx-auto mb-4 text-gray-400" />
            <p className="text-lg font-medium">Začněte výběrem polotovaru</p>
            <p className="text-sm">
              Vyberte polotovar ze seznamu a začněte počítat dávky.
            </p>
          </div>
        </div>
      )}
    </div>
  );
};

export default ManufactureBatchCalculator;
