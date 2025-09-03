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