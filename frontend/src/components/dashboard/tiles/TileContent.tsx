import React from 'react';
import { DashboardTile as DashboardTileType } from '../../../api/hooks/useDashboard';
import { LoadingTile } from './LoadingTile';
import { BackgroundTasksTile } from './BackgroundTasksTile';
import { ProductionTile } from './ProductionTile';
import { CountTile } from './CountTile';
import { InventorySummaryTile } from './InventorySummaryTile';
import { DefaultTile } from './DefaultTile';
import { Truck, PackageCheck, Package, FileText, Landmark, ClipboardList, Beaker } from 'lucide-react';

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
    // Transport tiles
    case 'intransitboxes':
      return <CountTile data={tile.data} icon={<Truck className="h-10 w-10" />} iconColor="text-blue-600" />;
    case 'receivedboxes':
      return <CountTile data={tile.data} icon={<PackageCheck className="h-10 w-10" />} iconColor="text-green-600" />;
    case 'errorboxes':
      return <CountTile data={tile.data} icon={<Package className="h-10 w-10" />} iconColor="text-indigo-600" />;
    // Statistics tiles
    case 'invoiceimportstatistics':
      return <CountTile data={tile.data} icon={<FileText className="h-10 w-10" />} iconColor="text-amber-600" />;
    case 'bankstatementimportstatistics':
      return <CountTile data={tile.data} icon={<Landmark className="h-10 w-10" />} iconColor="text-emerald-600" />;
    // Inventory tiles
    case 'productinventorycount':
      return <CountTile data={tile.data} icon={<ClipboardList className="h-10 w-10" />} iconColor="text-purple-600" />;
    case 'materialinventorycount':
      return <CountTile data={tile.data} icon={<Beaker className="h-10 w-10" />} iconColor="text-teal-600" />;
    case 'productinventorysummary':
      return <InventorySummaryTile data={tile.data} />;
    case 'materialinventorysummary':
      return <InventorySummaryTile data={tile.data} />;
    default:
      return <DefaultTile data={tile.data} />;
  }
};
