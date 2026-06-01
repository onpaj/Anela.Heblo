import React from "react";
import {
  ChevronLeft,
  ChevronRight,
  Loader2,
} from "lucide-react";
import { ManufactureOrderState } from "../../../api/generated/api-client";

interface StateTransitionControlsProps {
  order: any;
  currentStateTransitions: {
    next: ManufactureOrderState | null;
    previous: ManufactureOrderState | null;
  };
  onStateChange: (state: ManufactureOrderState) => void;
  isLoading: boolean;
  getStateLabel: (state: ManufactureOrderState) => string;
}

const stateColors: Record<ManufactureOrderState, string> = {
  [ManufactureOrderState.Draft]: "bg-gray-100 text-gray-800",
  [ManufactureOrderState.Planned]: "bg-blue-100 text-blue-800",
  [ManufactureOrderState.SemiProductManufactured]: "bg-yellow-100 text-yellow-800",
  [ManufactureOrderState.Completed]: "bg-green-100 text-green-800",
  [ManufactureOrderState.Cancelled]: "bg-red-100 text-red-800",
};

export const StateTransitionControls: React.FC<StateTransitionControlsProps> = ({
  order,
  currentStateTransitions,
  onStateChange,
  isLoading,
  getStateLabel,
}) => {
  return (
    <div className="flex items-center space-x-2">
      {/* Previous State Button */}
      {order && order.state !== ManufactureOrderState.Cancelled && currentStateTransitions.previous !== null && (
        <button
          onClick={() => onStateChange(currentStateTransitions.previous!)}
          disabled={isLoading}
          className="flex items-center px-4 py-3 bg-gray-500 text-white rounded-lg hover:bg-gray-600 transition-colors text-sm disabled:opacity-50 disabled:cursor-not-allowed border-2 border-gray-500 hover:border-gray-600"
          title={`Zpět na: ${getStateLabel(currentStateTransitions.previous!)}`}
        >
          {isLoading ? (
            <Loader2 className="h-4 w-4 mr-1 animate-spin" />
          ) : (
            <ChevronLeft className="h-4 w-4 mr-1" />
          )}
          {getStateLabel(currentStateTransitions.previous!)}
        </button>
      )}
      
      {/* Current State Display - Always visible */}
      {order && order.state !== undefined && (
        <div className="flex items-center px-4 py-2 bg-gray-100 border-2 border-gray-300 rounded-lg">
          <span className={`inline-flex items-center px-3 py-1 rounded-full text-sm font-medium ${stateColors[order.state as ManufactureOrderState]}`}>
            {getStateLabel(order.state)}
          </span>
        </div>
      )}
      
      {/* Next State Button */}
      {order && order.state !== ManufactureOrderState.Cancelled && currentStateTransitions.next !== null && (
        <button
          onClick={() => onStateChange(currentStateTransitions.next!)}
          disabled={isLoading}
          className="flex items-center px-4 py-3 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 transition-colors text-sm disabled:opacity-50 disabled:cursor-not-allowed border-2 border-indigo-600 hover:border-indigo-700"
          title={`Pokračovat na: ${getStateLabel(currentStateTransitions.next!)}`}
        >
          {isLoading ? (
            <Loader2 className="h-4 w-4 mr-1 animate-spin" />
          ) : (
            <>
              {getStateLabel(currentStateTransitions.next!)}
              <ChevronRight className="h-4 w-4 ml-1" />
            </>
          )}
        </button>
      )}
    </div>
  );
};

export default StateTransitionControls;