import React, { useState } from "react";
import CatalogDetail from "../CatalogDetail";
import GiftPackageManufacturingList, { GiftPackage } from "./GiftPackageManufacturingList";
import GiftPackageManufacturingDetail from "./GiftPackageManufacturingDetail";
import { useCreateGiftPackageManufacture, useEnqueueGiftPackageManufacture } from "../../../api/hooks/useGiftPackageManufacturing";
import { CreateGiftPackageManufactureRequest, EnqueueGiftPackageManufactureRequest } from "../../../api/generated/api-client";

const GiftPackageManufacturing: React.FC = () => {
  // State for manufacturing modal 
  const [selectedPackage, setSelectedPackage] = useState<GiftPackage | null>(null);
  const [isManufactureModalOpen, setIsManufactureModalOpen] = useState(false);
  const [salesCoefficient, setSalesCoefficient] = useState<number>(1.3);
  
  // State for date filtering - shared between list and detail
  const [dateFilters, setDateFilters] = useState<{fromDate: Date, toDate: Date}>({
    fromDate: new Date(new Date().getFullYear() - 1, new Date().getMonth(), new Date().getDate()),
    toDate: new Date()
  });
  
  // State for catalog detail modal
  const [selectedProductCode, setSelectedProductCode] = useState<string | null>(null);
  const [isCatalogDetailOpen, setIsCatalogDetailOpen] = useState(false);
  
  // Manufacturing API hooks
  const createManufactureMutation = useCreateGiftPackageManufacture();
  const enqueueManufactureMutation = useEnqueueGiftPackageManufacture();

  // Manufacturing modal handlers
  const handlePackageClick = (pkg: GiftPackage) => {
    setSelectedPackage(pkg);
    setIsManufactureModalOpen(true);
  };
  
  // Coefficient handler
  const handleSalesCoefficientChange = (coefficient: number) => {
    setSalesCoefficient(coefficient);
  };
  
  // Date filter handler
  const handleDateFilterChange = (fromDate: Date, toDate: Date) => {
    setDateFilters({ fromDate, toDate });
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

  const handleEnqueueManufacture = async (quantity: number) => {
    if (!selectedPackage) return;
    
    try {
      const request = new EnqueueGiftPackageManufactureRequest({
        giftPackageCode: selectedPackage.code,
        quantity: quantity,
        allowStockOverride: false
      });
      
      const response = await enqueueManufactureMutation.mutateAsync(request);
      console.log(`Výroba ${quantity}x ${selectedPackage.name} zařazena do fronty. Job ID: ${response.jobId}`);
    } catch (error) {
      console.error('Enqueue manufacturing error:', error);
      throw error;
    }
  };

  return (
    <>
      <GiftPackageManufacturingList
        onPackageClick={handlePackageClick}
        onCatalogDetailClick={handleCatalogDetailClick}
        onSalesCoefficientChange={handleSalesCoefficientChange}
        onDateFilterChange={handleDateFilterChange}
      />
      
      {/* Manufacturing Detail Modal */}
      <GiftPackageManufacturingDetail
        selectedPackage={selectedPackage}
        isOpen={isManufactureModalOpen}
        onClose={handleCloseManufactureModal}
        onManufacture={handleManufacture}
        onEnqueueManufacture={handleEnqueueManufacture}
        salesCoefficient={salesCoefficient}
        fromDate={dateFilters.fromDate}
        toDate={dateFilters.toDate}
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