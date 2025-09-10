import React, { useState } from "react";
import CatalogDetail from "../CatalogDetail";
import GiftPackageManufacturingList, { GiftPackage } from "./GiftPackageManufacturingList";
import GiftPackageManufacturingDetail from "./GiftPackageManufacturingDetail";
import { useCreateGiftPackageManufacture } from "../../../api/hooks/useGiftPackageManufacturing";
import { CreateGiftPackageManufactureRequest } from "../../../api/generated/api-client";

const GiftPackageManufacturing: React.FC = () => {
  // State for manufacturing modal 
  const [selectedPackage, setSelectedPackage] = useState<GiftPackage | null>(null);
  const [isManufactureModalOpen, setIsManufactureModalOpen] = useState(false);
  
  // State for catalog detail modal
  const [selectedProductCode, setSelectedProductCode] = useState<string | null>(null);
  const [isCatalogDetailOpen, setIsCatalogDetailOpen] = useState(false);
  
  // Manufacturing API hook
  const createManufactureMutation = useCreateGiftPackageManufacture();

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
      const request = new CreateGiftPackageManufactureRequest({
        giftPackageCode: selectedPackage.code,
        quantity: quantity,
        allowStockOverride: false, // TODO: This could be made configurable via UI
        userId: "00000000-0000-0000-0000-000000000000" // This will be overridden by the backend from current user context
      });
      
      await createManufactureMutation.mutateAsync(request);
      console.log(`Úspěšně vyrobeno ${quantity}x ${selectedPackage.name}`);
    } catch (error) {
      console.error('Manufacturing error:', error);
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