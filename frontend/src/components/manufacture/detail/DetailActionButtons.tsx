import React from "react";
import {
  Ban,
  Copy,
  Save,
  XCircle,
  Loader2,
  Calculator,
} from "lucide-react";
import { ManufactureOrderState } from "../../../api/generated/api-client";

interface DetailActionButtonsProps {
  order: any;
  onCancel: () => void;
  onDuplicate: () => void;
  onClose: () => void;
  onSave: () => void;
  onBatchCalculator?: () => void;
  isUpdateLoading: boolean;
  isDuplicateLoading: boolean;
}

export const DetailActionButtons: React.FC<DetailActionButtonsProps> = ({
  order,
  onCancel,
  onDuplicate,
  onClose,
  onSave,
  onBatchCalculator,
  isUpdateLoading,
  isDuplicateLoading,
}) => {
  return (
    <div className="border-t border-gray-200 p-3 flex-shrink-0">
      <div className="flex items-center justify-between">
        {/* Cancel button on the left */}
        <div>
          {order && order.state !== ManufactureOrderState.Completed && order.state !== ManufactureOrderState.Cancelled && (
            <button
              onClick={onCancel}
              disabled={isUpdateLoading}
              className="flex items-center px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700 transition-colors text-sm disabled:opacity-50 disabled:cursor-not-allowed"
              title="Stornovat zakázku"
            >
              <Ban className="h-4 w-4 mr-1" />
              Stornovat
            </button>
          )}
        </div>

        {/* Center buttons */}
        <div className="flex items-center space-x-2">
          {/* Duplicate button */}
          {order && (
            <button
              onClick={onDuplicate}
              disabled={isDuplicateLoading}
              className="flex items-center px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 transition-colors text-sm disabled:opacity-50 disabled:cursor-not-allowed"
              title="Duplikovat zakázku"
            >
              {isDuplicateLoading ? (
                <Loader2 className="h-4 w-4 mr-1 animate-spin" />
              ) : (
                <Copy className="h-4 w-4 mr-1" />
              )}
              Duplikovat
            </button>
          )}

          {/* Batch Calculator button */}
          {order && order.semiProduct && onBatchCalculator && (
            <button
              onClick={onBatchCalculator}
              className="flex items-center px-4 py-2 bg-emerald-600 text-white rounded-lg hover:bg-emerald-700 transition-colors text-sm"
              title="Otevřít kalkulačku dávek s přednastavenými hodnotami"
            >
              <Calculator className="h-4 w-4 mr-1" />
              Kalkulačka dávek
            </button>
          )}
        </div>
        
        {/* Close and Save buttons on the right */}
        <div className="flex items-center space-x-2">
        <button
          onClick={onClose}
          className="flex items-center px-4 py-2 bg-gray-500 text-white rounded-lg hover:bg-gray-600 transition-colors text-sm"
        >
          <XCircle className="h-4 w-4 mr-1" />
          Close
        </button>
        <button
          onClick={onSave}
          disabled={isUpdateLoading}
          className="flex items-center px-4 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 transition-colors text-sm disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {isUpdateLoading ? (
            <Loader2 className="h-4 w-4 mr-1 animate-spin" />
          ) : (
            <Save className="h-4 w-4 mr-1" />
          )}
          Save
        </button>
        </div>
      </div>
    </div>
  );
};

export default DetailActionButtons;