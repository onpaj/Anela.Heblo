import React, { useState } from 'react';
import { Plus, Edit, Package, Trash2, Calculator, TrendingDown } from 'lucide-react';
import { usePackingMaterials, useDeletePackingMaterial, PackingMaterialDto, ConsumptionType } from '../api/hooks/usePackingMaterials';
import { LoadingIndicator } from '../components/ui/LoadingIndicator';
import AddMaterialModal from '../components/packing-materials/modals/AddMaterialModal';
import EditMaterialModal from '../components/packing-materials/modals/EditMaterialModal';
import UpdateQuantityModal from '../components/packing-materials/modals/UpdateQuantityModal';
import ProcessDailyConsumptionModal from '../components/packing-materials/modals/ProcessDailyConsumptionModal';
import PackingMaterialDetailModal from '../components/packing-materials/modals/PackingMaterialDetailModal';

interface PackingMaterialsPageProps {}

const PackingMaterialsPage: React.FC<PackingMaterialsPageProps> = () => {
  const { data, isLoading, error } = usePackingMaterials();
  const deletePackingMaterialMutation = useDeletePackingMaterial();
  const [selectedMaterial, setSelectedMaterial] = useState<PackingMaterialDto | null>(null);
  const [isAddModalOpen, setIsAddModalOpen] = useState(false);
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [isQuantityModalOpen, setIsQuantityModalOpen] = useState(false);
  const [isProcessConsumptionModalOpen, setIsProcessConsumptionModalOpen] = useState(false);
  const [isDetailModalOpen, setIsDetailModalOpen] = useState(false);

  const formatForecastDays = (days?: number) => {
    if (days === undefined || days === null) return 'N/A';
    if (days > 365) return '∞';
    return `${Math.round(days)} dní`;
  };

  const formatQuantity = (quantity: number) => {
    return quantity.toLocaleString('cs-CZ', {
      minimumFractionDigits: 0,
      maximumFractionDigits: 2
    });
  };

  const getConsumptionTypeColor = (type: ConsumptionType) => {
    switch (type) {
      case ConsumptionType.PerOrder:
        return 'bg-blue-100 text-blue-800';
      case ConsumptionType.PerProduct:
        return 'bg-green-100 text-green-800';
      case ConsumptionType.PerDay:
        return 'bg-purple-100 text-purple-800';
      default:
        return 'bg-gray-100 text-gray-800';
    }
  };

  const handleDeleteMaterial = async (material: PackingMaterialDto) => {
    if (window.confirm(`Opravdu chcete smazat materiál "${material.name}"? Tato akce je nevratná.`)) {
      try {
        await deletePackingMaterialMutation.mutateAsync(material.id);
      } catch (error) {
        console.error('Chyba při mazání materiálu:', error);
        // Error is handled by the mutation and displayed via react-query
      }
    }
  };

  const handleRowClick = (material: PackingMaterialDto) => {
    setSelectedMaterial(material);
    setIsDetailModalOpen(true);
  };

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <LoadingIndicator isVisible={true} />
      </div>
    );
  }

  if (error) {
    return (
      <div className="p-4">
        <div className="bg-red-50 border border-red-200 text-red-700 px-4 py-3 rounded">
          Chyba při načítání materiálů: {(error as Error).message}
        </div>
      </div>
    );
  }

  const materials = data?.materials || [];

  return (
    <div className="p-6">
      {/* Header */}
      <div className="flex items-center justify-between mb-6">
        <div className="flex items-center space-x-3">
          <Package className="h-8 w-8 text-gray-700" />
          <div>
            <h1 className="text-2xl font-bold text-gray-900">Sledování materiálů</h1>
            <p className="text-sm text-gray-500">Správa spotřebních materiálů a jejich zásob</p>
          </div>
        </div>
        
        <div className="flex space-x-3">
          <button
            onClick={() => setIsProcessConsumptionModalOpen(true)}
            className="inline-flex items-center px-4 py-2 bg-orange-600 hover:bg-orange-700 text-white text-sm font-medium rounded-md transition-colors duration-200"
          >
            <TrendingDown className="h-4 w-4 mr-2" />
            Odečíst spotřebu
          </button>
          <button
            onClick={() => setIsAddModalOpen(true)}
            className="inline-flex items-center px-4 py-2 bg-indigo-600 hover:bg-indigo-700 text-white text-sm font-medium rounded-md transition-colors duration-200"
          >
            <Plus className="h-4 w-4 mr-2" />
            Přidat materiál
          </button>
        </div>
      </div>

      {/* Materials Table */}
      <div className="bg-white rounded-lg shadow overflow-hidden">
        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Název
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Aktuální množství
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Spotřeba
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Typ spotřeby
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Forecast (dní)
                </th>
                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Akce
                </th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {materials.length === 0 ? (
                <tr>
                  <td colSpan={6} className="px-6 py-12 text-center text-gray-500">
                    <Package className="h-12 w-12 mx-auto text-gray-300 mb-3" />
                    <p className="text-sm">Zatím nejsou přidány žádné materiály.</p>
                    <button
                      onClick={() => setIsAddModalOpen(true)}
                      className="mt-2 text-indigo-600 hover:text-indigo-500 text-sm font-medium"
                    >
                      Přidat první materiál
                    </button>
                  </td>
                </tr>
              ) : (
                materials.map((material) => (
                  <tr key={material.id} className="hover:bg-gray-50 cursor-pointer" onClick={() => handleRowClick(material)}>
                    <td className="px-6 py-4 whitespace-nowrap">
                      <div className="text-sm font-medium text-gray-900">{material.name}</div>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      <div className="text-sm text-gray-900">{formatQuantity(material.currentQuantity)}</div>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      <div className="text-sm text-gray-900">{formatQuantity(material.consumptionRate)}</div>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      <span className={`inline-flex px-2 py-1 text-xs font-semibold rounded-full ${getConsumptionTypeColor(material.consumptionType)}`}>
                        {material.consumptionTypeText}
                      </span>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      <div className="text-sm text-gray-900">{formatForecastDays(material.forecastedDays)}</div>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
                      <div className="flex items-center justify-end space-x-2">
                        <button
                          onClick={(e) => {
                            e.stopPropagation();
                            setSelectedMaterial(material);
                            setIsQuantityModalOpen(true);
                          }}
                          className="text-indigo-600 hover:text-indigo-900 p-1 rounded"
                          title="Upravit množství"
                        >
                          <Calculator className="h-4 w-4" />
                        </button>
                        <button
                          onClick={(e) => {
                            e.stopPropagation();
                            setSelectedMaterial(material);
                            setIsEditModalOpen(true);
                          }}
                          className="text-gray-600 hover:text-gray-900 p-1 rounded"
                          title="Editovat materiál"
                        >
                          <Edit className="h-4 w-4" />
                        </button>
                        <button
                          onClick={(e) => {
                            e.stopPropagation();
                            handleDeleteMaterial(material);
                          }}
                          className="text-red-600 hover:text-red-900 p-1 rounded"
                          title="Smazat materiál"
                          disabled={deletePackingMaterialMutation.isPending}
                        >
                          <Trash2 className="h-4 w-4" />
                        </button>
                      </div>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>

      {/* Modals */}
      <AddMaterialModal
        isOpen={isAddModalOpen}
        onClose={() => setIsAddModalOpen(false)}
        onSuccess={() => {
          // Refresh happens automatically via react-query invalidation
        }}
      />

      <EditMaterialModal
        isOpen={isEditModalOpen}
        onClose={() => setIsEditModalOpen(false)}
        material={selectedMaterial}
        onSuccess={() => {
          setSelectedMaterial(null);
        }}
      />

      <UpdateQuantityModal
        isOpen={isQuantityModalOpen}
        onClose={() => setIsQuantityModalOpen(false)}
        material={selectedMaterial}
        onSuccess={() => {
          setSelectedMaterial(null);
        }}
      />

      <ProcessDailyConsumptionModal
        isOpen={isProcessConsumptionModalOpen}
        onClose={() => setIsProcessConsumptionModalOpen(false)}
        onSuccess={() => {
          // Refresh data happens automatically via react-query invalidation
        }}
      />

      <PackingMaterialDetailModal
        isOpen={isDetailModalOpen}
        onClose={() => {
          setIsDetailModalOpen(false);
          setSelectedMaterial(null);
        }}
        material={selectedMaterial}
      />
    </div>
  );
};

export default PackingMaterialsPage;