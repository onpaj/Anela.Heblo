import React, { useState, useEffect } from 'react';
import { X, MapPin, Loader2 } from 'lucide-react';
import { useChangeTransportBoxState } from '../../api/hooks/useTransportBoxes';
import { TransportBoxState } from '../../api/generated/api-client';

interface LocationSelectionModalProps {
  isOpen: boolean;
  onClose: () => void;
  boxId: number | null;
  onSuccess: () => void;
}

// Available locations from TransportBoxLocation enum
const LOCATIONS = [
  { value: 'Kumbal', label: 'Kumbal' },
  { value: 'Relax', label: 'Relax' },
  { value: 'SkladSkla', label: 'Sklad Skla' },
];

// LocalStorage key for storing last selected location
const LAST_LOCATION_KEY = 'transportBox_lastSelectedLocation';

const LocationSelectionModal: React.FC<LocationSelectionModalProps> = ({
  isOpen,
  onClose,
  boxId,
  onSuccess,
}) => {
  const [selectedLocation, setSelectedLocation] = useState('');
  const [error, setError] = useState<string | null>(null);
  const changeStateMutation = useChangeTransportBoxState();

  // Load last selected location from localStorage when modal opens
  useEffect(() => {
    if (isOpen) {
      const lastLocation = localStorage.getItem(LAST_LOCATION_KEY);
      if (lastLocation && LOCATIONS.some(l => l.value === lastLocation)) {
        setSelectedLocation(lastLocation);
      } else {
        setSelectedLocation('');
      }
      setError(null);
    }
  }, [isOpen]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedLocation || !boxId) return;

    setError(null);

    try {
      await changeStateMutation.mutateAsync({
        boxId,
        newState: TransportBoxState.Reserve,
        location: selectedLocation
      });
      
      // Save selected location to localStorage for next time
      localStorage.setItem(LAST_LOCATION_KEY, selectedLocation);
      
      onSuccess();
      setSelectedLocation(''); // Reset form
    } catch (err) {
      console.error('Error changing to InReserve state:', err);
      setError(err instanceof Error ? err.message : 'Neočekávaná chyba');
    }
  };

  const handleClose = () => {
    setSelectedLocation('');
    setError(null);
    onClose();
  };

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 bg-gray-600 bg-opacity-50 overflow-y-auto h-full w-full z-50">
      <div className="relative top-20 mx-auto p-5 border w-96 shadow-lg rounded-md bg-white">
        {/* Header */}
        <div className="flex items-center justify-between mb-4">
          <div className="flex items-center gap-2">
            <MapPin className="h-5 w-5 text-indigo-600" />
            <h3 className="text-lg font-medium text-gray-900">
              Výběr lokace
            </h3>
          </div>
          <button
            onClick={handleClose}
            className="text-gray-400 hover:text-gray-600 transition-colors"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        {/* Content */}
        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label htmlFor="location" className="block text-sm font-medium text-gray-700 mb-2">
              Vyberte lokaci pro rezervu:
            </label>
            <select
              id="location"
              value={selectedLocation}
              onChange={(e) => setSelectedLocation(e.target.value)}
              className="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent"
              required
            >
              <option value="">-- Vyberte lokaci --</option>
              {LOCATIONS.map((location) => (
                <option key={location.value} value={location.value}>
                  {location.label}
                </option>
              ))}
            </select>
          </div>

          {error && (
            <div className="text-sm text-red-600 bg-red-50 p-3 rounded-md">
              {error}
            </div>
          )}

          {/* Actions */}
          <div className="flex justify-end gap-3 pt-4">
            <button
              type="button"
              onClick={handleClose}
              className="px-4 py-2 text-sm font-medium text-gray-700 bg-gray-200 rounded-md hover:bg-gray-300 transition-colors"
            >
              Zrušit
            </button>
            <button
              type="submit"
              disabled={!selectedLocation || changeStateMutation.isPending}
              className="px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-md hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors flex items-center gap-2"
            >
              {changeStateMutation.isPending ? (
                <>
                  <Loader2 className="h-4 w-4 animate-spin" />
                  Ukládám...
                </>
              ) : (
                'Přesunout do rezervy'
              )}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};

export default LocationSelectionModal;