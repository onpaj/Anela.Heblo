import React from 'react';
import { DashboardTile as DashboardTileType } from '../../../api/hooks/useDashboard';
import { LoadingTile } from './LoadingTile';
import { UnauthorizedTile } from './UnauthorizedTile';
import { DefaultTile } from './DefaultTile';
import { TILE_RENDERERS } from './tileRegistry';

interface TileContentProps {
  tile: DashboardTileType;
}

export const TileContent: React.FC<TileContentProps> = ({ tile }) => {
  if (tile.isUnauthorized) {
    return <UnauthorizedTile />;
  }

  if (!tile.data) {
    return <LoadingTile />;
  }

  const Renderer = TILE_RENDERERS[tile.tileId];
  return Renderer ? <Renderer data={tile.data} tile={tile} /> : <DefaultTile data={tile.data} />;
};
