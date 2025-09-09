import React, { useState } from "react";
import { X, Save, Package } from "lucide-react";
import {
  useCreatePurchaseOrderMutation,
  useUpdatePurchaseOrderMutation,
  usePurchaseOrderDetailQuery,
} from "../../api/hooks/usePurchaseOrders";
import {
  PurchaseOrderLineDto,
  ContactVia,
  SupplierDto,
} from "../../api/generated/api-client";
import { MaterialForPurchaseDto } from "../../api/hooks/useMaterials";
import {
  PurchaseOrderFormProps,
  FormData,
} from "../purchase-orders/form/PurchaseOrderTypes";
import {
  validateForm,
  clearFieldError,
} from "../purchase-orders/form/PurchaseOrderValidation";
import {
  createInitialFormData,
  createDefaultLine,
  calculateLineTotal,
  roundUnitPrice,
  createPurchaseOrderRequest,
  updatePurchaseOrderRequest,
  transformExistingOrderData,
} from "../purchase-orders/form/PurchaseOrderHelpers";
import PurchaseOrderHeader from "../purchase-orders/form/PurchaseOrderHeader";
import PurchaseOrderLines from "../purchase-orders/form/PurchaseOrderLines";
import MaterialResolver from "../purchase-orders/form/MaterialResolver";

const PurchaseOrderForm: React.FC<PurchaseOrderFormProps> = ({
  isOpen,
  onClose,
  onSuccess,
  editOrderId,
}) => {
  const isEditMode = !!editOrderId;

  const [formData, setFormData] = useState<FormData>(createInitialFormData());
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [pendingMaterialResolutions, setPendingMaterialResolutions] = useState<
    Set<number>
  >(new Set());

  const createMutation = useCreatePurchaseOrderMutation();
  const updateMutation = useUpdatePurchaseOrderMutation();

  // Fetch existing order data when in edit mode
  const { data: existingOrderData } = usePurchaseOrderDetailQuery(
    editOrderId || 0,
  );

  // Callback to handle when materials are resolved for existing purchase order lines
  const handleMaterialResolved = React.useCallback(
    (lineIndex: number, material: MaterialForPurchaseDto | null) => {
      setFormData((prev) => {
        const newLines = [...prev.lines];
        if (lineIndex < newLines.length) {
          newLines[lineIndex] = Object.assign(new PurchaseOrderLineDto(), {
            ...newLines[lineIndex],
            selectedMaterial: material,
            materialName:
              material?.productName || newLines[lineIndex].materialName,
          });
        }
        return { ...prev, lines: newLines };
      });

      // Remove this line from pending resolutions
      setPendingMaterialResolutions((prev) => {
        const newSet = new Set(prev);
        newSet.delete(lineIndex);
        return newSet;
      });
    },
    [],
  );

  // Load existing data in edit mode
  React.useEffect(() => {
    if (isEditMode && existingOrderData && isOpen) {
      const transformedData = transformExistingOrderData(existingOrderData);
      setFormData(transformedData);

      // Track which lines need material resolution
      if (existingOrderData.lines && existingOrderData.lines.length > 0) {
        const pendingResolutions = new Set<number>();
        existingOrderData.lines.forEach((line, index) => {
          // Only resolve materials that have a valid materialId (not temp ones)
          if (line.materialId && !line.materialId.startsWith("temp-")) {
            pendingResolutions.add(index);
          }
        });
        setPendingMaterialResolutions(pendingResolutions);
      }
    } else if (!isEditMode && isOpen) {
      // Reset to default state for create mode
      setFormData(createInitialFormData());
      setPendingMaterialResolutions(new Set());
    }
  }, [isEditMode, existingOrderData, isOpen]);

  // Add keyboard event listener for Esc key
  React.useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape" && isOpen) {
        onClose();
      }
    };

    if (isOpen) {
      document.addEventListener("keydown", handleKeyDown);
    }

    return () => {
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [isOpen, onClose]);

  if (!isOpen) {
    return null;
  }

  const handleBackdropClick = (e: React.MouseEvent) => {
    if (e.target === e.currentTarget) {
      onClose();
    }
  };

  const handleValidateForm = (): boolean => {
    const newErrors = validateForm(formData);
    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();

    if (!handleValidateForm()) {
      return;
    }

    setIsSubmitting(true);

    try {
      if (isEditMode && editOrderId) {
        const request = updatePurchaseOrderRequest(formData, editOrderId);
        const response = await updateMutation.mutateAsync({
          id: editOrderId,
          request,
        });

        if (onSuccess && response.id) {
          onSuccess(response.id);
        }
      } else {
        const request = createPurchaseOrderRequest(formData);
        const response = await createMutation.mutateAsync(request);

        if (onSuccess && response.id) {
          onSuccess(response.id);
        }
      }

      onClose();
    } catch (error) {
      console.error(
        `Failed to ${isEditMode ? "update" : "create"} purchase order:`,
        error,
      );
      setErrors({
        submit: `Nepodařilo se ${isEditMode ? "upravit" : "vytvořit"} objednávku. Zkuste to znovu.`,
      });
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleInputChange = (
    field: keyof FormData,
    value: string | ContactVia | null | SupplierDto,
  ) => {
    setFormData((prev) => ({ ...prev, [field]: value }));
    // Clear error when user starts typing
    if (errors[field]) {
      setErrors((prev) => clearFieldError(prev, field));
    }
  };

  const handleSupplierSelect = (supplier: SupplierDto | null) => {
    setFormData((prev) => ({ ...prev, selectedSupplier: supplier }));
    // Clear supplier error when a supplier is selected
    if (supplier && errors.selectedSupplier) {
      setErrors((prev) => clearFieldError(prev, "selectedSupplier"));
    }
  };

  const addLine = () => {
    setFormData((prev) => ({
      ...prev,
      lines: [...prev.lines, createDefaultLine()],
    }));
  };

  const removeLine = (index: number) => {
    setFormData((prev) => {
      const newLines = prev.lines.filter((_, i) => i !== index);
      // Always keep at least one empty row
      if (newLines.length === 0) {
        newLines.push(createDefaultLine());
      }
      return { ...prev, lines: newLines };
    });
  };

  const updateLine = (
    index: number,
    field: keyof PurchaseOrderLineDto,
    value: string | number,
  ) => {
    setFormData((prev) => {
      const newLines = [...prev.lines];

      // Apply rounding for unit price
      if (field === "unitPrice" && typeof value === "number") {
        value = roundUnitPrice(value);
      }

      newLines[index] = Object.assign(new PurchaseOrderLineDto(), {
        ...newLines[index],
        [field]: value,
      });

      // Recalculate line total with proper rounding
      if (field === "quantity" || field === "unitPrice") {
        newLines[index].lineTotal = calculateLineTotal(
          newLines[index].quantity || 0,
          newLines[index].unitPrice || 0,
        );
      }

      return { ...prev, lines: newLines };
    });
  };

  const handleMaterialSelect = (
    index: number,
    material: MaterialForPurchaseDto | null,
  ) => {
    setFormData((prev) => {
      const newLines = [...prev.lines];
      newLines[index] = Object.assign(new PurchaseOrderLineDto(), {
        ...newLines[index],
        selectedMaterial: material,
        materialId: material?.productCode || `temp-${Date.now()}`,
        materialName: material?.productName || "",
        unitPrice:
          material?.lastPurchasePrice || newLines[index].unitPrice || 0,
      });

      // Recalculate line total with proper rounding
      newLines[index].lineTotal = calculateLineTotal(
        newLines[index].quantity || 0,
        newLines[index].unitPrice || 0,
      );

      return { ...prev, lines: newLines };
    });

    // Clear material error when a material is selected
    if (material && errors[`line_${index}_material`]) {
      setErrors((prev) => clearFieldError(prev, `line_${index}_material`));
    }
  };

  return (
    <div
      className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center p-4 z-50"
      onClick={handleBackdropClick}
    >
      <div className="bg-white rounded-lg shadow-xl max-w-7xl w-full h-[90vh] overflow-hidden flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between p-6 border-b border-gray-200 flex-shrink-0">
          <div className="flex items-center space-x-3">
            <Package className="h-6 w-6 text-indigo-600" />
            <h2 className="text-xl font-semibold text-gray-900">
              {isEditMode
                ? "Upravit nákupní objednávku"
                : "Nová nákupní objednávka"}
            </h2>
          </div>
          <button
            onClick={onClose}
            className="text-gray-400 hover:text-gray-600 transition-colors"
          >
            <X className="h-6 w-6" />
          </button>
        </div>

        {/* Form */}
        <form onSubmit={handleSubmit} className="flex flex-col flex-1 min-h-0">
          <div className="flex-1 flex flex-col p-4 space-y-4 min-h-0">
            {/* Material Resolvers for edit mode - hidden components that load materials for existing lines */}
            {isEditMode &&
              pendingMaterialResolutions.size > 0 &&
              formData.lines.map((line, index) => {
                if (
                  !pendingMaterialResolutions.has(index) ||
                  !line.materialId ||
                  line.materialId.startsWith("temp-")
                ) {
                  return null;
                }
                return (
                  <MaterialResolver
                    key={`resolver-${line.materialId}-${index}`}
                    materialId={line.materialId}
                    lineIndex={index}
                    onMaterialResolved={handleMaterialResolved}
                  />
                );
              })}

            {/* Submit Error - moved to top */}
            {errors.submit && (
              <div className="bg-red-50 border border-red-200 rounded-md p-4">
                <p className="text-sm text-red-600">{errors.submit}</p>
              </div>
            )}

            {/* Basic Information */}
            <PurchaseOrderHeader
              formData={formData}
              errors={errors}
              onInputChange={handleInputChange}
              onSupplierSelect={handleSupplierSelect}
            />

            {/* Order Lines */}
            <div className="flex-1 min-h-0">
              <PurchaseOrderLines
                formData={formData}
                errors={errors}
                onAddLine={addLine}
                onRemoveLine={removeLine}
                onUpdateLine={updateLine}
                onMaterialSelect={handleMaterialSelect}
              />
            </div>
          </div>

          {/* Footer */}
          <div className="flex justify-end gap-3 p-4 border-t border-gray-200 bg-gray-50 flex-shrink-0">
            <button
              type="button"
              onClick={onClose}
              className="bg-gray-600 hover:bg-gray-700 text-white font-medium py-2 px-4 rounded-md transition-colors duration-200"
            >
              Zrušit
            </button>
            <button
              type="submit"
              disabled={isSubmitting}
              className="bg-indigo-600 hover:bg-indigo-700 text-white font-medium py-2 px-4 rounded-md transition-colors duration-200 disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2"
            >
              <Save className="h-4 w-4" />
              {isSubmitting
                ? "Ukládání..."
                : isEditMode
                  ? "Uložit změny"
                  : "Vytvořit objednávku"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};

export default PurchaseOrderForm;
