import React from 'react';
import { ArrowLeft, ArrowRight, Box, Loader2 } from 'lucide-react';
import { TransportBoxActionsProps, stateLabels, stateColors } from './TransportBoxTypes';

const TransportBoxActions: React.FC<TransportBoxActionsProps> = ({
  transportBox,
  changeStateMutation,
  handleStateChange,
  onClose,
}) => {
  const BUTTON_HEIGHT = 80; // Total height in pixels for all transition groups
  
  const previousTransitions = transportBox.allowedTransitions?.filter(transition => 
    transition.newState && transition.transitionType === 'Previous'
  ) || [];
  
  const nextTransitions = transportBox.allowedTransitions?.filter(transition => 
    transition.newState && transition.transitionType !== 'Previous'
  ) || [];
  
  // Calculate individual button heights based on count
  const prevButtonHeight = previousTransitions.length > 0 ? 
    (BUTTON_HEIGHT - (previousTransitions.length - 1) * 8) / previousTransitions.length : 0; // 8px gap between buttons
  
  const nextButtonHeight = nextTransitions.length > 0 ? 
    (BUTTON_HEIGHT - (nextTransitions.length - 1) * 8) / nextTransitions.length : 0;

  return (
    <div className="pt-6 border-t border-gray-200">
      <div className="flex items-center justify-between">
        {/* Close Button */}
        <button
          onClick={onClose}
          className="px-4 py-2 bg-gray-200 text-gray-800 rounded-md hover:bg-gray-300 transition-colors"
        >
          Zavřít
        </button>
        
        {/* State Transition Flow: Previous → Current → Next */}
        <div className="flex items-center gap-3">
          {/* Previous Transitions */}
          {previousTransitions.length > 0 && (
            <div className="flex flex-col gap-2" style={{ height: `${BUTTON_HEIGHT}px` }}>
              {previousTransitions.map((transition, index) => (
                <button
                  key={`prev-${index}-${transition.newState}`}
                  onClick={() => handleStateChange(transition.newState!)}
                  disabled={changeStateMutation.isPending || transition.systemOnly}
                  className={`flex items-center justify-center px-4 py-1 rounded-md transition-colors min-w-32 ${
                    transition.systemOnly
                      ? 'bg-gray-100 text-gray-400 cursor-not-allowed'
                      : 'bg-gray-500 text-white hover:bg-gray-600'
                  } disabled:opacity-50 disabled:cursor-not-allowed`}
                  style={{ height: `${prevButtonHeight}px` }}
                  title={transition.systemOnly ? 'Pouze systémový přechod' : `Změnit na: ${stateLabels[transition.newState!] || transition.newState}`}
                >
                  <div className="flex items-center">
                    {changeStateMutation.isPending ? (
                      <Loader2 className="h-4 w-4 mr-2 animate-spin" />
                    ) : (
                      <ArrowLeft className="h-4 w-4 mr-2" />
                    )}
                    <span className="text-sm">{stateLabels[transition.newState!] || transition.newState}</span>
                  </div>
                </button>
              ))}
            </div>
          )}
          
          {/* Current State (display only) */}
          <div className={`inline-flex flex-col items-center justify-center px-4 py-2 rounded-md border-2 border-dashed min-w-32 ${
            stateColors[transportBox.state || ''] || 'bg-gray-100 text-gray-800 border-gray-300'
          } border-opacity-50 relative`}
               style={{ height: `${BUTTON_HEIGHT}px` }}>
            <div className="text-xs text-gray-500 mb-1">AKTUÁLNÍ</div>
            <div className="flex items-center">
              <Box className="h-4 w-4 mr-2" />
              <span className="text-sm">{stateLabels[transportBox.state || ''] || transportBox.state}</span>
            </div>
          </div>
          
          {/* Next Transitions */}
          {nextTransitions.length > 0 && (
            <div className="flex flex-col gap-2" style={{ height: `${BUTTON_HEIGHT}px` }}>
              {nextTransitions.map((transition, index) => (
                <button
                  key={`next-${index}-${transition.newState}`}
                  onClick={() => handleStateChange(transition.newState!)}
                  disabled={changeStateMutation.isPending || transition.systemOnly}
                  className={`flex items-center justify-center px-4 py-1 rounded-md transition-colors min-w-32 ${
                    transition.systemOnly
                      ? 'bg-gray-100 text-gray-400 cursor-not-allowed'
                      : transition.newState === 'InTransit' || transition.newState === 'Stocked' || transition.newState === 'Closed'
                      ? 'bg-indigo-600 text-white hover:bg-indigo-700'
                      : transition.newState === 'Opened' || transition.newState === 'New'
                      ? 'bg-emerald-600 text-white hover:bg-emerald-700'
                      : 'bg-blue-600 text-white hover:bg-blue-700'
                  } disabled:opacity-50 disabled:cursor-not-allowed`}
                  style={{ height: `${nextButtonHeight}px` }}
                  title={transition.systemOnly ? 'Pouze systémový přechod' : `Změnit na: ${stateLabels[transition.newState!] || transition.newState}`}
                >
                  <div className="flex items-center">
                    {changeStateMutation.isPending ? (
                      <Loader2 className="h-4 w-4 mr-2 animate-spin" />
                    ) : (
                      <ArrowRight className="h-4 w-4 mr-2" />
                    )}
                    <span className="text-sm">{stateLabels[transition.newState!] || transition.newState}</span>
                  </div>
                </button>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

export default TransportBoxActions;