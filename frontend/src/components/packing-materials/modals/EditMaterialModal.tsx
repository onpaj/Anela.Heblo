import React, { useState, useEffect } from "react";
import { X, Edit, Loader } from "lucide-react";
import { useUpdatePackingMaterial, ConsumptionType, PackingMaterialDto } from "../../../api/hooks/usePackingMaterials";

interface EditMaterialModalProps {
  isOpen: boolean;
  onClose: () => void;
  material: PackingMaterialDto | null;
  onSuccess?: () => void;
}

const EditMaterialModal: React.FC<EditMaterialModalProps> = ({
  isOpen,
  onClose,
  material,
  onSuccess,
}) => {
  const [formData, setFormData] = useState({
    name: "",
    consumptionRate: "",
    consumptionType: ConsumptionType.PerProduct,
  });
  const [errors, setErrors] = useState<Record<string, string>>({});

  const { mutate: updateMaterial, isPending } = useUpdatePackingMaterial();

  // Initialize form with material data
  useEffect(() => {
    if (isOpen && material) {
      setFormData({
        name: material.name!,
        consumptionRate: material.consumptionRate!.toString(),
        consumptionType: material.consumptionType!,
      });
      setErrors({});
    }
  }, [isOpen, material]);

  const validateForm = () => {
    const newErrors: Record<string, string> = {};

    if (!formData.name.trim()) {
      newErrors.name = "Název materiálu je povinný";
    }

    if (!formData.consumptionRate || parseFloat(formData.consumptionRate) <= 0) {
      newErrors.consumptionRate = "Spotřeba musí být kladné číslo";
    }

    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    
    if (!validateForm() || !material) {
      return;
    }

    updateMaterial({
      id: material.id!,
      name: formData.name.trim(),
      consumptionRate: parseFloat(formData.consumptionRate),
      consumptionType: formData.consumptionType,
    }, {
      onSuccess: () => {
        onSuccess?.();
        onClose();
      },
      onError: (error) => {
        setErrors({ general: `Chyba při úpravě materiálu: ${error.message}` });
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
      <div className="bg-white dark:bg-graphite-surface rounded-lg shadow-xl dark:shadow-soft-dark w-full max-w-md mx-4">
        {/* Header */}
        <div className="flex items-center justify-between p-6 border-b border-gray-200 dark:border-graphite-border">
          <div className="flex items-center space-x-3">
            <Edit className="h-6 w-6 text-indigo-600 dark:text-graphite-accent" />
            <h2 className="text-xl font-semibold text-gray-900 dark:text-graphite-text">Upravit materiál</h2>
          </div>
          <button
            onClick={onClose}
            className="text-gray-400 dark:text-graphite-faint hover:text-gray-600 dark:hover:text-graphite-muted transition-colors"
            disabled={isPending}
          >
            <X className="h-6 w-6" />
          </button>
        </div>

        {/* Form */}
        <form onSubmit={handleSubmit} className="p-6">
          {/* General Error */}
          {errors.general && (
            <div className="mb-4 bg-red-50 dark:bg-red-900/30 border border-red-200 dark:border-red-900/40 text-red-700 dark:text-red-300 px-3 py-2 rounded text-sm">
              {errors.general}
            </div>
          )}

          {/* Name Field */}
          <div className="mb-4">
            <label htmlFor="name" className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-1">
              Název materiálu *
            </label>
            <input
              id="name"
              type="text"
              value={formData.name}
              onChange={(e) => setFormData({ ...formData, name: e.target.value })}
              className={`w-full px-3 py-2 border rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500 dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint ${
                errors.name ? 'border-red-500' : 'border-gray-300 dark:border-graphite-border'
              }`}
              placeholder="např. Krabičky, Etikety, Bubble wrap"
              disabled={isPending}
            />
            {errors.name && <p className="text-red-500 dark:text-red-400 text-xs mt-1">{errors.name}</p>}
          </div>

          {/* Consumption Rate Field */}
          <div className="mb-4">
            <label htmlFor="consumptionRate" className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-1">
              Spotřeba *
            </label>
            <input
              id="consumptionRate"
              type="number"
              step="0.01"
              min="0"
              value={formData.consumptionRate}
              onChange={(e) => setFormData({ ...formData, consumptionRate: e.target.value })}
              className={`w-full px-3 py-2 border rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500 dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint ${
                errors.consumptionRate ? 'border-red-500' : 'border-gray-300 dark:border-graphite-border'
              }`}
              placeholder="např. 1.5"
              disabled={isPending}
            />
            {errors.consumptionRate && <p className="text-red-500 dark:text-red-400 text-xs mt-1">{errors.consumptionRate}</p>}
          </div>

          {/* Consumption Type Field */}
          <div className="mb-6">
            <label htmlFor="consumptionType" className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-1">
              Typ spotřeby *
            </label>
            <select
              id="consumptionType"
              value={formData.consumptionType}
              onChange={(e) => setFormData({ ...formData, consumptionType: e.target.value as ConsumptionType })}
              className="w-full px-3 py-2 border border-gray-300 dark:border-graphite-border rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500 dark:bg-graphite-surface-2 dark:text-graphite-text"
              disabled={isPending}
            >
              {consumptionTypeOptions.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </div>

          {/* Current Quantity Info */}
          {material && (
            <div className="mb-6 p-3 bg-gray-50 dark:bg-graphite-surface-2 rounded-md">
              <p className="text-sm text-gray-600 dark:text-graphite-muted">
                <strong>Aktuální množství:</strong> {material.currentQuantity?.toLocaleString('cs-CZ')}
              </p>
              <p className="text-xs text-gray-500 dark:text-graphite-muted mt-1">
                Množství lze upravit pomocí tlačítka "Upravit množství" v seznamu materiálů.
              </p>
            </div>
          )}

          {/* Buttons */}
          <div className="flex items-center justify-end space-x-3">
            <button
              type="button"
              onClick={onClose}
              disabled={isPending}
              className="px-4 py-2 text-sm font-medium text-gray-700 dark:text-graphite-muted bg-white dark:bg-graphite-surface-2 border border-gray-300 dark:border-graphite-border rounded-md hover:bg-gray-50 dark:hover:bg-white/5 focus:outline-none focus:ring-2 focus:ring-indigo-500 disabled:opacity-50"
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
                  Ukládám...
                </>
              ) : (
                "Uložit změny"
              )}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};

export default EditMaterialModal;