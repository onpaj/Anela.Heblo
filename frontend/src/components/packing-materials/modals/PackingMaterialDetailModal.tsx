import React from 'react';
import { X, Package, TrendingUp } from 'lucide-react';
import { usePackingMaterialLogs, PackingMaterialDto } from '../../../api/hooks/usePackingMaterials';
import { LoadingIndicator } from '../../ui/LoadingIndicator';
import PackingMaterialConsumptionChart from './PackingMaterialConsumptionChart';
import PackingMaterialLogsGrid from './PackingMaterialLogsGrid';

interface PackingMaterialDetailModalProps {
  isOpen: boolean;
  onClose: () => void;
  material: PackingMaterialDto | null;
}

const PackingMaterialDetailModal: React.FC<PackingMaterialDetailModalProps> = ({
  isOpen,
  onClose,
  material,
}) => {
  const { data, isLoading, error } = usePackingMaterialLogs(material?.id || 0, 60);

  if (!isOpen || !material) return null;

  return (
    <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
      <div className="bg-white rounded-lg shadow-xl w-full max-w-4xl mx-4 max-h-[90vh] overflow-hidden flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between p-6 border-b border-gray-200">
          <div className="flex items-center space-x-3">
            <Package className="h-6 w-6 text-indigo-600" />
            <div>
              <h2 className="text-xl font-semibold text-gray-900">{material.name}</h2>
              <p className="text-sm text-gray-500">Detail spotřeby materiálu za posledních 60 dní</p>
            </div>
          </div>
          <button
            onClick={onClose}
            className="text-gray-400 hover:text-gray-600 transition-colors"
          >
            <X className="h-6 w-6" />
          </button>
        </div>

        {/* Content */}
        <div className="flex-1 overflow-y-auto p-6">
          {isLoading ? (
            <div className="flex items-center justify-center h-64">
              <LoadingIndicator isVisible={true} />
            </div>
          ) : error ? (
            <div className="bg-red-50 border border-red-200 text-red-700 px-4 py-3 rounded">
              Chyba při načítání dat: {(error as Error).message}
            </div>
          ) : data ? (
            <div className="space-y-6">
              {/* Material Info */}
              <div className="bg-gray-50 rounded-lg p-4">
                <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
                  <div>
                    <p className="text-sm font-medium text-gray-500">Aktuální množství</p>
                    <p className="text-lg font-semibold text-gray-900">
                      {material.currentQuantity.toLocaleString('cs-CZ', {
                        minimumFractionDigits: 0,
                        maximumFractionDigits: 2
                      })}
                    </p>
                  </div>
                  <div>
                    <p className="text-sm font-medium text-gray-500">Spotřeba</p>
                    <p className="text-lg font-semibold text-gray-900">
                      {material.consumptionRate.toLocaleString('cs-CZ', {
                        minimumFractionDigits: 0,
                        maximumFractionDigits: 2
                      })}
                    </p>
                  </div>
                  <div>
                    <p className="text-sm font-medium text-gray-500">Typ spotřeby</p>
                    <p className="text-lg font-semibold text-gray-900">{material.consumptionTypeText}</p>
                  </div>
                  <div>
                    <p className="text-sm font-medium text-gray-500">Forecast</p>
                    <p className="text-lg font-semibold text-gray-900">
                      {material.forecastedDays === undefined || material.forecastedDays === null
                        ? 'N/A'
                        : material.forecastedDays > 365
                        ? '∞'
                        : `${Math.round(material.forecastedDays)} dní`}
                    </p>
                  </div>
                </div>
              </div>

              {/* Chart Section */}
              <div>
                <div className="flex items-center space-x-2 mb-4">
                  <TrendingUp className="h-5 w-5 text-indigo-600" />
                  <h3 className="text-lg font-medium text-gray-900">Graf spotřeby za posledních 60 dní</h3>
                </div>
                <div className="bg-white border border-gray-200 rounded-lg p-4">
                  <PackingMaterialConsumptionChart data={data.logs} />
                </div>
              </div>

              {/* Logs Grid */}
              <div>
                <h3 className="text-lg font-medium text-gray-900 mb-4">Historie změn</h3>
                <PackingMaterialLogsGrid logs={data.logs} />
              </div>
            </div>
          ) : null}
        </div>

        {/* Footer */}
        <div className="border-t border-gray-200 px-6 py-4">
          <div className="flex justify-end">
            <button
              onClick={onClose}
              className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-indigo-500"
            >
              Zavřít
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};

export default PackingMaterialDetailModal;