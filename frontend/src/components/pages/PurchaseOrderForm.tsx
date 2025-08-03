import React, { useState } from 'react';
import { X, Trash2, Save, Calendar, Truck, FileText, Package } from 'lucide-react';
import { 
  useCreatePurchaseOrderMutation,
  useUpdatePurchaseOrderMutation,
  usePurchaseOrderDetailQuery
} from '../../api/hooks/usePurchaseOrders';
import { 
  PurchaseOrderLineDto, 
  UpdatePurchaseOrderLineRequest, 
  CreatePurchaseOrderLineRequest,
  UpdatePurchaseOrderRequest,
  CreatePurchaseOrderRequest
} from '../../api/generated/api-client';
import { MaterialAutocomplete } from '../common/MaterialAutocomplete';
import { MaterialForPurchaseDto, useMaterialByProductCode } from '../../api/hooks/useMaterials';

interface PurchaseOrderFormProps {
  isOpen: boolean;
  onClose: () => void;
  onSuccess?: (orderId: number) => void;
  editOrderId?: number; // Optional - if provided, form is in edit mode
}

interface FormData {
  orderNumber: string;
  supplierName: string;
  orderDate: string;
  expectedDeliveryDate: string;
  notes: string;
  lines: (PurchaseOrderLineDto & { selectedMaterial?: MaterialForPurchaseDto | null })[];
}

// Generate default order number in format POyyyyMMdd-HHmm
const generateDefaultOrderNumber = (): string => {
  const now = new Date();
  const year = now.getFullYear();
  const month = String(now.getMonth() + 1).padStart(2, '0');
  const day = String(now.getDate()).padStart(2, '0');
  const hour = String(now.getHours()).padStart(2, '0');
  const minute = String(now.getMinutes()).padStart(2, '0');
  
  return `PO${year}${month}${day}-${hour}${minute}`;
};

// Component to resolve and set material for existing purchase order line
const MaterialResolver: React.FC<{ 
  materialId: string;
  lineIndex: number;
  onMaterialResolved: (index: number, material: MaterialForPurchaseDto | null) => void;
}> = ({ materialId, lineIndex, onMaterialResolved }) => {
  const { data: material, isLoading, error } = useMaterialByProductCode(materialId);
  
  React.useEffect(() => {
    if (!isLoading && !error) {
      onMaterialResolved(lineIndex, material || null);
    }
  }, [material, isLoading, error, lineIndex, onMaterialResolved]);
  
  return null; // This is a logic-only component
};

const PurchaseOrderForm: React.FC<PurchaseOrderFormProps> = ({ isOpen, onClose, onSuccess, editOrderId }) => {
  
  const isEditMode = !!editOrderId;
  
  const [formData, setFormData] = useState<FormData>({
    orderNumber: generateDefaultOrderNumber(),
    supplierName: '',
    orderDate: new Date().toISOString().split('T')[0], // Today's date
    expectedDeliveryDate: '',
    notes: '',
    lines: [Object.assign(new PurchaseOrderLineDto(), {
      id: 0, // Temporary ID for new lines
      materialId: `temp-${Date.now()}`,
      materialName: '',
      quantity: 1,
      unitPrice: 0,
      lineTotal: 0,
      selectedMaterial: undefined
    })]
  });

  const [errors, setErrors] = useState<Record<string, string>>({});
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [pendingMaterialResolutions, setPendingMaterialResolutions] = useState<Set<number>>(new Set());

  const createMutation = useCreatePurchaseOrderMutation();
  const updateMutation = useUpdatePurchaseOrderMutation();
  
  // Fetch existing order data when in edit mode
  const { data: existingOrderData } = usePurchaseOrderDetailQuery(editOrderId || 0);

  // Callback to handle when materials are resolved for existing purchase order lines
  const handleMaterialResolved = React.useCallback((lineIndex: number, material: MaterialForPurchaseDto | null) => {
    setFormData(prev => {
      const newLines = [...prev.lines];
      if (lineIndex < newLines.length) {
        newLines[lineIndex] = Object.assign(new PurchaseOrderLineDto(), {
          ...newLines[lineIndex],
          selectedMaterial: material,
          materialName: material?.productName || newLines[lineIndex].materialName
        });
      }
      return { ...prev, lines: newLines };
    });

    // Remove this line from pending resolutions
    setPendingMaterialResolutions(prev => {
      const newSet = new Set(prev);
      newSet.delete(lineIndex);
      return newSet;
    });
  }, []);

  // Load existing data in edit mode
  React.useEffect(() => {
    if (isEditMode && existingOrderData && isOpen) {
      const linesWithMaterials = existingOrderData.lines && existingOrderData.lines.length > 0
        ? existingOrderData.lines.map(line => Object.assign(new PurchaseOrderLineDto(), {
            ...line,
            selectedMaterial: undefined // Will be resolved by MaterialResolver components
          }))
        : [Object.assign(new PurchaseOrderLineDto(), {
            id: 0, // Temporary ID for new lines
            materialId: `temp-${Date.now()}`,
            materialName: '',
            quantity: 1,
            unitPrice: 0,
            lineTotal: 0,
            selectedMaterial: undefined
          })];

      setFormData({
        orderNumber: existingOrderData.orderNumber || generateDefaultOrderNumber(),
        supplierName: existingOrderData.supplierName || '',
        orderDate: existingOrderData.orderDate ? new Date(existingOrderData.orderDate).toISOString().split('T')[0] : new Date().toISOString().split('T')[0],
        expectedDeliveryDate: existingOrderData.expectedDeliveryDate ? new Date(existingOrderData.expectedDeliveryDate).toISOString().split('T')[0] : '',
        notes: existingOrderData.notes || '',
        lines: linesWithMaterials
      });

      // Track which lines need material resolution
      if (existingOrderData.lines && existingOrderData.lines.length > 0) {
        const pendingResolutions = new Set<number>();
        existingOrderData.lines.forEach((line, index) => {
          // Only resolve materials that have a valid materialId (not temp ones)
          if (line.materialId && !line.materialId.startsWith('temp-')) {
            pendingResolutions.add(index);
          }
        });
        setPendingMaterialResolutions(pendingResolutions);
      }
    } else if (!isEditMode && isOpen) {
      // Reset to default state for create mode
      setFormData({
        orderNumber: generateDefaultOrderNumber(),
        supplierName: '',
        orderDate: new Date().toISOString().split('T')[0],
        expectedDeliveryDate: '',
        notes: '',
        lines: [Object.assign(new PurchaseOrderLineDto(), {
          id: 0, // Temporary ID for new lines
          materialId: `temp-${Date.now()}`,
          materialName: '',
          quantity: 1,
          unitPrice: 0,
          lineTotal: 0,
          selectedMaterial: undefined
        })]
      });
      setPendingMaterialResolutions(new Set());
    }
  }, [isEditMode, existingOrderData, isOpen]);

  // Add keyboard event listener for Esc key
  React.useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && isOpen) {
        onClose();
      }
    };

    if (isOpen) {
      document.addEventListener('keydown', handleKeyDown);
    }

    return () => {
      document.removeEventListener('keydown', handleKeyDown);
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

  const validateForm = (): boolean => {
    const newErrors: Record<string, string> = {};

    if (!formData.orderNumber.trim()) {
      newErrors.orderNumber = 'Číslo objednávky je povinné';
    }

    if (!formData.supplierName.trim()) {
      newErrors.supplierName = 'Název dodavatele je povinný';
    }

    if (!formData.orderDate) {
      newErrors.orderDate = 'Datum objednávky je povinné';
    }

    if (formData.expectedDeliveryDate && formData.orderDate && 
        new Date(formData.expectedDeliveryDate) < new Date(formData.orderDate)) {
      newErrors.expectedDeliveryDate = 'Datum dodání nemůže být před datem objednávky';
    }

    // Validate lines (skip empty rows - rows without material selected)
    const nonEmptyLines = formData.lines.filter(line => line.selectedMaterial);
    
    // Check if there's at least one non-empty line
    if (nonEmptyLines.length === 0) {
      newErrors.lines = 'Přidejte alespoň jednu položku objednávky';
    }
    
    formData.lines.forEach((line, index) => {
      // Only validate rows that have material selected
      if (line.selectedMaterial) {
        if (!line.materialName?.trim() && !line.selectedMaterial?.productName?.trim()) {
          newErrors[`line_${index}_material`] = 'Název materiálu je povinný';
        }
        if (!line.quantity || line.quantity <= 0) {
          newErrors[`line_${index}_quantity`] = 'Množství musí být větší než 0';
        }
        if (!line.unitPrice || line.unitPrice <= 0) {
          newErrors[`line_${index}_price`] = 'Jednotková cena musí být větší než 0';
        }
      }
    });

    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    
    if (!validateForm()) {
      return;
    }

    setIsSubmitting(true);

    try {
      if (isEditMode && editOrderId) {
        // Update existing order
        const request = new UpdatePurchaseOrderRequest({
          id: editOrderId,
          supplierName: formData.supplierName,
          expectedDeliveryDate: formData.expectedDeliveryDate ? new Date(formData.expectedDeliveryDate) : undefined,
          notes: formData.notes || undefined,
          orderNumber: formData.orderNumber || undefined,
          lines: formData.lines
            .filter(line => line.selectedMaterial && line.materialId && line.quantity && line.unitPrice)
            .map(line => new UpdatePurchaseOrderLineRequest({
              materialId: line.materialId!,
              name: line.selectedMaterial?.productName || line.materialName,
              quantity: line.quantity!,
              unitPrice: line.unitPrice!,
              notes: line.notes
            }))
        });

        const response = await updateMutation.mutateAsync({ id: editOrderId, request });
        
        if (onSuccess && response.id) {
          onSuccess(response.id);
        }
      } else {
        // Create new order
        const request = new CreatePurchaseOrderRequest({
          supplierName: formData.supplierName,
          orderDate: formData.orderDate,
          expectedDeliveryDate: formData.expectedDeliveryDate || undefined,
          notes: formData.notes || undefined,
          orderNumber: formData.orderNumber || undefined,
          lines: formData.lines
            .filter(line => line.selectedMaterial && line.materialId && line.quantity && line.unitPrice)
            .map(line => new CreatePurchaseOrderLineRequest({
              materialId: line.materialId!,
              name: line.selectedMaterial?.productName || line.materialName,
              quantity: line.quantity!,
              unitPrice: line.unitPrice!,
              notes: line.notes
            }))
        });

        const response = await createMutation.mutateAsync(request);
        
        if (onSuccess && response.id) {
          onSuccess(response.id);
        }
      }
      
      onClose();
    } catch (error) {
      console.error(`Failed to ${isEditMode ? 'update' : 'create'} purchase order:`, error);
      setErrors({ submit: `Nepodařilo se ${isEditMode ? 'upravit' : 'vytvořit'} objednávku. Zkuste to znovu.` });
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleInputChange = (field: keyof FormData, value: string) => {
    setFormData(prev => ({ ...prev, [field]: value }));
    // Clear error when user starts typing
    if (errors[field]) {
      setErrors(prev => ({ ...prev, [field]: '' }));
    }
  };


  const removeLine = (index: number) => {
    setFormData(prev => {
      const newLines = prev.lines.filter((_, i) => i !== index);
      // Always keep at least one empty row
      if (newLines.length === 0) {
        newLines.push(Object.assign(new PurchaseOrderLineDto(), {
          id: 0, // Temporary ID for new lines
          materialId: `temp-${Date.now()}`,
          materialName: '',
          quantity: 1,
          unitPrice: 0,
          lineTotal: 0,
          selectedMaterial: undefined
        }));
      }
      return { ...prev, lines: newLines };
    });
  };

  const updateLine = (index: number, field: keyof PurchaseOrderLineDto, value: string | number) => {
    setFormData(prev => {
      const newLines = [...prev.lines];
      
      // Apply rounding for unit price
      if (field === 'unitPrice' && typeof value === 'number') {
        value = Math.round(value * 10000) / 10000; // Round to 4 decimal places
      }
      
      newLines[index] = Object.assign(new PurchaseOrderLineDto(), { ...newLines[index], [field]: value });
      
      // Recalculate line total with proper rounding
      if (field === 'quantity' || field === 'unitPrice') {
        newLines[index].lineTotal = Math.round((newLines[index].quantity || 0) * (newLines[index].unitPrice || 0) * 100) / 100;
      }
      
      return { ...prev, lines: newLines };
    });
  };

  const handleMaterialSelect = (index: number, material: MaterialForPurchaseDto | null) => {
    setFormData(prev => {
      const newLines = [...prev.lines];
      if (material) {
        // Parse MOQ to number, default to 1 if not available or invalid
        const moq = material.minimalOrderQuantity ? parseInt(material.minimalOrderQuantity) : 1;
        const quantity = isNaN(moq) ? 1 : Math.max(moq, 1);
        
        // Round unit price to 4 decimal places if provided
        const unitPrice = material.lastPurchasePrice ? Math.round(material.lastPurchasePrice * 10000) / 10000 : newLines[index].unitPrice;
        
        newLines[index] = Object.assign(new PurchaseOrderLineDto(), {
          ...newLines[index],
          materialId: material.productCode || `temp-${Date.now()}`, // Use product code as material ID
          materialName: material.productName || '',
          selectedMaterial: material,
          // Pre-fill quantity with MOQ
          quantity: quantity,
          // Pre-fill unit price with last purchase price if available
          unitPrice: unitPrice,
        });
        // Recalculate line total
        newLines[index].lineTotal = Math.round((newLines[index].quantity || 0) * (newLines[index].unitPrice || 0) * 100) / 100;
        
        // Add empty row if this is the last row and it now has material
        if (index === newLines.length - 1) {
          newLines.push(Object.assign(new PurchaseOrderLineDto(), {
            id: 0, // Temporary ID for new lines
            materialId: `temp-${Date.now()}-${Math.random()}`,
            materialName: '',
            quantity: 1,
            unitPrice: 0,
            lineTotal: 0,
            selectedMaterial: undefined
          }));
        }
      } else {
        newLines[index] = Object.assign(new PurchaseOrderLineDto(), {
          ...newLines[index],
          materialId: `temp-${Date.now()}`,
          materialName: '',
          selectedMaterial: undefined,
        });
      }
      
      return { ...prev, lines: newLines };
    });

    // Clear material error when a material is selected
    if (material && errors[`line_${index}_material`]) {
      setErrors(prev => ({ ...prev, [`line_${index}_material`]: '' }));
    }
  };

  const calculateTotal = () => {
    return formData.lines.reduce((sum, line) => sum + (line.lineTotal || 0), 0);
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
              {isEditMode ? 'Upravit nákupní objednávku' : 'Nová nákupní objednávka'}
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
          <div className="flex-1 overflow-y-auto p-4 space-y-4 min-h-0">
            
            {/* Material Resolvers for edit mode - hidden components that load materials for existing lines */}
            {isEditMode && pendingMaterialResolutions.size > 0 && formData.lines.map((line, index) => {
              if (!pendingMaterialResolutions.has(index) || !line.materialId || line.materialId.startsWith('temp-')) {
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
            <div className="space-y-3">
              <h3 className="text-lg font-medium text-gray-900 flex items-center">
                <FileText className="h-5 w-5 mr-2 text-gray-500" />
                Základní informace
              </h3>
              
              <div className="grid grid-cols-1 md:grid-cols-4 gap-3">
                {/* Order Number */}
                <div className="md:col-span-4">
                  <label htmlFor="orderNumber" className="block text-sm font-medium text-gray-700 mb-1">
                    <FileText className="h-4 w-4 inline mr-1" />
                    Číslo objednávky *
                  </label>
                  <input
                    type="text"
                    id="orderNumber"
                    value={formData.orderNumber}
                    onChange={(e) => handleInputChange('orderNumber', e.target.value)}
                    className={`block w-full px-3 py-1.5 text-sm border rounded-md shadow-sm focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 ${
                      errors.orderNumber ? 'border-red-300' : 'border-gray-300'
                    }`}
                    placeholder="Číslo objednávky (např. PO20250101-1015)"
                  />
                  {errors.orderNumber && (
                    <p className="mt-1 text-sm text-red-600">{errors.orderNumber}</p>
                  )}
                </div>

                {/* Supplier Name */}
                <div className="md:col-span-2">
                  <label htmlFor="supplierName" className="block text-sm font-medium text-gray-700 mb-1">
                    <Truck className="h-4 w-4 inline mr-1" />
                    Dodavatel *
                  </label>
                  <input
                    type="text"
                    id="supplierName"
                    value={formData.supplierName}
                    onChange={(e) => handleInputChange('supplierName', e.target.value)}
                    className={`block w-full px-3 py-1.5 text-sm border rounded-md shadow-sm focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 ${
                      errors.supplierName ? 'border-red-300' : 'border-gray-300'
                    }`}
                    placeholder="Název dodavatele"
                  />
                  {errors.supplierName && (
                    <p className="mt-1 text-sm text-red-600">{errors.supplierName}</p>
                  )}
                </div>

                {/* Order Date */}
                <div>
                  <label htmlFor="orderDate" className="block text-sm font-medium text-gray-700 mb-1">
                    <Calendar className="h-4 w-4 inline mr-1" />
                    Objednáno *
                  </label>
                  <input
                    type="date"
                    id="orderDate"
                    value={formData.orderDate}
                    onChange={(e) => handleInputChange('orderDate', e.target.value)}
                    className={`block w-full px-3 py-1.5 text-sm border rounded-md shadow-sm focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 ${
                      errors.orderDate ? 'border-red-300' : 'border-gray-300'
                    }`}
                  />
                  {errors.orderDate && (
                    <p className="mt-1 text-sm text-red-600">{errors.orderDate}</p>
                  )}
                </div>

                {/* Expected Delivery Date */}
                <div>
                  <label htmlFor="expectedDeliveryDate" className="block text-sm font-medium text-gray-700 mb-1">
                    <Calendar className="h-4 w-4 inline mr-1" />
                    Dodání
                  </label>
                  <input
                    type="date"
                    id="expectedDeliveryDate"
                    value={formData.expectedDeliveryDate}
                    onChange={(e) => handleInputChange('expectedDeliveryDate', e.target.value)}
                    className={`block w-full px-3 py-1.5 text-sm border rounded-md shadow-sm focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 ${
                      errors.expectedDeliveryDate ? 'border-red-300' : 'border-gray-300'
                    }`}
                  />
                  {errors.expectedDeliveryDate && (
                    <p className="mt-1 text-sm text-red-600">{errors.expectedDeliveryDate}</p>
                  )}
                </div>
              </div>

              {/* Notes */}
              <div>
                <label htmlFor="notes" className="block text-sm font-medium text-gray-700 mb-1">
                  Poznámky
                </label>
                <input
                  type="text"
                  id="notes"
                  value={formData.notes}
                  onChange={(e) => handleInputChange('notes', e.target.value)}
                  className="block w-full px-3 py-1.5 text-sm border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-indigo-500 focus:border-indigo-500"
                  placeholder="Volitelné poznámky k objednávce"
                />
              </div>
            </div>

            {/* Order Lines */}
            <div className="space-y-3">
              <div className="flex items-center justify-between">
                <h3 className="text-lg font-medium text-gray-900">Položky objednávky</h3>
                <div className="text-right">
                  {formData.lines.some(line => line.selectedMaterial && (line.quantity || 0) > 0 && (line.unitPrice || 0) > 0) && (
                    <div className="text-sm text-gray-600">
                      Celkem: <span className="font-semibold text-indigo-600 text-base">
                        {calculateTotal().toLocaleString('cs-CZ', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} Kč
                      </span>
                    </div>
                  )}
                </div>
              </div>
              
              {/* Lines validation error - moved to top */}
              {errors.lines && (
                <div className="bg-red-50 border border-red-200 rounded-md p-3">
                  <p className="text-sm text-red-600">{errors.lines}</p>
                </div>
              )}

              <div className="space-y-2">
                {/* Header row */}
                <div className="grid grid-cols-12 gap-2 px-2 text-xs font-medium text-gray-600 uppercase tracking-wider">
                  <div className="col-span-4">Materiál</div>
                  <div className="col-span-2">Množství</div>
                  <div className="col-span-2">Jedn. cena</div>
                  <div className="col-span-2">Celkem</div>
                  <div className="col-span-1">Poznámka</div>
                  <div className="col-span-1"></div>
                </div>

                  {/* Line items */}
                  {formData.lines.map((line, index) => (
                    <div key={line.id === 0 ? `temp-${index}` : line.id} className="space-y-1">
                      <div className="grid grid-cols-12 gap-2 p-2 bg-gray-50 rounded-md hover:bg-gray-100 transition-colors">
                        {/* Material Selection */}
                        <div className="col-span-4">
                          <MaterialAutocomplete
                            value={line.selectedMaterial || null}
                            onSelect={(material) => handleMaterialSelect(index, material)}
                            placeholder="Vyberte materiál..."
                            error={errors[`line_${index}_material`]}
                            className="w-full"
                          />
                        </div>

                        {/* Quantity */}
                        <div className="col-span-2">
                          <input
                            type="number"
                            min="0"
                            step="0.01"
                            value={line.quantity}
                            onChange={(e) => updateLine(index, 'quantity', parseFloat(e.target.value) || 0)}
                            className={`block w-full px-3 py-1.5 text-sm border rounded-md shadow-sm focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-indigo-500 ${
                              errors[`line_${index}_quantity`] ? 'border-red-300' : 'border-gray-300'
                            }`}
                            title="Množství"
                          />
                        </div>

                        {/* Unit Price */}
                        <div className="col-span-2">
                          <input
                            type="number"
                            min="0"
                            step="0.0001"
                            value={(line.unitPrice || 0).toFixed(4)}
                            onChange={(e) => updateLine(index, 'unitPrice', parseFloat(e.target.value) || 0)}
                            className={`block w-full px-3 py-1.5 text-sm border rounded-md shadow-sm focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-indigo-500 ${
                              errors[`line_${index}_price`] ? 'border-red-300' : 'border-gray-300'
                            }`}
                            title="Jednotková cena"
                          />
                        </div>

                        {/* Line Total */}
                        <div className="col-span-2 flex items-center justify-end pr-2">
                          <span className="text-sm font-medium text-gray-900">
                            {(line.lineTotal || 0).toLocaleString('cs-CZ', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} Kč
                          </span>
                        </div>

                        {/* Line Notes */}
                        <div className="col-span-1">
                          <input
                            type="text"
                            value={line.notes || ''}
                            onChange={(e) => updateLine(index, 'notes', e.target.value)}
                            className="block w-full px-3 py-1.5 text-sm border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-indigo-500"
                            placeholder="..."
                            title="Poznámky k položce"
                          />
                        </div>

                        {/* Remove button */}
                        <div className="col-span-1 flex items-center justify-center">
                          <button
                            type="button"
                            onClick={() => removeLine(index)}
                            className="text-red-600 hover:text-red-800 transition-colors p-1"
                            title="Odstranit položku"
                          >
                            <Trash2 className="h-4 w-4" />
                          </button>
                        </div>
                      </div>
                      
                      {/* Error messages row */}
                      {(errors[`line_${index}_quantity`] || errors[`line_${index}_price`]) && (
                        <div className="grid grid-cols-12 gap-2 px-2">
                          <div className="col-span-4"></div>
                          <div className="col-span-2">
                            {errors[`line_${index}_quantity`] && (
                              <p className="text-xs text-red-600">{errors[`line_${index}_quantity`]}</p>
                            )}
                          </div>
                          <div className="col-span-2">
                            {errors[`line_${index}_price`] && (
                              <p className="text-xs text-red-600">{errors[`line_${index}_price`]}</p>
                            )}
                          </div>
                          <div className="col-span-4"></div>
                        </div>
                      )}
                    </div>
                  ))}

              </div>
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
              {isSubmitting ? 'Ukládání...' : (isEditMode ? 'Uložit změny' : 'Vytvořit objednávku')}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};

export default PurchaseOrderForm;