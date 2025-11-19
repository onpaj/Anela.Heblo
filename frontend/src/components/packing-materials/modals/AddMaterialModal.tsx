import React, { useState, useEffect } from "react";
import { X, Package, Loader } from "lucide-react";
import { useCreatePackingMaterial, ConsumptionType } from "../../../api/hooks/usePackingMaterials";

interface AddMaterialModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSuccess?: () => void;
}

const AddMaterialModal: React.FC<AddMaterialModalProps> = ({
  isOpen,
  onClose,
  onSuccess,
}) => {
  const [formData, setFormData] = useState({
    name: "",
    consumptionRate: "",
    consumptionType: ConsumptionType.PerProduct,
    currentQuantity: "",
  });
  const [errors, setErrors] = useState<Record<string, string>>({});

  const { mutate: createMaterial, isPending } = useCreatePackingMaterial();

  // Reset form when modal closes
  useEffect(() => {
    if (!isOpen) {
      setFormData({
        name: "",
        consumptionRate: "",
        consumptionType: ConsumptionType.PerProduct,
        currentQuantity: "",
      });
      setErrors({});
    }
  }, [isOpen]);

  const validateForm = () => {
    const newErrors: Record<string, string> = {};

    if (!formData.name.trim()) {
      newErrors.name = "Název materiálu je povinný";
    }

    if (!formData.consumptionRate || parseFloat(formData.consumptionRate) <= 0) {
      newErrors.consumptionRate = "Spotřeba musí být kladné číslo";
    }

    if (!formData.currentQuantity || parseFloat(formData.currentQuantity) < 0) {
      newErrors.currentQuantity = "Aktuální množství musí být nezáporné číslo";
    }

    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    
    if (!validateForm()) {
      return;
    }

    createMaterial({
      name: formData.name.trim(),
      consumptionRate: parseFloat(formData.consumptionRate),
      consumptionType: formData.consumptionType,
      currentQuantity: parseFloat(formData.currentQuantity),
    }, {
      onSuccess: () => {
        onSuccess?.();
        onClose();
      },
      onError: (error) => {
        setErrors({ general: `Chyba při vytváření materiálu: ${error.message}` });
      },
    });
  };

  const consumptionTypeOptions = [
    { value: ConsumptionType.PerOrder, label: "za zakázku" },
    { value: ConsumptionType.PerProduct, label: "za produkt" },
    { value: ConsumptionType.PerDay, label: "za den" },
  ];

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
      <div className="bg-white rounded-lg shadow-xl w-full max-w-md mx-4">
        {/* Header */}
        <div className="flex items-center justify-between p-6 border-b border-gray-200">
          <div className="flex items-center space-x-3">
            <Package className="h-6 w-6 text-indigo-600" />
            <h2 className="text-xl font-semibold text-gray-900">Přidat materiál</h2>
          </div>
          <button
            onClick={onClose}
            className="text-gray-400 hover:text-gray-600 transition-colors"
            disabled={isPending}
          >
            <X className="h-6 w-6" />
          </button>
        </div>

        {/* Form */}
        <form onSubmit={handleSubmit} className="p-6">
          {/* General Error */}
          {errors.general && (
            <div className="mb-4 bg-red-50 border border-red-200 text-red-700 px-3 py-2 rounded text-sm">
              {errors.general}
            </div>
          )}

          {/* Name Field */}
          <div className="mb-4">
            <label htmlFor="name" className="block text-sm font-medium text-gray-700 mb-1">
              Název materiálu *
            </label>
            <input
              id="name"
              type="text"
              value={formData.name}
              onChange={(e) => setFormData({ ...formData, name: e.target.value })}
              className={`w-full px-3 py-2 border rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500 ${
                errors.name ? 'border-red-500' : 'border-gray-300'
              }`}
              placeholder="např. Krabičky, Etikety, Bubble wrap"
              disabled={isPending}
            />
            {errors.name && <p className="text-red-500 text-xs mt-1">{errors.name}</p>}
          </div>

          {/* Consumption Rate Field */}
          <div className="mb-4">
            <label htmlFor="consumptionRate" className="block text-sm font-medium text-gray-700 mb-1">
              Spotřeba *
            </label>
            <input
              id="consumptionRate"
              type="number"
              step="0.01"
              min="0"
              value={formData.consumptionRate}
              onChange={(e) => setFormData({ ...formData, consumptionRate: e.target.value })}
              className={`w-full px-3 py-2 border rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500 ${
                errors.consumptionRate ? 'border-red-500' : 'border-gray-300'
              }`}
              placeholder="např. 1.5"
              disabled={isPending}
            />
            {errors.consumptionRate && <p className="text-red-500 text-xs mt-1">{errors.consumptionRate}</p>}
          </div>

          {/* Consumption Type Field */}
          <div className="mb-4">
            <label htmlFor="consumptionType" className="block text-sm font-medium text-gray-700 mb-1">
              Typ spotřeby *
            </label>
            <select
              id="consumptionType"
              value={formData.consumptionType}
              onChange={(e) => setFormData({ ...formData, consumptionType: parseInt(e.target.value) as ConsumptionType })}
              className="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500"
              disabled={isPending}
            >
              {consumptionTypeOptions.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </div>

          {/* Current Quantity Field */}
          <div className="mb-6">
            <label htmlFor="currentQuantity" className="block text-sm font-medium text-gray-700 mb-1">
              Aktuální množství *
            </label>
            <input
              id="currentQuantity"
              type="number"
              step="0.01"
              min="0"
              value={formData.currentQuantity}
              onChange={(e) => setFormData({ ...formData, currentQuantity: e.target.value })}
              className={`w-full px-3 py-2 border rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500 ${
                errors.currentQuantity ? 'border-red-500' : 'border-gray-300'
              }`}
              placeholder="např. 100"
              disabled={isPending}
            />
            {errors.currentQuantity && <p className="text-red-500 text-xs mt-1">{errors.currentQuantity}</p>}
          </div>

          {/* Buttons */}
          <div className="flex items-center justify-end space-x-3">
            <button
              type="button"
              onClick={onClose}
              disabled={isPending}
              className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-indigo-500 disabled:opacity-50"
            >
              Zrušit
            </button>
            <button
              type="submit"
              disabled={isPending}
              className="px-4 py-2 text-sm font-medium text-white bg-indigo-600 border border-transparent rounded-md hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-indigo-500 disabled:opacity-50 flex items-center"
            >
              {isPending ? (
                <>
                  <Loader className="h-4 w-4 mr-2 animate-spin" />
                  Vytvářím...
                </>
              ) : (
                "Přidat materiál"
              )}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};

export default AddMaterialModal;