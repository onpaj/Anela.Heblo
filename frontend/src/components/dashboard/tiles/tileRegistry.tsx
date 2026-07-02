import React from 'react';
import { DashboardTile as DashboardTileType } from '../../../api/hooks/useDashboard';
import { BackgroundTasksTile } from './BackgroundTasksTile';
import { ProductionTile } from './ProductionTile';
import { ConditionsTile } from './ConditionsTile';
import { ManualActionRequiredTile } from './ManualActionRequiredTile';
import { PurchaseOrdersInTransitTile } from './PurchaseOrdersInTransitTile';
import { CountTile } from './CountTile';
import { InventorySummaryTile } from './InventorySummaryTile';
import { LowStockAlertTile } from './LowStockAlertTile';
import { DataQualityTile } from './DataQualityTile';
import { DqtYesterdayStatusTile } from './DqtYesterdayStatusTile';
import { WeatherForecastTile } from './WeatherForecastTile';
import { FailedJobsTile } from './FailedJobsTile';
import { PackingStatsTile } from './PackingStatsTile';
import { Truck, PackageCheck, Package, FileText, Landmark, ClipboardList, Beaker, AlertTriangle, Gift } from 'lucide-react';

export type TileRenderer = React.FC<{ data: any; tile: DashboardTileType }>;

export const TILE_RENDERERS: Record<string, TileRenderer> = {
  backgroundtaskstatus: ({ data }) => <BackgroundTasksTile data={data} />,
  todayproduction: ({ data, tile }) => <ProductionTile data={data} title={tile.title || 'Dnes'} />,
  nextdayproduction: ({ data, tile }) => <ProductionTile data={data} title={tile.title || 'Zítra'} />,
  // Manufacture tiles
  manufactureconditions: ({ data }) => <ConditionsTile data={data} />,
  manualactionrequired: ({ data, tile }) => (
    <ManualActionRequiredTile data={data} tileCategory={tile.category} tileTitle={tile.title} />
  ),
  // Purchase tiles
  purchaseordersintransit: ({ data, tile }) => (
    <PurchaseOrdersInTransitTile data={data} tileCategory={tile.category} tileTitle={tile.title} />
  ),
  // Transport tiles
  intransitboxes: ({ data, tile }) => (
    <CountTile
      data={data}
      icon={<Truck className="h-10 w-10" />}
      iconColor="text-blue-600"
      tileCategory={tile.category}
      tileTitle={tile.title}
      targetUrl="/logistics/transport-boxes"
    />
  ),
  receivedboxes: ({ data, tile }) => (
    <CountTile
      data={data}
      icon={<PackageCheck className="h-10 w-10" />}
      iconColor="text-green-600"
      tileCategory={tile.category}
      tileTitle={tile.title}
      targetUrl="/logistics/transport-boxes"
    />
  ),
  errorboxes: ({ data, tile }) => (
    <CountTile
      data={data}
      icon={<Package className="h-10 w-10" />}
      iconColor="text-indigo-600"
      tileCategory={tile.category}
      tileTitle={tile.title}
      targetUrl="/logistics/transport-boxes"
    />
  ),
  // Statistics tiles
  invoiceimportstatistics: ({ data, tile }) => (
    <CountTile
      data={data}
      icon={<FileText className="h-10 w-10" />}
      iconColor="text-amber-600"
      tileCategory={tile.category}
      tileTitle={tile.title}
      targetUrl="/automation/invoice-import-statistics"
    />
  ),
  bankstatementimportstatistics: ({ data, tile }) => (
    <CountTile
      data={data}
      icon={<Landmark className="h-10 w-10" />}
      iconColor="text-emerald-600"
      tileCategory={tile.category}
      tileTitle={tile.title}
      targetUrl="/finance/bank-statements"
    />
  ),
  // Inventory tiles
  productinventorycount: ({ data, tile }) => (
    <CountTile
      data={data}
      icon={<ClipboardList className="h-10 w-10" />}
      iconColor="text-purple-600"
      tileCategory={tile.category}
      tileTitle={tile.title}
      targetUrl="/logistics/inventory"
    />
  ),
  materialinventorycount: ({ data, tile }) => (
    <CountTile
      data={data}
      icon={<Beaker className="h-10 w-10" />}
      iconColor="text-teal-600"
      tileCategory={tile.category}
      tileTitle={tile.title}
      targetUrl="/manufacturing/inventory"
    />
  ),
  productinventorysummary: ({ data }) => (
    <InventorySummaryTile data={data} targetUrl="/logistics/inventory" />
  ),
  materialwithexpirationinventorysummary: ({ data }) => (
    <InventorySummaryTile data={data} targetUrl="/manufacturing/inventory" />
  ),
  materialwithoutexpirationinventorysummary: ({ data }) => (
    <InventorySummaryTile data={data} targetUrl="/manufacturing/inventory" />
  ),
  // Purchase efficiency tiles
  lowstockefficiency: ({ data, tile }) => (
    <CountTile
      data={data}
      icon={<AlertTriangle className="h-10 w-10" />}
      iconColor="text-orange-600"
      tileCategory={tile.category}
      tileTitle={tile.title}
      targetUrl="/purchase/stock-analysis"
    />
  ),
  // Gift package tiles
  criticalgiftpackages: ({ data, tile }) => (
    <CountTile
      data={data}
      icon={<Gift className="h-10 w-10" />}
      iconColor="text-red-600"
      tileCategory={tile.category}
      tileTitle={tile.title}
      targetUrl="/logistics/gift-package-manufacturing"
    />
  ),
  // Low stock alert tile
  lowstockalert: ({ data }) => <LowStockAlertTile data={data} />,
  // Data quality tile
  dataqualitystatus: ({ data }) => <DataQualityTile data={data} />,
  dqtyesterdaystatus: ({ data }) => <DqtYesterdayStatusTile data={data} />,
  weatherforecast: ({ data }) => <WeatherForecastTile data={data} />,
  failedjobs: ({ data }) => <FailedJobsTile data={data} />,
  packingstats: ({ data }) => <PackingStatsTile data={data} />,
};
