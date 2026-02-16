import React, { useState } from "react";
import { Settings } from "lucide-react";
import {
  useLiveHealthCheck,
  useReadyHealthCheck,
} from "../../api/hooks/useHealth";
import {
  useUserDashboardSettings,
  useTileData,
  useSaveDashboardSettings
} from "../../api/hooks/useDashboard";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";
import DashboardGrid from "../dashboard/DashboardGrid";
import DashboardSettings from "../dashboard/DashboardSettings";

const Dashboard: React.FC = () => {
  useLiveHealthCheck();
  useReadyHealthCheck();

  const [showSettings, setShowSettings] = useState(false);

  const { data: userSettings, isLoading: settingsLoading } = useUserDashboardSettings();
  const { data: allTileData = [], isLoading: dataLoading } = useTileData();
  const saveDashboardSettings = useSaveDashboardSettings();

  // Filter visible tiles based on user settings and handle AutoShow tiles
  const visibleTileData = React.useMemo(() => {
    if (!userSettings || !allTileData.length) return [];

    // Get user's visible tile settings
    const userTileSettings = userSettings.tiles.reduce((acc, tile) => {
      acc[tile.tileId] = tile;
      return acc;
    }, {} as Record<string, any>);

    // Filter and sort tiles
    return allTileData
      .filter(tile => {
        const userSetting = userTileSettings[tile.tileId];
        // Show tile if user has it enabled, or if it's AutoShow and not explicitly disabled
        return userSetting?.isVisible || (tile.autoShow && userSetting?.isVisible !== false);
      })
      .sort((a, b) => {
        const aOrder = userTileSettings[a.tileId]?.displayOrder ?? 999;
        const bOrder = userTileSettings[b.tileId]?.displayOrder ?? 999;
        return aOrder - bOrder;
      });
  }, [userSettings, allTileData]);

  const handleReorder = async (tileIds: string[]) => {
    if (!userSettings) return;

    // Create updated tiles with new order
    const updatedTiles = tileIds.map((tileId, index) => {
      const existingTile = userSettings.tiles.find(t => t.tileId === tileId);
      return {
        tileId,
        isVisible: existingTile?.isVisible ?? true,
        displayOrder: index
      };
    });

    // Add tiles that weren't reordered (not visible)
    userSettings.tiles.forEach(tile => {
      if (!tileIds.includes(tile.tileId)) {
        updatedTiles.push(tile);
      }
    });

    await saveDashboardSettings.mutateAsync({ tiles: updatedTiles });
  };

  const isLoading = settingsLoading || dataLoading;

  return (
    <div
      className="flex flex-col w-full"
      style={{ height: PAGE_CONTAINER_HEIGHT }}
      data-testid="dashboard-container"
    >
      {/* Header - Fixed */}
      <div className="flex-shrink-0 mb-3 px-3 sm:px-4 md:px-6 lg:px-8">
        <div className="flex justify-between items-center">
          <div>
            <h1 className="text-2xl md:text-3xl font-bold text-gray-900">
              Dashboard
            </h1>
            <p className="mt-2 text-gray-600 hidden sm:block">
              Přehled systému a aktuálního stavu
            </p>
          </div>
          <button
            onClick={() => setShowSettings(!showSettings)}
            className="flex items-center space-x-2 px-3 py-2 bg-white border border-gray-300 rounded-md shadow-sm hover:bg-gray-50 transition-colors hidden md:flex"
          >
            <Settings className="h-4 w-4" />
            <span className="text-sm font-medium">Nastavení</span>
          </button>
        </div>
      </div>

      {/* Content Area */}
      <div className="flex-1 px-4 sm:px-6 lg:px-8 overflow-auto">
        {showSettings ? (
          <DashboardSettings onClose={() => setShowSettings(false)} />
        ) : (
          <>
            {isLoading ? (
              <div className="flex justify-center items-center h-64">
                <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600"></div>
              </div>
            ) : (
              <DashboardGrid
                tiles={visibleTileData}
                onReorder={handleReorder}
              />
            )}
          </>
        )}
      </div>
    </div>
  );
};

export default Dashboard;
