import React from 'react';
import { DashboardTile as DashboardTileType } from '../../../api/hooks/useDashboard';
import { LoadingTile } from './LoadingTile';
import { BackgroundTasksTile } from './BackgroundTasksTile';
import { ProductionTile } from './ProductionTile';
import { CountTile } from './CountTile';
import { InventorySummaryTile } from './InventorySummaryTile';
import { ManualActionRequiredTile } from './ManualActionRequiredTile';
import { PurchaseOrdersInTransitTile } from './PurchaseOrdersInTransitTile';
import { LowStockAlertTile } from './LowStockAlertTile';
import { DefaultTile } from './DefaultTile';
import { Truck, PackageCheck, Package, FileText, Landmark, ClipboardList, Beaker, AlertTriangle, Gift } from 'lucide-react';

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
    // Manufacture tiles
    case 'manualactionrequired':
      return <ManualActionRequiredTile data={tile.data} tileCategory={tile.category} tileTitle={tile.title} />;
    // Purchase tiles
    case 'purchaseordersintransit':
      return <PurchaseOrdersInTransitTile data={tile.data} tileCategory={tile.category} tileTitle={tile.title} />;
    // Transport tiles
    case 'intransitboxes':
      return <CountTile data={tile.data} icon={<Truck className="h-10 w-10" />} iconColor="text-blue-600" tileCategory={tile.category} tileTitle={tile.title} targetUrl="/logistics/transport-boxes" />;
    case 'receivedboxes':
      return <CountTile data={tile.data} icon={<PackageCheck className="h-10 w-10" />} iconColor="text-green-600" tileCategory={tile.category} tileTitle={tile.title} targetUrl="/logistics/transport-boxes" />;
    case 'errorboxes':
      return <CountTile data={tile.data} icon={<Package className="h-10 w-10" />} iconColor="text-indigo-600" tileCategory={tile.category} tileTitle={tile.title} targetUrl="/logistics/transport-boxes" />;
    // Statistics tiles
    case 'invoiceimportstatistics':
      return <CountTile data={tile.data} icon={<FileText className="h-10 w-10" />} iconColor="text-amber-600" tileCategory={tile.category} tileTitle={tile.title} targetUrl="/automation/invoice-import-statistics" />;
    case 'bankstatementimportstatistics':
      return <CountTile data={tile.data} icon={<Landmark className="h-10 w-10" />} iconColor="text-emerald-600" tileCategory={tile.category} tileTitle={tile.title} targetUrl="/finance/bank-statements" />;
    // Inventory tiles
    case 'productinventorycount':
      return <CountTile data={tile.data} icon={<ClipboardList className="h-10 w-10" />} iconColor="text-purple-600" tileCategory={tile.category} tileTitle={tile.title} targetUrl="/logistics/inventory" />;
    case 'materialinventorycount':
      return <CountTile data={tile.data} icon={<Beaker className="h-10 w-10" />} iconColor="text-teal-600" tileCategory={tile.category} tileTitle={tile.title} targetUrl="/manufacturing/inventory" />;
    case 'productinventorysummary':
      return <InventorySummaryTile data={tile.data} targetUrl="/logistics/inventory" />;
    case 'materialwithexpirationinventorysummary':
      return <InventorySummaryTile data={tile.data} targetUrl="/manufacturing/inventory" />;
    case 'materialwithoutexpirationinventorysummary':
      return <InventorySummaryTile data={tile.data} targetUrl="/manufacturing/inventory" />;
    // Purchase efficiency tiles
    case 'lowstockefficiency':
      return <CountTile data={tile.data} icon={<AlertTriangle className="h-10 w-10" />} iconColor="text-orange-600" tileCategory={tile.category} tileTitle={tile.title} targetUrl="/purchase/stock-analysis" />;
    // Gift package tiles
    case 'criticalgiftpackages':
      return <CountTile data={tile.data} icon={<Gift className="h-10 w-10" />} iconColor="text-red-600" tileCategory={tile.category} tileTitle={tile.title} targetUrl="/logistics/gift-package-manufacturing" />;
    // Low stock alert tile
    case 'lowstockalert':
      return <LowStockAlertTile data={tile.data} />;
    default:
      return <DefaultTile data={tile.data} />;
  }
};
