import React from 'react';
import { ArrowLeft, ArrowRight, Box, Loader2 } from 'lucide-react';
import { TransportBoxActionsProps, stateLabels, stateColors } from './TransportBoxTypes';

const TransportBoxActions: React.FC<TransportBoxActionsProps> = ({
  transportBox,
  changeStateMutation,
  handleStateChange,
}) => {
  const previousTransitions = transportBox.allowedTransitions?.filter(transition => 
    transition.newState && transition.transitionType === 'Previous'
  ) || [];
  
  const nextTransitions = transportBox.allowedTransitions?.filter(transition => 
    transition.newState && transition.transitionType !== 'Previous'
  ) || [];

  return (
    <div className="bg-white border border-gray-200 rounded-lg p-4 h-full">
      <h3 className="text-base font-medium text-gray-900 mb-4 flex items-center gap-2">
        <Box className="h-4 w-4" />
        Navigace stavu
      </h3>
      
      {/* 3 Equal Sections Layout */}
      <div className="flex flex-col h-[calc(100%-4rem)] gap-4">
        {/* Section 1: Previous Transitions */}
        <div className="flex-1 border border-gray-200 rounded-lg p-3 flex flex-col">
          <div className="text-xs text-gray-500 mb-2 uppercase tracking-wider text-center font-semibold">Zpět</div>
          <div className="flex-1 flex flex-col gap-2">
            {previousTransitions.length > 0 ? (
              previousTransitions.map((transition, index) => (
                <button
                  key={`prev-${index}-${transition.newState}`}
                  onClick={() => handleStateChange(transition.newState!)}
                  disabled={changeStateMutation.isPending || transition.systemOnly}
                  className={`flex-1 flex items-center justify-center px-3 rounded-lg transition-colors text-sm font-medium ${
                    transition.systemOnly
                      ? 'bg-gray-100 text-gray-400 cursor-not-allowed'
                      : 'bg-gray-500 text-white hover:bg-gray-600 active:bg-gray-700'
                  } disabled:opacity-50 disabled:cursor-not-allowed`}
                  title={transition.systemOnly ? 'Pouze systémový přechod' : `Změnit na: ${stateLabels[transition.newState!] || transition.newState}`}
                >
                  <div className="flex items-center">
                    {changeStateMutation.isPending ? (
                      <Loader2 className="h-4 w-4 mr-2 animate-spin" />
                    ) : (
                      <ArrowLeft className="h-4 w-4 mr-2" />
                    )}
                    <span>{stateLabels[transition.newState!] || transition.newState}</span>
                  </div>
                </button>
              ))
            ) : (
              <div className="flex-1 flex items-center justify-center text-xs text-gray-400 italic">Žádné kroky zpět</div>
            )}
          </div>
        </div>

        {/* Section 2: Current State */}
        <div className="flex-1 border border-gray-200 rounded-lg p-3 flex flex-col">
          <div className="text-xs text-gray-500 mb-2 uppercase tracking-wider text-center font-semibold">Aktuální</div>
          <div className="flex-1 flex flex-col">
            <div className={`flex-1 rounded-lg border-2 border-dashed flex items-center justify-center ${
              stateColors[transportBox.state || ''] || 'bg-gray-100 text-gray-800 border-gray-300'
            } border-opacity-50`}>
              <div className="flex items-center">
                <Box className="h-5 w-5 mr-2" />
                <span className="text-sm font-medium">{stateLabels[transportBox.state || ''] || transportBox.state}</span>
              </div>
            </div>
          </div>
        </div>

        {/* Section 3: Next Transitions */}
        <div className="flex-1 border border-gray-200 rounded-lg p-3 flex flex-col">
          <div className="text-xs text-gray-500 mb-2 uppercase tracking-wider text-center font-semibold">Vpřed</div>
          <div className="flex-1 flex flex-col gap-2">
            {nextTransitions.length > 0 ? (
              nextTransitions.map((transition, index) => (
                <button
                  key={`next-${index}-${transition.newState}`}
                  onClick={() => handleStateChange(transition.newState!)}
                  disabled={changeStateMutation.isPending || transition.systemOnly}
                  className={`flex-1 flex items-center justify-center px-3 rounded-lg transition-colors text-sm font-medium ${
                    transition.systemOnly
                      ? 'bg-gray-100 text-gray-400 cursor-not-allowed'
                      : transition.newState === 'InTransit' || transition.newState === 'Stocked' || transition.newState === 'Closed'
                      ? 'bg-indigo-600 text-white hover:bg-indigo-700 active:bg-indigo-800'
                      : transition.newState === 'Opened' || transition.newState === 'New'
                      ? 'bg-emerald-600 text-white hover:bg-emerald-700 active:bg-emerald-800'
                      : 'bg-blue-600 text-white hover:bg-blue-700 active:bg-blue-800'
                  } disabled:opacity-50 disabled:cursor-not-allowed`}
                  title={transition.systemOnly ? 'Pouze systémový přechod' : `Změnit na: ${stateLabels[transition.newState!] || transition.newState}`}
                >
                  <div className="flex items-center">
                    {changeStateMutation.isPending ? (
                      <Loader2 className="h-4 w-4 mr-2 animate-spin" />
                    ) : (
                      <ArrowRight className="h-4 w-4 mr-2" />
                    )}
                    <span>{stateLabels[transition.newState!] || transition.newState}</span>
                  </div>
                </button>
              ))
            ) : (
              <div className="flex-1 flex items-center justify-center text-xs text-gray-400 italic">Žádné kroky vpřed</div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};

export default TransportBoxActions;