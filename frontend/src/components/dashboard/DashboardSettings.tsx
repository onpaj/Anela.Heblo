import React, { useState } from 'react';
import { Settings, Eye, EyeOff } from 'lucide-react';
import {
  DashboardTile,
  useAvailableTiles,
  useUserDashboardSettings,
  useEnableTile,
  useDisableTile
} from '../../api/hooks/useDashboard';
import CategoryBadge from './CategoryBadge';
import SizeBadge from './SizeBadge';

interface DashboardSettingsProps {
  onClose?: () => void;
}

const DashboardSettings: React.FC<DashboardSettingsProps> = ({ onClose }) => {
  const { data: availableTiles = [], isLoading: tilesLoading } = useAvailableTiles();
  const { data: userSettings, isLoading: settingsLoading } = useUserDashboardSettings();
  const enableTile = useEnableTile();
  const disableTile = useDisableTile();
  
  const [filter, setFilter] = useState('all'); // 'all', 'enabled', 'disabled'

  const isLoading = tilesLoading || settingsLoading;

  const handleToggleTile = async (tile: DashboardTile) => {
    const userTile = userSettings?.tiles.find(t => t.tileId === tile.tileId);
    const isEnabled = userTile?.isVisible || false;

    if (isEnabled) {
      await disableTile.mutateAsync(tile.tileId);
    } else {
      await enableTile.mutateAsync(tile.tileId);
    }
  };

  const visibleTiles = userSettings?.tiles.filter(t => t.isVisible).map(t => t.tileId) || [];

  const filteredTiles = availableTiles.filter(tile => {
    if (filter === 'enabled') {
      return visibleTiles.includes(tile.tileId);
    }
    if (filter === 'disabled') {
      return !visibleTiles.includes(tile.tileId);
    }
    return true; // 'all'
  });

  const newTilesCount = availableTiles.filter(tile =>
    !visibleTiles.includes(tile.tileId) && tile.defaultEnabled
  ).length;

  if (isLoading) {
    return (
      <div className="p-6">
        <div className="animate-pulse space-y-4">
          <div className="h-6 bg-gray-200 rounded w-1/3"></div>
          <div className="space-y-3">
            {[1, 2, 3].map(i => (
              <div key={i} className="h-16 bg-gray-200 rounded"></div>
            ))}
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="bg-white rounded-lg shadow-lg border border-gray-200 h-full flex flex-col">
      {/* Header */}
      <div className="px-6 py-4 border-b border-gray-200">
        <div className="flex items-center justify-between">
          <div className="flex items-center space-x-2">
            <Settings className="h-5 w-5 text-gray-600" />
            <h2 className="text-lg font-semibold text-gray-900">Nastavení dashboardu</h2>
          </div>
          {onClose && (
            <button
              onClick={onClose}
              className="text-gray-400 hover:text-gray-600 transition-colors"
            >
              ✕
            </button>
          )}
        </div>
        
        {newTilesCount > 0 && (
          <div className="mt-3 bg-blue-50 p-3 rounded-lg">
            <p className="text-sm text-blue-800">
              <strong>Nové dlaždice k dispozici ({newTilesCount})</strong>
            </p>
            <p className="text-xs text-blue-600 mt-1">
              Byly přidány nové dlaždice. Můžete je aktivovat níže.
            </p>
          </div>
        )}
      </div>

      {/* Filter Tabs */}
      <div className="px-6 py-3 border-b border-gray-100">
        <div className="flex space-x-1">
          {[
            { key: 'all', label: 'Vše', count: availableTiles.length },
            { key: 'enabled', label: 'Aktivní', count: visibleTiles.length },
            { key: 'disabled', label: 'Neaktivní', count: availableTiles.length - visibleTiles.length }
          ].map(tab => (
            <button
              key={tab.key}
              onClick={() => setFilter(tab.key)}
              className={`
                px-3 py-1.5 rounded-md text-sm font-medium transition-colors
                ${filter === tab.key 
                  ? 'bg-blue-100 text-blue-700' 
                  : 'text-gray-600 hover:text-gray-900 hover:bg-gray-50'
                }
              `}
            >
              {tab.label} ({tab.count})
            </button>
          ))}
        </div>
      </div>

      {/* Tiles List */}
      <div className="px-6 py-4 flex-1 overflow-y-auto" data-testid="dashboard-settings-tiles">
        <div className="grid grid-cols-1 lg:grid-cols-2 xl:grid-cols-3 gap-3">
          {filteredTiles.map(tile => {
            const userTile = userSettings?.tiles.find(t => t.tileId === tile.tileId);
            const isEnabled = userTile?.isVisible || false;
            const isNew = !visibleTiles.includes(tile.tileId) && tile.defaultEnabled;
            
            return (
              <div 
                key={tile.tileId}
                className={`
                  p-4 rounded-lg border transition-all
                  ${isEnabled ? 'border-green-200 bg-green-50' : 'border-gray-200 bg-white'}
                  ${isNew ? 'ring-2 ring-blue-200' : ''}
                `}
              >
                <div className="flex items-center justify-between">
                  <div className="flex-1">
                    <div className="flex items-center space-x-2">
                      <h3 className="font-medium text-gray-900">{tile.title}</h3>
                      <SizeBadge size={tile.size} />
                      {isNew && (
                        <span className="px-2 py-0.5 bg-blue-100 text-blue-700 rounded-full text-xs font-medium">
                          Nová
                        </span>
                      )}
                    </div>
                    <p className="text-sm text-gray-600 mt-1">{tile.description}</p>
                    <div className="mt-2">
                      <CategoryBadge category={tile.category} />
                    </div>
                  </div>
                  
                  <button
                    onClick={() => handleToggleTile(tile)}
                    disabled={enableTile.isPending || disableTile.isPending}
                    className={`
                      flex items-center space-x-1 px-3 py-1.5 rounded-md text-sm font-medium
                      transition-colors disabled:opacity-50
                      ${isEnabled 
                        ? 'bg-red-100 text-red-700 hover:bg-red-200' 
                        : 'bg-green-100 text-green-700 hover:bg-green-200'
                      }
                    `}
                  >
                    {isEnabled ? (
                      <>
                        <EyeOff className="h-4 w-4" />
                        <span>Skrýt</span>
                      </>
                    ) : (
                      <>
                        <Eye className="h-4 w-4" />
                        <span>Zobrazit</span>
                      </>
                    )}
                  </button>
                </div>
              </div>
            );
          })}
        </div>

        {filteredTiles.length === 0 && (
          <div className="col-span-full text-center py-8 text-gray-500">
            <p>Žádné dlaždice v této kategorii</p>
          </div>
        )}
      </div>
    </div>
  );
};

export default DashboardSettings;