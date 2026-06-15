import React, { useState } from "react";
import CatalogDetail from "../CatalogDetail";
import GiftPackageManufacturingList, { GiftPackage } from "./GiftPackageManufacturingList";
import GiftPackageManufacturingDetail from "./GiftPackageManufacturingDetail";
import StockUpOperationStatusIndicator from '../../common/StockUpOperationStatusIndicator';
import { useCreateGiftPackageManufacture, useEnqueueGiftPackageManufacture } from "../../../api/hooks/useGiftPackageManufacturing";
import { useStockUpOperationsSummary } from '../../../api/hooks/useStockUpOperations';
import { CreateGiftPackageManufactureRequest, EnqueueGiftPackageManufactureRequest, StockUpSourceType } from "../../../api/generated/api-client";
import { useScreenView } from "../../../telemetry/useScreenView";
import { usePermissionsContext } from '../../../auth/PermissionsContext';

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

  useScreenView('Logistics', 'GiftPackageManufacturing');

  // Manufacturing API hooks
  const createManufactureMutation = useCreateGiftPackageManufacture();
  const enqueueManufactureMutation = useEnqueueGiftPackageManufacture();

  // Gate StockUpOperations summary on the matching feature permission.
  // Backend constant: AccessRoles.WarehouseStockUpRead = "warehouse.stock_up.read"
  // (see backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs).
  const { hasPermission, isLoading: permsLoading } = usePermissionsContext();
  const canSeeStockUp = !permsLoading && hasPermission('warehouse.stock_up.read');

  const { data: stockUpSummary } = useStockUpOperationsSummary(
    StockUpSourceType.GiftPackageManufacture,
    { enabled: canSeeStockUp },
  );

  const showIndicator = canSeeStockUp && stockUpSummary &&
    ((stockUpSummary.totalInQueue ?? 0) > 0 || (stockUpSummary.failedCount ?? 0) > 0);

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
        allowStockOverride: false // TODO: This could be made configurable via UI
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
      console.log(`Výroba ${quantity}x ${selectedPackage.name} zařazena do fronty. Log ID: ${response.jobId}`);
    } catch (error) {
      console.error('Enqueue manufacturing error:', error);
      throw error;
    }
  };

  return (
    <>
      {showIndicator && (
        <StockUpOperationStatusIndicator
          summary={stockUpSummary}
          sourceType={StockUpSourceType.GiftPackageManufacture}
        />
      )}

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