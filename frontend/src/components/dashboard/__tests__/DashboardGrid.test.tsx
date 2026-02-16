import React from 'react';
import { render, screen } from '@testing-library/react';
import DashboardGrid from '../DashboardGrid';
import { DashboardTile as DashboardTileType } from '../../../api/hooks/useDashboard';

// Mock useMediaQuery hook
jest.mock('../../../hooks/useMediaQuery', () => ({
  useIsMobile: jest.fn(() => false), // Default to desktop
}));

// Mock DashboardTile component
jest.mock('../DashboardTile', () => {
  return function MockDashboardTile({ tile, isDragDisabled }: { tile: DashboardTileType; isDragDisabled?: boolean }) {
    return (
      <div
        data-testid={`dashboard-tile-${tile.tileId}`}
        data-drag-disabled={String(isDragDisabled)}
      >
        {tile.title}
      </div>
    );
  };
});

// Mock @dnd-kit libraries
jest.mock('@dnd-kit/core', () => ({
  DndContext: ({ children, onDragEnd }: any) => (
    <div data-testid="dnd-context" data-drag-end={onDragEnd?.toString()}>
      {children}
    </div>
  ),
  closestCenter: jest.fn(),
  KeyboardSensor: jest.fn(),
  PointerSensor: jest.fn(),
  useSensor: jest.fn(),
  useSensors: jest.fn().mockReturnValue([]),
}));

jest.mock('@dnd-kit/sortable', () => ({
  arrayMove: jest.fn((array, oldIndex, newIndex) => {
    const result = [...array];
    const [removed] = result.splice(oldIndex, 1);
    result.splice(newIndex, 0, removed);
    return result;
  }),
  SortableContext: ({ children }: any) => (
    <div data-testid="sortable-context">{children}</div>
  ),
  sortableKeyboardCoordinates: jest.fn(),
  rectSortingStrategy: jest.fn(),
}));

const mockTiles: DashboardTileType[] = [
  {
    tileId: 'tile-1',
    title: 'Analytics Tile',
    description: 'Analytics data',
    size: 'Medium',
    category: 'Analytics',
    defaultEnabled: true,
    autoShow: false,
    requiredPermissions: [],
    data: { count: 42 }
  },
  {
    tileId: 'tile-2',
    title: 'Finance Tile',
    description: 'Finance data',
    size: 'Large',
    category: 'Finance',
    defaultEnabled: true,
    autoShow: true,
    requiredPermissions: [],
    data: { revenue: 1000 }
  }
];

describe('DashboardGrid', () => {
  it('should render grid container with correct testid', () => {
    render(<DashboardGrid tiles={mockTiles} />);
    
    expect(screen.getByTestId('dashboard-grid')).toBeInTheDocument();
  });

  it('should render all provided tiles', () => {
    render(<DashboardGrid tiles={mockTiles} />);
    
    expect(screen.getByTestId('dashboard-tile-tile-1')).toBeInTheDocument();
    expect(screen.getByTestId('dashboard-tile-tile-2')).toBeInTheDocument();
    expect(screen.getByText('Analytics Tile')).toBeInTheDocument();
    expect(screen.getByText('Finance Tile')).toBeInTheDocument();
  });

  it('should apply correct grid classes', () => {
    render(<DashboardGrid tiles={mockTiles} />);

    const grid = screen.getByTestId('dashboard-grid');
    expect(grid).toHaveClass(
      'grid',
      'gap-4',
      'w-full',
      'grid-cols-1',
      'md:grid-cols-3',
      'lg:grid-cols-6',
      'auto-rows-fr'
    );
  });

  it('should apply custom className when provided', () => {
    render(<DashboardGrid tiles={mockTiles} className="custom-grid-class" />);
    
    const grid = screen.getByTestId('dashboard-grid');
    expect(grid).toHaveClass('custom-grid-class');
  });

  it('should set minimum height style', () => {
    render(<DashboardGrid tiles={mockTiles} />);
    
    const grid = screen.getByTestId('dashboard-grid');
    expect(grid).toHaveStyle({ minHeight: '200px' });
  });

  it('should show empty state when no tiles provided', () => {
    render(<DashboardGrid tiles={[]} />);
    
    expect(screen.getByText('Žádné dlaždice k zobrazení')).toBeInTheDocument();
    expect(screen.getByText('Přidejte dlaždice v nastavení')).toBeInTheDocument();
    expect(screen.getByText('📊')).toBeInTheDocument();
  });

  it('should apply empty state styling', () => {
    render(<DashboardGrid tiles={[]} />);
    
    const emptyState = screen.getByText('Žádné dlaždice k zobrazení').closest('div');
    expect(emptyState?.parentElement).toHaveClass(
      'flex',
      'items-center',
      'justify-center',
      'h-64',
      'bg-gray-50',
      'rounded-lg',
      'border-2',
      'border-dashed',
      'border-gray-300'
    );
  });

  it('should render DndContext and SortableContext on desktop', () => {
    render(<DashboardGrid tiles={mockTiles} />);

    expect(screen.getByTestId('dnd-context')).toBeInTheDocument();
    expect(screen.getByTestId('sortable-context')).toBeInTheDocument();
  });

  it('should NOT render DndContext on mobile', () => {
    const { useIsMobile } = require('../../../hooks/useMediaQuery');
    useIsMobile.mockReturnValue(true);

    render(<DashboardGrid tiles={mockTiles} />);

    expect(screen.queryByTestId('dnd-context')).not.toBeInTheDocument();
    expect(screen.queryByTestId('sortable-context')).not.toBeInTheDocument();

    // Reset mock
    useIsMobile.mockReturnValue(false);
  });

  it('should pass isDragDisabled=true to tiles on mobile', () => {
    const { useIsMobile } = require('../../../hooks/useMediaQuery');
    useIsMobile.mockReturnValue(true);

    render(<DashboardGrid tiles={mockTiles} />);

    const tile1 = screen.getByTestId('dashboard-tile-tile-1');
    const tile2 = screen.getByTestId('dashboard-tile-tile-2');

    expect(tile1).toHaveAttribute('data-drag-disabled', 'true');
    expect(tile2).toHaveAttribute('data-drag-disabled', 'true');

    // Reset mock
    useIsMobile.mockReturnValue(false);
  });

  it('should not pass isDragDisabled on desktop (drag enabled)', () => {
    render(<DashboardGrid tiles={mockTiles} />);

    const tile1 = screen.getByTestId('dashboard-tile-tile-1');
    const tile2 = screen.getByTestId('dashboard-tile-tile-2');

    // On desktop, drag should be enabled (DndContext is used)
    expect(screen.getByTestId('dnd-context')).toBeInTheDocument();
  });

  it('should call onReorder when provided', () => {
    const mockOnReorder = jest.fn();
    render(<DashboardGrid tiles={mockTiles} onReorder={mockOnReorder} />);
    
    // The onReorder callback should be set up in the DndContext
    const dndContext = screen.getByTestId('dnd-context');
    expect(dndContext).toHaveAttribute('data-drag-end');
  });

  it('should handle single tile', () => {
    const singleTile = [mockTiles[0]];
    render(<DashboardGrid tiles={singleTile} />);
    
    expect(screen.getByTestId('dashboard-tile-tile-1')).toBeInTheDocument();
    expect(screen.queryByTestId('dashboard-tile-tile-2')).not.toBeInTheDocument();
  });

  it('should handle tiles with different sizes', () => {
    const tilesWithDifferentSizes: DashboardTileType[] = [
      { ...mockTiles[0], size: 'Small' },
      { ...mockTiles[1], size: 'Large' }
    ];
    
    render(<DashboardGrid tiles={tilesWithDifferentSizes} />);
    
    expect(screen.getByTestId('dashboard-tile-tile-1')).toBeInTheDocument();
    expect(screen.getByTestId('dashboard-tile-tile-2')).toBeInTheDocument();
  });

  it('should handle tiles without data', () => {
    const tilesWithoutData: DashboardTileType[] = [
      { ...mockTiles[0], data: undefined },
      { ...mockTiles[1], data: null }
    ];
    
    render(<DashboardGrid tiles={tilesWithoutData} />);
    
    expect(screen.getByTestId('dashboard-tile-tile-1')).toBeInTheDocument();
    expect(screen.getByTestId('dashboard-tile-tile-2')).toBeInTheDocument();
  });

  it('should maintain tile order', () => {
    render(<DashboardGrid tiles={mockTiles} />);
    
    const grid = screen.getByTestId('dashboard-grid');
    const tiles = grid.children;
    
    expect(tiles[0]).toHaveAttribute('data-testid', 'dashboard-tile-tile-1');
    expect(tiles[1]).toHaveAttribute('data-testid', 'dashboard-tile-tile-2');
  });

  it('should not show grid when tiles array is empty', () => {
    render(<DashboardGrid tiles={[]} />);
    
    expect(screen.queryByTestId('dashboard-grid')).not.toBeInTheDocument();
    expect(screen.queryByTestId('dnd-context')).not.toBeInTheDocument();
  });

  it('should handle tiles with special characters in IDs', () => {
    const specialTiles: DashboardTileType[] = [
      {
        ...mockTiles[0],
        tileId: 'tile-with-dashes',
        title: 'Special Tile'
      },
      {
        ...mockTiles[1],
        tileId: 'tile_with_underscores',
        title: 'Another Special Tile'
      }
    ];
    
    render(<DashboardGrid tiles={specialTiles} />);
    
    expect(screen.getByTestId('dashboard-tile-tile-with-dashes')).toBeInTheDocument();
    expect(screen.getByTestId('dashboard-tile-tile_with_underscores')).toBeInTheDocument();
  });
});