import React from 'react';
import { DashboardTile as DashboardTileType } from '../../../api/hooks/useDashboard';
import { LoadingTile } from './LoadingTile';
import { BackgroundTasksTile } from './BackgroundTasksTile';
import { ProductionTile } from './ProductionTile';
import { DefaultTile } from './DefaultTile';

interface TileContentProps {
  tile: DashboardTileType;
}

export const TileContent: React.FC<TileContentProps> = ({ tile }) => {
  if (!tile.data) {
    return <LoadingTile />;
  }

  switch (tile.tileId) {
    case 'backgroundtaskstatus':
      return <BackgroundTasksTile data={tile.data} />;
    case 'todayproduction':
      return <ProductionTile data={tile.data} title={tile.title || 'Dnes'} />;
    case 'nextdayproduction':
      return <ProductionTile data={tile.data} title={tile.title || 'Zítra'} />;
    default:
      return <DefaultTile data={tile.data} />;
  }
};
