import React from 'react';
import { render, screen } from '@testing-library/react';
import { TileContent } from '../TileContent';
import { DashboardTile as DashboardTileType } from '../../../../api/hooks/useDashboard';

// Mock all tile components
jest.mock('../LoadingTile', () => ({
  LoadingTile: () => <div data-testid="loading-tile">Loading...</div>
}));

jest.mock('../BackgroundTasksTile', () => ({
  BackgroundTasksTile: ({ data }: any) => <div data-testid="background-tasks-tile">{JSON.stringify(data)}</div>
}));

jest.mock('../ProductionTile', () => ({
  ProductionTile: ({ data, title }: any) => <div data-testid="production-tile">{title}: {JSON.stringify(data)}</div>
}));

jest.mock('../CountTile', () => ({
  CountTile: ({ data, icon, iconColor }: any) => (
    <div data-testid="count-tile" data-icon-color={iconColor}>
      {JSON.stringify(data)}
    </div>
  )
}));

jest.mock('../InventorySummaryTile', () => ({
  InventorySummaryTile: ({ data }: any) => <div data-testid="inventory-summary-tile">{JSON.stringify(data)}</div>
}));

jest.mock('../DefaultTile', () => ({
  DefaultTile: ({ data }: any) => <div data-testid="default-tile">{JSON.stringify(data)}</div>
}));

const createMockTile = (tileId: string, data?: any): DashboardTileType => ({
  tileId,
  title: `${tileId} Title`,
  description: `${tileId} Description`,
  size: 'Medium',
  category: 'Test',
  defaultEnabled: true,
  autoShow: false,
  requiredPermissions: [],
  data
});

describe('TileContent', () => {
  it('should render LoadingTile when data is null', () => {
    const tile = createMockTile('test-tile', null);
    render(<TileContent tile={tile} />);
    
    expect(screen.getByTestId('loading-tile')).toBeInTheDocument();
    expect(screen.getByText('Loading...')).toBeInTheDocument();
  });

  it('should render LoadingTile when data is undefined', () => {
    const tile = createMockTile('test-tile', undefined);
    render(<TileContent tile={tile} />);
    
    expect(screen.getByTestId('loading-tile')).toBeInTheDocument();
  });

  it('should render BackgroundTasksTile for backgroundtaskstatus', () => {
    const data = { running: 3, pending: 1 };
    const tile = createMockTile('backgroundtaskstatus', data);
    render(<TileContent tile={tile} />);
    
    expect(screen.getByTestId('background-tasks-tile')).toBeInTheDocument();
    expect(screen.getByText(JSON.stringify(data))).toBeInTheDocument();
  });

  it('should render ProductionTile for todayproduction with default title', () => {
    const data = { totalOrders: 2, products: [{ productName: 'Test Product', semiProductCompleted: true, productsCompleted: false }] };
    const tile = { ...createMockTile('todayproduction', data), title: undefined }; // Let TileContent use default 'Dnes'
    render(<TileContent tile={tile} />);
    
    expect(screen.getByTestId('production-tile')).toBeInTheDocument();
    expect(screen.getByText(/Dnes:/)).toBeInTheDocument();
  });

  it('should render ProductionTile for nextdayproduction with default title', () => {
    const data = { totalOrders: 1, products: [{ productName: 'Tomorrow Product', semiProductCompleted: false, productsCompleted: false }] };
    const tile = { ...createMockTile('nextdayproduction', data), title: undefined }; // Let TileContent use default 'Zítra'
    render(<TileContent tile={tile} />);
    
    expect(screen.getByTestId('production-tile')).toBeInTheDocument();
    expect(screen.getByText(/Zítra:/)).toBeInTheDocument();
  });

  it('should render ProductionTile with custom title when provided', () => {
    const data = { planned: 100, completed: 75 };
    const tile = { ...createMockTile('todayproduction', data), title: 'Custom Production Title' };
    render(<TileContent tile={tile} />);
    
    expect(screen.getByTestId('production-tile')).toBeInTheDocument();
    expect(screen.getByText(/Custom Production Title:/)).toBeInTheDocument();
  });

  // Transport tiles
  it('should render CountTile with truck icon for intransitboxes', () => {
    const data = { count: 5 };
    const tile = createMockTile('intransitboxes', data);
    render(<TileContent tile={tile} />);
    
    const countTile = screen.getByTestId('count-tile');
    expect(countTile).toBeInTheDocument();
    expect(countTile).toHaveAttribute('data-icon-color', 'text-blue-600');
  });

  it('should render CountTile with package check icon for receivedboxes', () => {
    const data = { count: 12 };
    const tile = createMockTile('receivedboxes', data);
    render(<TileContent tile={tile} />);
    
    const countTile = screen.getByTestId('count-tile');
    expect(countTile).toBeInTheDocument();
    expect(countTile).toHaveAttribute('data-icon-color', 'text-green-600');
  });

  it('should render CountTile with package icon for errorboxes', () => {
    const data = { count: 2 };
    const tile = createMockTile('errorboxes', data);
    render(<TileContent tile={tile} />);
    
    const countTile = screen.getByTestId('count-tile');
    expect(countTile).toBeInTheDocument();
    expect(countTile).toHaveAttribute('data-icon-color', 'text-indigo-600');
  });

  // Statistics tiles
  it('should render CountTile with file text icon for invoiceimportstatistics', () => {
    const data = { imported: 25, failed: 1 };
    const tile = createMockTile('invoiceimportstatistics', data);
    render(<TileContent tile={tile} />);
    
    const countTile = screen.getByTestId('count-tile');
    expect(countTile).toBeInTheDocument();
    expect(countTile).toHaveAttribute('data-icon-color', 'text-amber-600');
  });

  it('should render CountTile with landmark icon for bankstatementimportstatistics', () => {
    const data = { processed: 10 };
    const tile = createMockTile('bankstatementimportstatistics', data);
    render(<TileContent tile={tile} />);
    
    const countTile = screen.getByTestId('count-tile');
    expect(countTile).toBeInTheDocument();
    expect(countTile).toHaveAttribute('data-icon-color', 'text-emerald-600');
  });

  // Inventory tiles
  it('should render CountTile with clipboard icon for productinventorycount', () => {
    const data = { count: 150 };
    const tile = createMockTile('productinventorycount', data);
    render(<TileContent tile={tile} />);
    
    const countTile = screen.getByTestId('count-tile');
    expect(countTile).toBeInTheDocument();
    expect(countTile).toHaveAttribute('data-icon-color', 'text-purple-600');
  });

  it('should render CountTile with beaker icon for materialinventorycount', () => {
    const data = { count: 75 };
    const tile = createMockTile('materialinventorycount', data);
    render(<TileContent tile={tile} />);
    
    const countTile = screen.getByTestId('count-tile');
    expect(countTile).toBeInTheDocument();
    expect(countTile).toHaveAttribute('data-icon-color', 'text-teal-600');
  });

  it('should render InventorySummaryTile for productinventorysummary', () => {
    const data = { total: 1000, available: 850, reserved: 150 };
    const tile = createMockTile('productinventorysummary', data);
    render(<TileContent tile={tile} />);
    
    expect(screen.getByTestId('inventory-summary-tile')).toBeInTheDocument();
    expect(screen.getByText(JSON.stringify(data))).toBeInTheDocument();
  });

  it('should render InventorySummaryTile for materialwithexpirationinventorysummary', () => {
    const data = { total: 500, available: 400, reserved: 100 };
    const tile = createMockTile('materialwithexpirationinventorysummary', data);
    render(<TileContent tile={tile} />);

    expect(screen.getByTestId('inventory-summary-tile')).toBeInTheDocument();
    expect(screen.getByText(JSON.stringify(data))).toBeInTheDocument();
  });

  it('should render InventorySummaryTile for materialwithoutexpirationinventorysummary', () => {
    const data = { total: 300, available: 250, reserved: 50 };
    const tile = createMockTile('materialwithoutexpirationinventorysummary', data);
    render(<TileContent tile={tile} />);

    expect(screen.getByTestId('inventory-summary-tile')).toBeInTheDocument();
    expect(screen.getByText(JSON.stringify(data))).toBeInTheDocument();
  });

  it('should render DefaultTile for unknown tile types', () => {
    const data = { custom: 'data' };
    const tile = createMockTile('unknown-tile-type', data);
    render(<TileContent tile={tile} />);
    
    expect(screen.getByTestId('default-tile')).toBeInTheDocument();
    expect(screen.getByText(JSON.stringify(data))).toBeInTheDocument();
  });

  it('should render DefaultTile for empty string tile ID', () => {
    const data = { test: 'data' };
    const tile = createMockTile('', data);
    render(<TileContent tile={tile} />);
    
    expect(screen.getByTestId('default-tile')).toBeInTheDocument();
  });

  it('should handle tile with empty data object', () => {
    const tile = createMockTile('backgroundtaskstatus', {});
    render(<TileContent tile={tile} />);
    
    expect(screen.getByTestId('background-tasks-tile')).toBeInTheDocument();
    expect(screen.getByText('{}')).toBeInTheDocument();
  });

  it('should handle tile with complex data structure', () => {
    const complexData = {
      nested: { value: 42 },
      array: [1, 2, 3],
      string: 'test'
    };
    const tile = createMockTile('backgroundtaskstatus', complexData);
    render(<TileContent tile={tile} />);
    
    expect(screen.getByTestId('background-tasks-tile')).toBeInTheDocument();
  });

  it('should pass correct props to ProductionTile variants', () => {
    const todayData = { planned: 100 };
    const nextDayData = { planned: 50 };
    
    // Test today production
    const todayTile = { ...createMockTile('todayproduction', todayData), title: undefined };
    const { rerender } = render(<TileContent tile={todayTile} />);
    expect(screen.getByTestId('production-tile')).toBeInTheDocument();
    expect(screen.getByText(/Dnes:/)).toBeInTheDocument();
    
    // Test next day production
    const nextDayTile = { ...createMockTile('nextdayproduction', nextDayData), title: undefined };
    rerender(<TileContent tile={nextDayTile} />);
    expect(screen.getByTestId('production-tile')).toBeInTheDocument();
    expect(screen.getByText(/Zítra:/)).toBeInTheDocument();
  });
});