import React, { useState, useEffect } from "react";
import { X, Package, Loader } from "lucide-react";
import { useUpdatePackingMaterialQuantity, PackingMaterialDto } from "../../../api/hooks/usePackingMaterials";

interface UpdateQuantityModalProps {
  isOpen: boolean;
  onClose: () => void;
  material: PackingMaterialDto | null;
  onSuccess?: () => void;
}

const UpdateQuantityModal: React.FC<UpdateQuantityModalProps> = ({
  isOpen,
  onClose,
  material,
  onSuccess,
}) => {
  const [formData, setFormData] = useState({
    newQuantity: "",
    date: "",
  });
  const [errors, setErrors] = useState<Record<string, string>>({});

  const { mutate: updateQuantity, isPending } = useUpdatePackingMaterialQuantity();

  // Initialize form when modal opens
  useEffect(() => {
    if (isOpen && material) {
      const today = new Date().toISOString().split('T')[0];
      setFormData({
        newQuantity: material.currentQuantity.toString(),
        date: today,
      });
      setErrors({});
    }
  }, [isOpen, material]);

  const validateForm = () => {
    const newErrors: Record<string, string> = {};

    if (!formData.newQuantity || parseFloat(formData.newQuantity) < 0) {
      newErrors.newQuantity = "Množství musí být nezáporné číslo";
    }

    if (!formData.date) {
      newErrors.date = "Datum je povinné";
    }

    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    
    if (!validateForm() || !material) {
      return;
    }

    updateQuantity({
      id: material.id,
      newQuantity: parseFloat(formData.newQuantity),
      date: formData.date, // Will be converted to DateOnly on backend
    }, {
      onSuccess: () => {
        onSuccess?.();
        onClose();
      },
      onError: (error) => {
        setErrors({ general: `Chyba při aktualizaci množství: ${error.message}` });
      },
    });
  };

  const formatQuantity = (quantity: number) => {
    return quantity.toLocaleString('cs-CZ', {
      minimumFractionDigits: 0,
      maximumFractionDigits: 2
    });
  };

  if (!isOpen || !material) return null;

  const currentQuantity = material.currentQuantity;
  const newQuantity = parseFloat(formData.newQuantity) || 0;
  const difference = newQuantity - currentQuantity;

  return (
    <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
      <div className="bg-white rounded-lg shadow-xl w-full max-w-md mx-4">
        {/* Header */}
        <div className="flex items-center justify-between p-6 border-b border-gray-200">
          <div className="flex items-center space-x-3">
            <Package className="h-6 w-6 text-indigo-600" />
            <h2 className="text-xl font-semibold text-gray-900">Upravit množství</h2>
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

          {/* Material Info */}
          <div className="mb-4 p-4 bg-gray-50 rounded-md">
            <h3 className="text-sm font-medium text-gray-900 mb-2">{material.name}</h3>
            <div className="grid grid-cols-2 gap-4 text-sm">
              <div>
                <span className="text-gray-500">Aktuální množství:</span>
                <div className="font-medium">{formatQuantity(currentQuantity)}</div>
              </div>
              <div>
                <span className="text-gray-500">Typ spotřeby:</span>
                <div className="font-medium">{material.consumptionTypeText}</div>
              </div>
            </div>
          </div>

          {/* New Quantity Field */}
          <div className="mb-4">
            <label htmlFor="newQuantity" className="block text-sm font-medium text-gray-700 mb-1">
              Nové množství *
            </label>
            <input
              id="newQuantity"
              type="number"
              step="0.01"
              min="0"
              value={formData.newQuantity}
              onChange={(e) => setFormData({ ...formData, newQuantity: e.target.value })}
              className={`w-full px-3 py-2 border rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500 ${
                errors.newQuantity ? 'border-red-500' : 'border-gray-300'
              }`}
              placeholder="např. 150"
              disabled={isPending}
            />
            {errors.newQuantity && <p className="text-red-500 text-xs mt-1">{errors.newQuantity}</p>}
          </div>

          {/* Date Field */}
          <div className="mb-4">
            <label htmlFor="date" className="block text-sm font-medium text-gray-700 mb-1">
              Datum změny *
            </label>
            <input
              id="date"
              type="date"
              value={formData.date}
              onChange={(e) => setFormData({ ...formData, date: e.target.value })}
              className={`w-full px-3 py-2 border rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500 ${
                errors.date ? 'border-red-500' : 'border-gray-300'
              }`}
              disabled={isPending}
            />
            {errors.date && <p className="text-red-500 text-xs mt-1">{errors.date}</p>}
          </div>

          {/* Change Summary */}
          {formData.newQuantity && !isNaN(newQuantity) && (
            <div className="mb-6 p-3 bg-blue-50 rounded-md">
              <h4 className="text-sm font-medium text-blue-900 mb-2">Souhrn změny</h4>
              <div className="text-sm text-blue-800">
                <div className="flex justify-between">
                  <span>Aktuální:</span>
                  <span>{formatQuantity(currentQuantity)}</span>
                </div>
                <div className="flex justify-between">
                  <span>Nové:</span>
                  <span>{formatQuantity(newQuantity)}</span>
                </div>
                <hr className="my-2 border-blue-200" />
                <div className="flex justify-between font-medium">
                  <span>Změna:</span>
                  <span className={difference >= 0 ? 'text-green-600' : 'text-red-600'}>
                    {difference >= 0 ? '+' : ''}{formatQuantity(difference)}
                  </span>
                </div>
              </div>
            </div>
          )}

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
                  Ukládám...
                </>
              ) : (
                "Uložit množství"
              )}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};

export default UpdateQuantityModal;