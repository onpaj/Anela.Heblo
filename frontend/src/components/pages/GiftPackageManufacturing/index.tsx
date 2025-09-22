import React, { useState } from "react";
import CatalogDetail from "../CatalogDetail";
import GiftPackageManufacturingList, { GiftPackage } from "./GiftPackageManufacturingList";
import GiftPackageManufacturingDetail from "./GiftPackageManufacturingDetail";
import { useEnqueueGiftPackageManufacture } from "../../../api/hooks/useGiftPackageManufacturing";
import { EnqueueGiftPackageManufactureRequest } from "../../../api/generated/api-client";

const GiftPackageManufacturing: React.FC = () => {
  // State for manufacturing modal 
  const [selectedPackage, setSelectedPackage] = useState<GiftPackage | null>(null);
  const [isManufactureModalOpen, setIsManufactureModalOpen] = useState(false);
  
  // State for catalog detail modal
  const [selectedProductCode, setSelectedProductCode] = useState<string | null>(null);
  const [isCatalogDetailOpen, setIsCatalogDetailOpen] = useState(false);
  
  // Manufacturing API hook
  const enqueueManufactureMutation = useEnqueueGiftPackageManufacture();

  // Manufacturing modal handlers
  const handlePackageClick = (pkg: GiftPackage) => {
    setSelectedPackage(pkg);
    setIsManufactureModalOpen(true);
  };
  
  // Catalog detail handlers
  const handleCatalogDetailClick = (productCode: string) => {
    setSelectedProductCode(productCode);
    setIsCatalogDetailOpen(true);
  };
  
  const handleCloseCatalogDetail = () => {
    setIsCatalogDetailOpen(false);
    setSelectedProductCode(null);
  };
  
  const handleCloseManufactureModal = () => {
    setIsManufactureModalOpen(false);
    setSelectedPackage(null);
  };
  
  const handleManufacture = async (quantity: number) => {
    if (!selectedPackage) return;
    
    try {
      const request = new EnqueueGiftPackageManufactureRequest({
        giftPackageCode: selectedPackage.code,
        quantity: quantity,
        allowStockOverride: false, // TODO: This could be made configurable via UI
        requestedByUserName: "" // Will be determined by backend from current user context
      });
      
      const response = await enqueueManufactureMutation.mutateAsync(request);
      console.log(`Výroba zařazena do fronty: ${response.message || `${quantity}x ${selectedPackage.name}`}`);
      console.log(`Job ID: ${response.jobId}`);
    } catch (error) {
      console.error('Manufacturing enqueue error:', error);
      throw error;
    }
  };

  return (
    <>
      <GiftPackageManufacturingList
        onPackageClick={handlePackageClick}
        onCatalogDetailClick={handleCatalogDetailClick}
      />
      
      {/* Manufacturing Detail Modal */}
      <GiftPackageManufacturingDetail
        selectedPackage={selectedPackage}
        isOpen={isManufactureModalOpen}
        onClose={handleCloseManufactureModal}
        onManufacture={handleManufacture}
      />
      
      {/* Catalog Detail Modal */}
      <CatalogDetail
        productCode={selectedProductCode}
        isOpen={isCatalogDetailOpen}
        onClose={handleCloseCatalogDetail}
        defaultTab="basic"
      />
    </>
  );
};

export default GiftPackageManufacturing;