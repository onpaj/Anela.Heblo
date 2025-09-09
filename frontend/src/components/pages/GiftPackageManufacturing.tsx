import React, { useState, useEffect, useRef } from "react";
import { 
  Package, 
  Clock, 
  Users, 
  Plus,
  RefreshCw,
  AlertCircle,
  X,
  TrendingUp,
  Info,
  Search
} from "lucide-react";
import {
  useAvailableGiftPackages,
  useManufactureLog,
  getGiftPackageClient,
} from "../../api/hooks/useGiftPackageManufacturing";
import {
  GiftPackageDto,
  GetGiftPackageDetailResponse,
} from "../../api/generated/api-client";
import { formatDistanceToNow } from "date-fns";
import CatalogDetail from "./CatalogDetail";

const GiftPackageManufacturing: React.FC = () => {
  const [activeTab, setActiveTab] = useState<'production' | 'log'>('production');
  const [isModalOpen, setIsModalOpen] = useState<boolean>(false);
  const [selectedPackage, setSelectedPackage] = useState<GiftPackageDto | null>(null);
  const [quantity, setQuantity] = useState<number>(1);
  const [searchFilter, setSearchFilter] = useState<string>('');
  const [selectedProductCode, setSelectedProductCode] = useState<string | null>(null);
  const [isDetailModalOpen, setIsDetailModalOpen] = useState<boolean>(false);
  const [selectedPackageCode, setSelectedPackageCode] = useState<string>('');
  const [isLoadingDetail, setIsLoadingDetail] = useState<boolean>(false);
  const [loadedGiftPackageDetail, setLoadedGiftPackageDetail] = useState<GetGiftPackageDetailResponse | null>(null);
  
  // Refs
  const quantityInputRef = useRef<HTMLInputElement>(null);
  
  // API hooks
  const { data: availablePackages, isLoading: packagesLoading, error: packagesError } = useAvailableGiftPackages();
  const { data: manufactureLog, refetch: refetchLog } = useManufactureLog(20);

  // Filter packages based on search input
  const filteredPackages = availablePackages?.giftPackages?.filter(pkg => 
    (pkg.name?.toLowerCase().includes(searchFilter.toLowerCase()) || false) ||
    (pkg.code?.toLowerCase().includes(searchFilter.toLowerCase()) || false)
  ) || [];

  // Calculate suggested quantity and maximum manufacturable quantity
  const calculateQuantities = (pkg: GiftPackageDto) => {
    // Use detailed data if available, otherwise fallback to basic data
    const ingredients = loadedGiftPackageDetail?.giftPackage?.ingredients || pkg.ingredients;
    
    if (!ingredients || ingredients.length === 0) return { maxQuantity: 0, suggestedQuantity: 0 };
    
    const maxQuantity = Math.min(
      ...ingredients.map(ingredient => 
        Math.floor((ingredient.availableStock || 0) / (ingredient.requiredQuantity || 1))
      )
    );
    
    // Calculate suggested quantity based on real sales data
    const salesPerDay = pkg.dailySales ?? 1; // Use real sales data from API
    const suggestedQuantity = Math.max(1, Math.min(maxQuantity, salesPerDay * 3)); // Suggest 3 days worth
    
    return { maxQuantity, suggestedQuantity };
  };

  // Open modal for package manufacturing
  const openManufacturingModal = async (pkg: GiftPackageDto) => {
    if (!pkg.code) return;
    
    setIsLoadingDetail(true);
    setSelectedPackageCode(pkg.code);
    
    try {
      // Call GetGiftPackageDetailAsync directly
      const client = getGiftPackageClient();
      const detail = await client.logistics_GetGiftPackageDetail(pkg.code);
      
      setLoadedGiftPackageDetail(detail);
      setSelectedPackage(pkg);
      const { suggestedQuantity } = calculateQuantities(pkg);
      setQuantity(suggestedQuantity);
      setIsModalOpen(true);
      setIsLoadingDetail(false);
    } catch (error) {
      console.error("Failed to load gift package detail:", error);
      setIsLoadingDetail(false);
      setLoadedGiftPackageDetail(null);
      // Still open modal with basic data
      setSelectedPackage(pkg);
      const { suggestedQuantity } = calculateQuantities(pkg);
      setQuantity(suggestedQuantity);
      setIsModalOpen(true);
    }
  };

  // Close modal
  const closeModal = () => {
    setIsModalOpen(false);
    setSelectedPackage(null);
    setQuantity(1);
    setSelectedPackageCode('');
    setIsLoadingDetail(false);
    setLoadedGiftPackageDetail(null);
  };

  // Handle row click to open catalog detail
  const handleRowClick = (productCode: string) => {
    setSelectedProductCode(productCode);
    setIsDetailModalOpen(true);
  };

  // Close catalog detail modal
  const closeCatalogDetail = () => {
    setIsDetailModalOpen(false);
    setSelectedProductCode(null);
  };

  // Focus and select the quantity input when modal opens
  useEffect(() => {
    if (isModalOpen && quantityInputRef.current) {
      // Use setTimeout to ensure the modal is fully rendered
      setTimeout(() => {
        quantityInputRef.current?.focus();
        quantityInputRef.current?.select();
      }, 100);
    }
  }, [isModalOpen]);

  // Handle manufacturing (placeholder for now)
  const handleManufacture = async () => {
    if (!selectedPackage) return;

    // TODO: Implement actual manufacturing logic
    alert(`Výroba ${quantity}x ${selectedPackage.name} bude implementována později.`);
    closeModal();
    refetchLog();
  };

  if (packagesLoading) {
    return (
      <div className="mx-auto px-4 py-6">
        <div className="flex items-center justify-center min-h-64">
          <RefreshCw className="h-8 w-8 animate-spin text-indigo-600" />
          <span className="ml-2 text-lg text-gray-600">Načítání...</span>
        </div>
      </div>
    );
  }

  if (packagesError) {
    return (
      <div className="mx-auto px-4 py-6">
        <div className="bg-red-50 border border-red-200 rounded-lg p-6">
          <div className="flex items-center">
            <AlertCircle className="h-6 w-6 text-red-600 mr-2" />
            <h3 className="text-lg font-semibold text-red-900">Chyba při načítání dat</h3>
          </div>
          <p className="mt-2 text-red-700">
            Nepodařilo se načíst dostupné dárkové balíčky. Zkuste stránku obnovit.
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="mx-auto px-4 py-6">
      {/* Header */}
      <div className="mb-8">
        <div className="flex items-center mb-4">
          <Package className="h-8 w-8 text-indigo-600 mr-3" />
          <h1 className="text-3xl font-bold text-gray-900">
            Výroba dárkových balíčků
          </h1>
        </div>
        <p className="text-gray-600">
          Spravujte výrobu dárkových balíčků a sledujte historii výroby.
        </p>
      </div>

      {/* Tabs */}
      <div className="mb-6">
        <div className="border-b border-gray-200">
          <nav className="-mb-px flex space-x-8">
            <button
              onClick={() => setActiveTab('production')}
              className={`py-2 px-1 border-b-2 font-medium text-sm ${
                activeTab === 'production'
                  ? 'border-indigo-500 text-indigo-600'
                  : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
              }`}
            >
              <div className="flex items-center">
                <Package className="h-4 w-4 mr-2" />
                Výroba
              </div>
            </button>
            <button
              onClick={() => setActiveTab('log')}
              className={`py-2 px-1 border-b-2 font-medium text-sm ${
                activeTab === 'log'
                  ? 'border-indigo-500 text-indigo-600'
                  : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
              }`}
            >
              <div className="flex items-center">
                <Clock className="h-4 w-4 mr-2" />
                Log
              </div>
            </button>
          </nav>
        </div>
      </div>

      {/* Production Tab Content */}
      {activeTab === 'production' && (
        <div className="bg-white shadow-sm rounded-lg overflow-hidden">
          <div className="px-6 py-4 border-b border-gray-200">
            <div className="flex justify-between items-center">
              <h3 className="text-lg font-semibold text-gray-900">Dostupné dárkové balíčky</h3>
              {/* Search Filter */}
              <div className="relative max-w-md">
                <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                  <Search className="h-4 w-4 text-gray-400" />
                </div>
                <input
                  type="text"
                  placeholder="Hledat podle názvu nebo kódu..."
                  value={searchFilter}
                  onChange={(e) => setSearchFilter(e.target.value)}
                  className="block w-full pl-9 pr-3 py-2 border border-gray-300 rounded-md leading-5 bg-white placeholder-gray-500 focus:outline-none focus:placeholder-gray-400 focus:ring-1 focus:ring-indigo-500 focus:border-indigo-500 text-sm"
                />
              </div>
            </div>
          </div>
          
          {filteredPackages.length > 0 ? (
            <>
              {/* Results count */}
              {searchFilter && (
                <div className="px-6 py-2 bg-gray-50 border-b border-gray-200">
                  <p className="text-sm text-gray-600">
                    Nalezeno {filteredPackages.length} z {availablePackages?.giftPackages?.length || 0} balíčků
                  </p>
                </div>
              )}
              
              <div className="overflow-x-auto">
                <table className="min-w-full divide-y divide-gray-200">
                  <thead className="bg-gray-50">
                    <tr>
                      <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                        Název / Kód
                      </th>
                      <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                        Aktuálně skladem
                      </th>
                      <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                        Prodeje / den
                      </th>
                      <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                        Navrhované množství
                      </th>
                      <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                        Akce
                      </th>
                    </tr>
                  </thead>
                  <tbody className="bg-white divide-y divide-gray-200">
                    {filteredPackages.map((pkg) => {
                    const { maxQuantity, suggestedQuantity } = calculateQuantities(pkg);
                    const currentStock = pkg.availableStock ?? 0; // Real stock data from API
                    const salesPerDay = pkg.dailySales ?? 0; // Real sales data from API
                    
                    return (
                      <tr key={pkg.code} className="hover:bg-gray-50 cursor-pointer" onClick={() => handleRowClick(pkg.code || "")}>
                        <td className="px-6 py-4 whitespace-nowrap">
                          <div>
                            <div className="text-sm font-medium text-gray-900">{pkg.name}</div>
                            <div className="text-sm text-gray-500">{pkg.code}</div>
                          </div>
                        </td>
                        <td className="px-6 py-4 whitespace-nowrap">
                          <span className={`text-sm font-medium ${
                            currentStock < 10 ? 'text-red-600' : 
                            currentStock < 20 ? 'text-orange-600' : 'text-green-600'
                          }`}>
                            {currentStock} ks
                          </span>
                        </td>
                        <td className="px-6 py-4 whitespace-nowrap">
                          <div className="flex items-center text-sm text-gray-900">
                            <TrendingUp className="h-4 w-4 mr-1 text-gray-400" />
                            {salesPerDay} ks/den
                          </div>
                        </td>
                        <td className="px-6 py-4 whitespace-nowrap">
                          <span className="text-sm font-medium text-indigo-600">
                            {suggestedQuantity} ks
                          </span>
                          <div className="text-xs text-gray-500">
                            (max: {maxQuantity} ks)
                          </div>
                        </td>
                        <td className="px-6 py-4 whitespace-nowrap">
                          <button
                            onClick={(e) => {
                              e.stopPropagation();
                              openManufacturingModal(pkg);
                            }}
                            disabled={isLoadingDetail && selectedPackageCode === pkg.code}
                            className="inline-flex items-center px-3 py-1.5 border border-transparent text-xs font-medium rounded-md text-white bg-indigo-600 hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:bg-indigo-400 disabled:cursor-not-allowed"
                          >
                            {isLoadingDetail && selectedPackageCode === pkg.code ? (
                              <RefreshCw className="h-3 w-3 mr-1 animate-spin" />
                            ) : (
                              <Plus className="h-3 w-3 mr-1" />
                            )}
                            Vyrobit
                          </button>
                        </td>
                      </tr>
                    );
                    })}
                  </tbody>
                </table>
              </div>
            </>
          ) : searchFilter ? (
            <div className="text-center py-12">
              <Search className="h-12 w-12 text-gray-400 mx-auto mb-4" />
              <p className="text-gray-600">Žádné balíčky neodpovídají hledanému výrazu "{searchFilter}".</p>
              <button
                onClick={() => setSearchFilter('')}
                className="mt-2 text-indigo-600 hover:text-indigo-700 text-sm"
              >
                Vymazat filtr
              </button>
            </div>
          ) : availablePackages?.giftPackages && availablePackages.giftPackages.length > 0 ? (
            <div className="text-center py-12">
              <Package className="h-12 w-12 text-gray-400 mx-auto mb-4" />
              <p className="text-gray-600">Žádné balíčky neodpovídají hledanému výrazu.</p>
            </div>
          ) : (
            <div className="text-center py-12">
              <Package className="h-12 w-12 text-gray-400 mx-auto mb-4" />
              <p className="text-gray-600">Žádné dárkové balíčky nejsou k dispozici.</p>
            </div>
          )}
        </div>
      )}

      {/* Log Tab Content */}
      {activeTab === 'log' && (
        <div className="bg-white shadow-sm rounded-lg p-6">
          <div className="flex items-center justify-between mb-4">
            <h3 className="text-lg font-semibold text-gray-900">Historie výroby</h3>
            <button
              onClick={() => refetchLog()}
              className="p-2 text-gray-500 hover:text-gray-700"
              title="Obnovit historii"
            >
              <RefreshCw className="h-4 w-4" />
            </button>
          </div>
          
          {manufactureLog?.manufactureLogs && manufactureLog.manufactureLogs.length > 0 ? (
            <div className="space-y-3">
              {manufactureLog.manufactureLogs.map((log) => (
                <div key={log.id} className="border border-gray-200 rounded-lg p-4">
                  <div className="flex items-start justify-between">
                    <div className="flex-1">
                      <div className="flex items-center mb-2">
                        <Package className="h-4 w-4 text-gray-600 mr-2" />
                        <span className="font-medium text-gray-900">
                          {log.giftPackageCode}
                        </span>
                        <span className="ml-2 px-2 py-1 bg-gray-100 text-gray-800 text-xs rounded">
                          {log.quantityCreated}x
                        </span>
                        {log.stockOverrideApplied && (
                          <span className="ml-2 px-2 py-1 bg-orange-100 text-orange-800 text-xs rounded">
                            Override
                          </span>
                        )}
                      </div>
                      
                      <div className="flex items-center text-sm text-gray-600 mb-2">
                        <Clock className="h-3 w-3 mr-1" />
                        <span>{formatDistanceToNow(new Date(log.createdAt || ""), { addSuffix: true })}</span>
                        <Users className="h-3 w-3 ml-4 mr-1" />
                        <span>Uživatel: {log.createdBy}</span>
                      </div>
                      
                      {log.consumedItems && log.consumedItems.length > 0 && (
                        <div className="text-sm">
                          <p className="text-gray-600 mb-1">Spotřebované položky:</p>
                          <div className="space-y-1">
                            {log.consumedItems.map((item, idx) => (
                              <div key={idx} className="text-gray-800">
                                {item.productCode}: {item.quantityConsumed}x
                              </div>
                            ))}
                          </div>
                        </div>
                      )}
                    </div>
                  </div>
                </div>
              ))}
            </div>
          ) : (
            <div className="text-center py-8">
              <Clock className="h-12 w-12 text-gray-400 mx-auto mb-4" />
              <p className="text-gray-600">Zatím žádná historie výroby.</p>
            </div>
          )}
        </div>
      )}

      {/* Manufacturing Modal */}
      {isModalOpen && selectedPackage && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg max-w-7xl w-full mx-4 max-h-[90vh] overflow-y-auto">
            <div className="px-6 py-4 border-b border-gray-200">
              <div className="flex items-center justify-between">
                <h3 className="text-lg font-semibold text-gray-900">
                  Výroba: {selectedPackage.name}
                </h3>
                <button
                  onClick={closeModal}
                  className="text-gray-400 hover:text-gray-600"
                >
                  <X className="h-6 w-6" />
                </button>
              </div>
            </div>
            
            <div className="p-8 space-y-8">
              {/* Package info */}
              <div className="bg-gray-50 rounded-lg p-6">
                <div className="flex items-center mb-4">
                  <Info className="h-5 w-5 text-indigo-600 mr-2" />
                  <span className="text-lg font-medium text-gray-900">Složení balíčku</span>
                </div>
                
                {isLoadingDetail ? (
                  <div className="flex items-center justify-center py-4">
                    <RefreshCw className="h-5 w-5 animate-spin text-indigo-600 mr-2" />
                    <span className="text-gray-600">Načítám detail balíčku...</span>
                  </div>
                ) : loadedGiftPackageDetail?.giftPackage?.ingredients && loadedGiftPackageDetail.giftPackage.ingredients.length > 0 ? (
                  <div className="space-y-2">
                    {loadedGiftPackageDetail.giftPackage.ingredients.map((ingredient, idx) => {
                      const totalNeeded = (ingredient.requiredQuantity || 0) * quantity;
                      const isAvailable = (ingredient.availableStock || 0) >= totalNeeded;
                      
                      return (
                        <div key={idx} className="flex justify-between items-center py-2 border-b border-gray-200 last:border-b-0">
                          <div>
                            <span className="font-medium text-gray-900">{ingredient.productName}</span>
                            <span className="text-sm text-gray-500 ml-2">({ingredient.productCode})</span>
                          </div>
                          <div className="text-right">
                            <div className={`text-sm font-medium ${isAvailable ? 'text-green-600' : 'text-red-600'}`}>
                              {totalNeeded} ks potřeba
                            </div>
                            <div className="text-xs text-gray-500">
                              {ingredient.availableStock} ks skladem
                            </div>
                          </div>
                        </div>
                      );
                    })}
                  </div>
                ) : selectedPackage?.ingredients && selectedPackage.ingredients.length > 0 ? (
                  <div className="space-y-2">
                    {selectedPackage.ingredients.map((ingredient, idx) => {
                      const totalNeeded = (ingredient.requiredQuantity || 0) * quantity;
                      const isAvailable = (ingredient.availableStock || 0) >= totalNeeded;
                      
                      return (
                        <div key={idx} className="flex justify-between items-center py-2 border-b border-gray-200 last:border-b-0">
                          <div>
                            <span className="font-medium text-gray-900">{ingredient.productName}</span>
                            <span className="text-sm text-gray-500 ml-2">({ingredient.productCode})</span>
                          </div>
                          <div className="text-right">
                            <div className={`text-sm font-medium ${isAvailable ? 'text-green-600' : 'text-red-600'}`}>
                              {totalNeeded} ks potřeba
                            </div>
                            <div className="text-xs text-gray-500">
                              {ingredient.availableStock} ks skladem
                            </div>
                          </div>
                        </div>
                      );
                    })}
                  </div>
                ) : (
                  <div className="text-center py-4 text-gray-500">
                    Žádné ingredience nejsou k dispozici.
                  </div>
                )}
              </div>

              {/* Quantity input */}
              <div className="bg-indigo-50 rounded-lg p-6">
                <label className="block text-lg font-medium text-gray-900 mb-3">
                  Množství k výrobě
                </label>
                <div className="flex items-end gap-4">
                  <div className="flex-1 max-w-xs">
                    <input
                      ref={quantityInputRef}
                      type="number"
                      min="1"
                      max={calculateQuantities(selectedPackage).maxQuantity}
                      value={quantity}
                      onChange={(e) => setQuantity(parseInt(e.target.value) || 1)}
                      className="w-full px-4 py-3 text-lg border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                    />
                  </div>
                  <div className="text-gray-600">
                    <span className="text-sm">Maximum: </span>
                    <span className="font-medium text-lg">{calculateQuantities(selectedPackage).maxQuantity} ks</span>
                  </div>
                </div>
              </div>

            </div>

            {/* Modal footer */}
            <div className="px-8 py-6 border-t border-gray-200 flex justify-end space-x-4">
              <button
                onClick={closeModal}
                className="px-6 py-3 border border-gray-300 rounded-md text-sm font-medium text-gray-700 bg-white hover:bg-gray-50"
              >
                Zrušit
              </button>
              <button
                onClick={handleManufacture}
                className="px-6 py-3 border border-transparent rounded-md text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700"
              >
                Vyrobit
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Catalog Detail Modal */}
      <CatalogDetail 
        productCode={selectedProductCode}
        isOpen={isDetailModalOpen}
        onClose={closeCatalogDetail}
      />
    </div>
  );
};

export default GiftPackageManufacturing;