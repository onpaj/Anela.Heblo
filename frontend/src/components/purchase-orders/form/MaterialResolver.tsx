import React from 'react';
import { useMaterialByProductCode } from '../../../api/hooks/useMaterials';
import { MaterialResolverProps } from './PurchaseOrderTypes';

// Component to resolve and set material for existing purchase order line
const MaterialResolver: React.FC<MaterialResolverProps> = ({ 
  materialId, 
  lineIndex, 
  onMaterialResolved 
}) => {
  const { data: material, isLoading, error } = useMaterialByProductCode(materialId);
  
  React.useEffect(() => {
    if (!isLoading && !error) {
      onMaterialResolved(lineIndex, material || null);
    }
  }, [material, isLoading, error, lineIndex, onMaterialResolved]);
  
  return null; // This is a logic-only component
};

export default MaterialResolver;