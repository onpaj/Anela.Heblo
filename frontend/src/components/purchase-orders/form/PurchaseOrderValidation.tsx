import { FormData } from './PurchaseOrderTypes';

export const validateForm = (formData: FormData): Record<string, string> => {
  const newErrors: Record<string, string> = {};

  if (!formData.orderNumber.trim()) {
    newErrors.orderNumber = 'Číslo objednávky je povinné';
  }

  if (!formData.selectedSupplier) {
    newErrors.selectedSupplier = 'Dodavatel je povinný';
  }

  if (!formData.orderDate) {
    newErrors.orderDate = 'Datum objednávky je povinné';
  }

  if (formData.expectedDeliveryDate && formData.orderDate && 
      new Date(formData.expectedDeliveryDate) < new Date(formData.orderDate)) {
    newErrors.expectedDeliveryDate = 'Datum dodání nemůže být před datem objednávky';
  }

  // Validate lines (skip empty rows - rows without material selected)
  formData.lines.forEach((line, index) => {
    // Only validate rows that have material selected
    const hasValidMaterial = line.selectedMaterial && line.selectedMaterial.productName;
    
    if (hasValidMaterial) {
      if (!line.selectedMaterial?.productName?.trim()) {
        newErrors[`line_${index}_material`] = 'Vyberte materiál ze seznamu';
      }
      if (!line.quantity || line.quantity <= 0) {
        newErrors[`line_${index}_quantity`] = 'Množství musí být větší než 0';
      }
      if (!line.unitPrice || line.unitPrice <= 0) {
        newErrors[`line_${index}_price`] = 'Jednotková cena musí být větší než 0';
      }
    }
  });

  return newErrors;
};

export const clearFieldError = (
  errors: Record<string, string>, 
  field: string
): Record<string, string> => {
  if (errors[field]) {
    const newErrors = { ...errors };
    delete newErrors[field];
    return newErrors;
  }
  return errors;
};