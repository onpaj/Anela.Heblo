import React from 'react';
import { Box, Calendar, MapPin } from 'lucide-react';
import { TransportBoxInfoProps } from './TransportBoxTypes';
import TransportBoxStateBadge from './components/TransportBoxStateBadge';

const TransportBoxInfo: React.FC<TransportBoxInfoProps> = ({
  transportBox,
  descriptionInput,
  handleDescriptionChange,
  isDescriptionChanged,
  isFormEditable,
  formatDate,
}) => {
  return (
    <div className="bg-gray-50 p-3 rounded-lg">
      <h3 className="text-base font-medium text-gray-900 mb-3 flex items-center gap-2">
        <Box className="h-4 w-4" />
        Základní informace
      </h3>
      <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 xl:grid-cols-6 gap-3 text-sm">
        <div>
          <label className="block text-xs font-medium text-gray-600">ID</label>
          <p className="mt-0.5 text-sm text-gray-900 font-medium">{transportBox.id}</p>
        </div>
        <div>
          <label className="block text-xs font-medium text-gray-600">Kód</label>
          <p className="mt-0.5 text-sm text-gray-900 font-medium">{transportBox.code || '-'}</p>
        </div>
        <div>
          <label className="block text-xs font-medium text-gray-600">Stav</label>
          <div className="mt-0.5">
            <TransportBoxStateBadge state={transportBox.state || ''} size="sm" />
          </div>
        </div>
        {/* Location - only show in Reserve state */}
        {transportBox.state === 'Reserve' && (
          <div>
            <label className="block text-xs font-medium text-gray-600">Lokace</label>
            <p className="mt-0.5 text-sm text-gray-900 flex items-center gap-1">
              <MapPin className="h-3 w-3 text-gray-400" />
              {transportBox.location || '-'}
            </p>
          </div>
        )}
        <div>
          <label className="block text-xs font-medium text-gray-600">Položky</label>
          <p className="mt-0.5 text-sm text-gray-900 font-medium">{transportBox.itemCount}</p>
        </div>
        <div className="lg:col-span-2">
          <label className="block text-xs font-medium text-gray-600">Změna</label>
          <p className="mt-0.5 text-xs text-gray-900 flex items-center gap-1">
            <Calendar className="h-3 w-3 text-gray-400 flex-shrink-0" />
            <span className="break-all">{formatDate(transportBox.lastStateChanged)}</span>
          </p>
        </div>
      </div>
      

      {/* Notes/Description Section */}
      <div className="mt-3 border-t border-gray-200 pt-3">
        <label className="block text-xs font-medium text-gray-600 mb-1">Poznámka k boxu</label>
        {isFormEditable('notes') ? (
          <>
            <textarea
              rows={2}
              value={descriptionInput}
              onChange={(e) => handleDescriptionChange(e.target.value)}
              placeholder="Zadejte poznámku..."
              className="w-full px-2 py-1.5 text-sm border border-gray-300 rounded focus:outline-none focus:ring-1 focus:ring-blue-500 focus:border-transparent resize-none"
            />
            {isDescriptionChanged && (
              <p className="mt-1 text-xs text-orange-600">
                Neuložené změny
              </p>
            )}
          </>
        ) : (
          <p className="text-sm text-gray-900 bg-white rounded px-2 py-1.5 border border-gray-200 min-h-[2.5rem] flex items-center">
            {transportBox.description || 
              <span className="text-gray-400 italic">Žádná poznámka</span>}
          </p>
        )}
      </div>
    </div>
  );
};

export default TransportBoxInfo;