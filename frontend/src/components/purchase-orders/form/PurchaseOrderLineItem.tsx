import React from "react";
import { Trash2 } from "lucide-react";
import MaterialAutocomplete from "../../common/MaterialAutocomplete";
import { PurchaseOrderLineItemProps } from "./PurchaseOrderTypes";

const PurchaseOrderLineItem: React.FC<PurchaseOrderLineItemProps> = ({
  line,
  index,
  errors,
  onUpdateLine,
  onMaterialSelect,
  onRemoveLine,
}) => {
  return (
    <div className="space-y-0 border-b border-gray-100 last:border-b-0 dark:border-graphite-border">
      <div className="grid grid-cols-12 gap-2 p-2 hover:bg-gray-50 transition-colors dark:hover:bg-white/5">
        {/* Material Selection */}
        <div className="col-span-4">
          <MaterialAutocomplete
            value={line.selectedMaterial}
            onSelect={(material) => onMaterialSelect(index, material)}
            placeholder="Vyberte materiál nebo zboží..."
            error={errors[`line_${index}_material`]}
            className="w-full"
          />
        </div>

        {/* Quantity */}
        <div className="col-span-2">
          <input
            type="number"
            min="0"
            step="0.01"
            value={line.quantity}
            onChange={(e) =>
              onUpdateLine(index, "quantity", parseFloat(e.target.value) || 0)
            }
            className={`block w-full px-3 py-1.5 text-sm border rounded-md shadow-sm focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-indigo-500 dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint ${
              errors[`line_${index}_quantity`]
                ? "border-red-300 dark:border-red-900/40"
                : "border-gray-300 dark:border-graphite-border"
            }`}
            title="Množství"
          />
        </div>

        {/* Unit Price */}
        <div className="col-span-2">
          <input
            type="number"
            min="0"
            step="0.0001"
            value={line.unitPrice || ""}
            onChange={(e) =>
              onUpdateLine(index, "unitPrice", parseFloat(e.target.value) || 0)
            }
            className={`block w-full px-3 py-1.5 text-sm border rounded-md shadow-sm focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-indigo-500 dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint ${
              errors[`line_${index}_price`]
                ? "border-red-300 dark:border-red-900/40"
                : "border-gray-300 dark:border-graphite-border"
            }`}
            title="Jednotková cena"
          />
        </div>

        {/* Line Total */}
        <div className="col-span-2 flex items-center justify-end">
          <span className="text-sm font-medium text-gray-900 px-3 py-1.5 dark:text-graphite-text">
            {(line.lineTotal || 0).toLocaleString("cs-CZ", {
              minimumFractionDigits: 2,
              maximumFractionDigits: 2,
            })}{" "}
            Kč
          </span>
        </div>

        {/* Line Notes */}
        <div className="col-span-1">
          <input
            type="text"
            value={line.notes || ""}
            onChange={(e) => onUpdateLine(index, "notes", e.target.value)}
            className="block w-full px-3 py-1.5 text-sm border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-indigo-500 dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text dark:placeholder-graphite-faint"
            placeholder="..."
            title="Poznámky k položce"
          />
        </div>

        {/* Remove button */}
        <div className="col-span-1 flex items-center justify-center">
          <button
            type="button"
            onClick={() => onRemoveLine(index)}
            className="text-red-600 hover:text-red-800 transition-colors px-3 py-1.5 dark:text-red-400 dark:hover:text-red-300"
            title="Odstranit položku"
          >
            <Trash2 className="h-4 w-4" />
          </button>
        </div>
      </div>

      {/* Error messages row */}
      {(errors[`line_${index}_quantity`] || errors[`line_${index}_price`]) && (
        <div className="grid grid-cols-12 gap-2 px-2 pb-2">
          <div className="col-span-4"></div>
          <div className="col-span-2">
            {errors[`line_${index}_quantity`] && (
              <p className="text-xs text-red-600 dark:text-red-400">
                {errors[`line_${index}_quantity`]}
              </p>
            )}
          </div>
          <div className="col-span-2">
            {errors[`line_${index}_price`] && (
              <p className="text-xs text-red-600 dark:text-red-400">
                {errors[`line_${index}_price`]}
              </p>
            )}
          </div>
          <div className="col-span-4"></div>
        </div>
      )}
    </div>
  );
};

export default PurchaseOrderLineItem;
