import React from 'react';
import { Plus } from 'lucide-react';
import { PurchaseOrderLinesProps } from './PurchaseOrderTypes';
import { calculateTotal } from './PurchaseOrderHelpers';
import PurchaseOrderLineItem from './PurchaseOrderLineItem';

const PurchaseOrderLines: React.FC<PurchaseOrderLinesProps> = ({
  formData,
  errors,
  onAddLine,
  onRemoveLine,
  onUpdateLine,
  onMaterialSelect
}) => {
  return (
    <div className="space-y-3 flex flex-col h-full">
      <div className="flex items-center justify-between">
        <h3 className="text-lg font-medium text-gray-900">Položky objednávky</h3>
        <div className="flex items-center space-x-4">
          {/* Total */}
          {formData.lines.some(line => line.selectedMaterial && (line.quantity || 0) > 0 && (line.unitPrice || 0) > 0) && (
            <div className="text-sm text-gray-600">
              Celkem: <span className="font-semibold text-indigo-600 text-base">
                {calculateTotal(formData.lines).toLocaleString('cs-CZ', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} Kč
              </span>
            </div>
          )}
          {/* Add Item Button */}
          <button
            type="button"
            onClick={onAddLine}
            className="flex items-center space-x-1 text-sm bg-indigo-600 hover:bg-indigo-700 text-white px-3 py-1.5 rounded-md transition-colors"
            title="Přidat položku"
          >
            <Plus className="h-4 w-4" />
            <span>Přidat položku</span>
          </button>
        </div>
      </div>
      
      {/* Lines validation error - moved to top */}
      {errors.lines && (
        <div className="bg-red-50 border border-red-200 rounded-md p-3">
          <p className="text-sm text-red-600">{errors.lines}</p>
        </div>
      )}

      {/* Scrollable container for items */}
      <div className="border border-gray-200 rounded-lg flex-1 overflow-y-auto">
        <div className="space-y-0">
          {/* Header row */}
          <div className="sticky top-0 bg-gray-50 grid grid-cols-12 gap-2 px-2 py-2 text-xs font-medium text-gray-600 uppercase tracking-wider border-b border-gray-200">
            <div className="col-span-4">Materiál</div>
            <div className="col-span-2">Množství</div>
            <div className="col-span-2">Jedn. cena</div>
            <div className="col-span-2">Celkem</div>
            <div className="col-span-1">Poznámka</div>
            <div className="col-span-1"></div>
          </div>

          {/* Line items */}
          {formData.lines.map((line, index) => (
            <PurchaseOrderLineItem
              key={line.id === 0 ? `temp-${index}` : line.id}
              line={line}
              index={index}
              errors={errors}
              onUpdateLine={onUpdateLine}
              onMaterialSelect={onMaterialSelect}
              onRemoveLine={onRemoveLine}
            />
          ))}
        </div>
      </div>
    </div>
  );
};

export default PurchaseOrderLines;