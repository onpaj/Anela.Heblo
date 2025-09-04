import React from 'react';
import { CatalogAutocomplete } from './CatalogAutocomplete';
import { catalogItemToMaterial, materialDisplayValue, PRODUCT_TYPE_FILTERS } from './CatalogAutocompleteAdapters';
import { MaterialForPurchaseDto } from '../../api/hooks/useMaterials';

interface MaterialAutocompleteProps {
  value?: MaterialForPurchaseDto | null;
  onSelect: (item: MaterialForPurchaseDto | null) => void;
  placeholder?: string;
  disabled?: boolean;
  error?: string;
  className?: string;
}

export const MaterialAutocomplete: React.FC<MaterialAutocompleteProps> = ({
  value,
  onSelect,
  placeholder = "Vyberte položku z katalogu...",
  disabled = false,
  error,
  className = ""
}) => {
  return (
    <CatalogAutocomplete<MaterialForPurchaseDto>
      value={value}
      onSelect={onSelect}
      placeholder={placeholder}
      productTypes={PRODUCT_TYPE_FILTERS.PURCHASE_MATERIALS}
      itemAdapter={catalogItemToMaterial}
      displayValue={materialDisplayValue}
      showSelectedInfo
      clearable
      size="md"
      disabled={disabled}
      error={error}
      className={className}
    />
  );
};

export default MaterialAutocomplete;