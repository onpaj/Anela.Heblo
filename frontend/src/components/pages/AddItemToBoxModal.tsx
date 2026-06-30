import React, { useState } from "react";
import { X, Package, Loader } from "lucide-react";
import { getAuthenticatedApiClient } from "../../api/client";
import MaterialAutocomplete from "../common/MaterialAutocomplete";
import { MaterialForPurchaseDto } from "../../api/hooks/useMaterials";
import { AddItemToBoxRequest } from "../../api/generated/api-client";

interface AddItemToBoxModalProps {
  isOpen: boolean;
  onClose: () => void;
  boxId: number | null;
  onSuccess: () => void;
}

const AddItemToBoxModal: React.FC<AddItemToBoxModalProps> = ({
  isOpen,
  onClose,
  boxId,
  onSuccess,
}) => {
  const [selectedProduct, setSelectedProduct] =
    useState<MaterialForPurchaseDto | null>(null);
  const [amount, setAmount] = useState("");
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!boxId) return;

    setError(null); // Clear previous errors

    if (
      !selectedProduct ||
      !selectedProduct.productCode ||
      !selectedProduct.productName
    ) {
      setError("Produkt je povinný");
      return;
    }

    const numAmount = parseFloat(amount);
    if (isNaN(numAmount) || numAmount <= 0) {
      setError("Množství musí být kladné číslo");
      return;
    }

    setIsLoading(true);

    try {
      const apiClient = await getAuthenticatedApiClient();
      const request = new AddItemToBoxRequest({
        productCode: selectedProduct.productCode,
        productName: selectedProduct.productName,
        amount: numAmount,
      });
      const response = await apiClient.transportBox_AddItemToBox(
        boxId,
        request,
      );

      if (response.success) {
        onSuccess();
        // Reset form
        setSelectedProduct(null);
        setAmount("");
        setError(null);
        onClose();
      } else {
        // Handle API errors
        if (response.errorCode) {
          setError("Došlo k chybě při přidávání položky.");
        } else {
          setError("Došlo k chybě při přidávání položky.");
        }
      }
    } catch (err) {
      // Network errors or other exceptions
      console.error("Error adding item to box:", err);
      if (err instanceof Error && err.message.includes("Network")) {
        setError("Chyba připojení. Zkontrolujte internetové připojení.");
      } else {
        setError("Chyba připojení. Zkontrolujte internetové připojení.");
      }
    } finally {
      setIsLoading(false);
    }
  };

  const handleClose = () => {
    if (!isLoading) {
      setSelectedProduct(null);
      setAmount("");
      setError(null);
      onClose();
    }
  };

  const handleProductSelect = (product: MaterialForPurchaseDto | null) => {
    setSelectedProduct(product);
    if (error) setError(null); // Clear error when user selects product
  };

  if (!isOpen || !boxId) return null;

  return (
    <div className="fixed inset-0 bg-gray-500 bg-opacity-75 flex items-center justify-center z-50">
      <div className="bg-white dark:bg-graphite-surface rounded-lg shadow-xl dark:shadow-soft-dark max-w-md w-full mx-4">
        <div className="flex items-center justify-between p-4 border-b border-gray-200 dark:border-graphite-border">
          <div className="flex items-center">
            <Package className="h-5 w-5 text-indigo-600 dark:text-graphite-accent mr-2" />
            <h2 className="text-lg font-medium text-gray-900 dark:text-graphite-text">
              Přidání položky do boxu
            </h2>
          </div>
          <button
            onClick={handleClose}
            disabled={isLoading}
            aria-label="close"
            className="text-gray-400 dark:text-graphite-faint hover:text-gray-600 disabled:opacity-50"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        <form
          onSubmit={handleSubmit}
          className="p-4"
          onKeyDown={(e) => {
            if (
              e.key === "Enter" &&
              e.target instanceof HTMLInputElement &&
              e.target.type === "number"
            ) {
              e.preventDefault();
              handleSubmit(e as any);
            }
          }}
        >
          {error && (
            <div className="mb-4 p-3 bg-red-100 dark:bg-red-900/30 border border-red-300 dark:border-graphite-border text-red-700 dark:text-red-300 rounded-md text-sm">
              {error}
            </div>
          )}
          <div className="mb-4">
            <label
              htmlFor="product"
              className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-2"
            >
              Produkt/Materiál
            </label>
            <MaterialAutocomplete
              value={selectedProduct}
              onSelect={handleProductSelect}
              disabled={isLoading}
              placeholder="Zadejte název nebo kód produktu..."
            />
          </div>

          <div className="mb-4">
            <label
              htmlFor="amount"
              className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-2"
            >
              Množství
            </label>
            <input
              type="number"
              id="amount"
              value={amount}
              onChange={(e) => {
                setAmount(e.target.value);
                if (error) setError(null); // Clear error when user types amount
              }}
              disabled={isLoading}
              placeholder="0"
              min="0.01"
              step="0.01"
              className="w-full border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent disabled:opacity-50 disabled:cursor-not-allowed"
            />
          </div>

          <div className="flex justify-end space-x-3">
            <button
              type="button"
              onClick={handleClose}
              disabled={isLoading}
              className="px-4 py-2 text-sm font-medium text-gray-700 dark:text-graphite-muted bg-white dark:bg-graphite-surface border border-gray-300 dark:border-graphite-border rounded-md hover:bg-gray-50 dark:hover:bg-white/5 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              Zrušit
            </button>
            <button
              type="submit"
              disabled={isLoading}
              className="px-4 py-2 text-sm font-medium text-white bg-indigo-600 border border-transparent rounded-md hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed flex items-center"
            >
              {isLoading && <Loader className="h-4 w-4 mr-2 animate-spin" />}
              Přidat položku
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};

export default AddItemToBoxModal;
