import React from 'react';
import { Box, Calendar, MapPin } from 'lucide-react';
import { TransportBoxInfoProps } from './TransportBoxTypes';
import TransportBoxStateBadge from './components/TransportBoxStateBadge';

const TransportBoxInfo: React.FC<TransportBoxInfoProps> = ({
  transportBox,
  boxNumberInput,
  setBoxNumberInput,
  boxNumberError,
  descriptionInput,
  handleDescriptionChange,
  isDescriptionChanged,
  isFormEditable,
  handleBoxNumberSubmit,
  formatDate,
}) => {
  return (
    <div className="bg-gray-50 p-4 rounded-lg">
      <h3 className="text-lg font-medium text-gray-900 mb-4 flex items-center gap-2">
        <Box className="h-5 w-5" />
        Základní informace
      </h3>
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
        <div>
          <label className="block text-sm font-medium text-gray-700">ID</label>
          <p className="mt-1 text-sm text-gray-900">{transportBox.id}</p>
        </div>
        <div>
          <label className="block text-sm font-medium text-gray-700">Kód</label>
          <p className="mt-1 text-sm text-gray-900">{transportBox.code || '-'}</p>
        </div>
        <div>
          <label className="block text-sm font-medium text-gray-700">Stav</label>
          <div className="mt-1">
            <TransportBoxStateBadge state={transportBox.state || ''} />
          </div>
        </div>
        {/* Location - only show in Reserve state */}
        {transportBox.state === 'Reserve' && (
          <div>
            <label className="block text-sm font-medium text-gray-700">Lokace</label>
            <p className="mt-1 text-sm text-gray-900 flex items-center gap-1">
              <MapPin className="h-4 w-4 text-gray-400" />
              {transportBox.location || '-'}
            </p>
          </div>
        )}
        <div>
          <label className="block text-sm font-medium text-gray-700">Počet položek</label>
          <p className="mt-1 text-sm text-gray-900">{transportBox.itemCount}</p>
        </div>
        <div>
          <label className="block text-sm font-medium text-gray-700">Poslední změna</label>
          <p className="mt-1 text-sm text-gray-900 flex items-center gap-1">
            <Calendar className="h-4 w-4 text-gray-400" />
            {formatDate(transportBox.lastStateChanged)}
          </p>
        </div>
      </div>
      
      {/* Box Number Input Form - only for New state */}
      {transportBox.state === 'New' && (
        <div className="mt-6 pt-4 border-t border-gray-200">
          <form onSubmit={handleBoxNumberSubmit} className="flex flex-col">
            <div className="flex items-center gap-3">
              <label htmlFor="boxNumberInput" className="text-sm font-medium text-gray-700">
                Číslo boxu:
              </label>
              <div className="flex items-center gap-2">
                <input
                  id="boxNumberInput"
                  type="text"
                  value={boxNumberInput}
                  onChange={(e) => setBoxNumberInput(e.target.value.toUpperCase())}
                  placeholder="B001"
                  maxLength={4}
                  className={`w-24 px-3 py-2 text-lg font-mono border rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent ${
                    boxNumberError ? 'border-red-300' : 'border-gray-300'
                  }`}
                  style={{ fontSize: '16px' }} // Prevent iOS zoom on focus
                />
                <button
                  type="submit"
                  disabled={!boxNumberInput.trim()}
                  className="px-4 py-2 text-sm font-medium text-white bg-indigo-600 border border-transparent rounded-md hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  Přiřadit
                </button>
              </div>
            </div>
            {boxNumberError && (
              <div className="mt-2 text-sm text-red-600">
                {boxNumberError}
              </div>
            )}
            <div className="mt-1 text-xs text-gray-500">
              Zadání čísla otevře box (B + 3 číslice)
            </div>
          </form>
        </div>
      )}

      {/* Notes/Description Section */}
      <div className="mt-4">
        <label className="block text-sm font-medium text-gray-700 mb-2">Poznámka k boxu</label>
        {isFormEditable('notes') ? (
          <>
            <textarea
              rows={3}
              value={descriptionInput}
              onChange={(e) => handleDescriptionChange(e.target.value)}
              placeholder="Zadejte poznámku k tomuto boxu..."
              className="w-full px-3 py-2 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
            />
            <p className="mt-1 text-xs text-gray-600">
              Poznámka se automaticky uloží při změně stavu boxu.
              {isDescriptionChanged && <span className="text-orange-600 ml-1">(Máte neuložené změny)</span>}
            </p>
          </>
        ) : (
          <p className="text-sm text-gray-900">
            {transportBox.description || 
              <span className="text-gray-400 italic">Žádná poznámka</span>}
          </p>
        )}
      </div>
    </div>
  );
};

export default TransportBoxInfo;