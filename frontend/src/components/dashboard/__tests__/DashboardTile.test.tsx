import React from 'react';
import { render, screen } from '@testing-library/react';
import { DndContext } from '@dnd-kit/core';
import DashboardTile from '../DashboardTile';
import { DashboardTile as DashboardTileType } from '../../../api/hooks/useDashboard';

// Mock the TileContent component
jest.mock('../tiles', () => ({
  TileHeader: ({ title, dragHandleProps }: any) => (
    <div data-testid="tile-header" {...dragHandleProps}>
      {title}
    </div>
  ),
  TileContent: ({ tile }: any) => (
    <div data-testid="tile-content">{tile.tileId}</div>
  ),
}));

const mockTile: DashboardTileType = {
  tileId: 'test-tile-123',
  title: 'Test Tile',
  description: 'This is a test tile',
  size: 'Medium',
  category: 'Analytics',
  defaultEnabled: true,
  autoShow: false,
  requiredPermissions: ['read'],
  data: { count: 42 }
};

const renderWithDndContext = (component: React.ReactElement) => {
  return render(
    <DndContext id="test-dnd">
      {component}
    </DndContext>
  );
};

describe('DashboardTile', () => {
  it('should render tile with correct testid', () => {
    renderWithDndContext(<DashboardTile tile={mockTile} />);
    
    expect(screen.getByTestId('dashboard-tile-test-tile-123')).toBeInTheDocument();
  });

  it('should render TileHeader with correct title', () => {
    renderWithDndContext(<DashboardTile tile={mockTile} />);
    
    const header = screen.getByTestId('tile-header');
    expect(header).toBeInTheDocument();
    expect(header).toHaveTextContent('Test Tile');
  });

  it('should render TileContent with tile data', () => {
    renderWithDndContext(<DashboardTile tile={mockTile} />);
    
    const content = screen.getByTestId('tile-content');
    expect(content).toBeInTheDocument();
    expect(content).toHaveTextContent('test-tile-123');
  });

  it('should apply correct size classes for Small size', () => {
    const smallTile = { ...mockTile, size: 'Small' as const };
    renderWithDndContext(<DashboardTile tile={smallTile} />);
    
    const tileElement = screen.getByTestId('dashboard-tile-test-tile-123');
    expect(tileElement).toHaveClass('col-span-1', 'row-span-1');
  });

  it('should apply correct size classes for Medium size', () => {
    const mediumTile = { ...mockTile, size: 'Medium' as const };
    renderWithDndContext(<DashboardTile tile={mediumTile} />);
    
    const tileElement = screen.getByTestId('dashboard-tile-test-tile-123');
    expect(tileElement).toHaveClass('col-span-2', 'row-span-1');
  });

  it('should apply correct size classes for Large size', () => {
    const largeTile = { ...mockTile, size: 'Large' as const };
    renderWithDndContext(<DashboardTile tile={largeTile} />);
    
    const tileElement = screen.getByTestId('dashboard-tile-test-tile-123');
    expect(tileElement).toHaveClass('col-span-2', 'row-span-2');
  });

  it('should apply default size classes for unknown size', () => {
    const unknownSizeTile = { ...mockTile, size: 'Unknown' as any };
    renderWithDndContext(<DashboardTile tile={unknownSizeTile} />);
    
    const tileElement = screen.getByTestId('dashboard-tile-test-tile-123');
    expect(tileElement).toHaveClass('col-span-1', 'row-span-1');
  });

  it('should apply custom className when provided', () => {
    renderWithDndContext(<DashboardTile tile={mockTile} className="custom-class" />);
    
    const tileElement = screen.getByTestId('dashboard-tile-test-tile-123');
    expect(tileElement).toHaveClass('custom-class');
  });

  it('should have default styling classes', () => {
    renderWithDndContext(<DashboardTile tile={mockTile} />);
    
    const tileElement = screen.getByTestId('dashboard-tile-test-tile-123');
    expect(tileElement).toHaveClass(
      'bg-white',
      'rounded-lg',
      'shadow-sm',
      'border',
      'border-gray-200',
      'hover:shadow-md',
      'transition-shadow',
      'duration-200'
    );
  });

  it('should handle tile with no data', () => {
    const tileWithNoData = { ...mockTile, data: undefined };
    renderWithDndContext(<DashboardTile tile={tileWithNoData} />);
    
    expect(screen.getByTestId('dashboard-tile-test-tile-123')).toBeInTheDocument();
    expect(screen.getByTestId('tile-content')).toBeInTheDocument();
  });

  it('should handle tile with empty title', () => {
    const tileWithEmptyTitle = { ...mockTile, title: '' };
    renderWithDndContext(<DashboardTile tile={tileWithEmptyTitle} />);
    
    const header = screen.getByTestId('tile-header');
    expect(header).toBeInTheDocument();
    expect(header).toHaveTextContent('');
  });

  it('should handle different tile IDs correctly', () => {
    const tile1 = { ...mockTile, tileId: 'analytics-tile' };
    const tile2 = { ...mockTile, tileId: 'finance-tile' };
    
    const { rerender } = renderWithDndContext(<DashboardTile tile={tile1} />);
    expect(screen.getByTestId('dashboard-tile-analytics-tile')).toBeInTheDocument();
    
    rerender(
      <DndContext id="test-dnd">
        <DashboardTile tile={tile2} />
      </DndContext>
    );
    expect(screen.getByTestId('dashboard-tile-finance-tile')).toBeInTheDocument();
  });

  it('should include padding for tile content', () => {
    renderWithDndContext(<DashboardTile tile={mockTile} />);

    const tileElement = screen.getByTestId('dashboard-tile-test-tile-123');
    const contentWrapper = tileElement.querySelector('.p-4.flex-1');
    expect(contentWrapper).toBeInTheDocument();
  });
});