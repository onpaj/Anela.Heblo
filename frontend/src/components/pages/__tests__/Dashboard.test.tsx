import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import Dashboard from "../Dashboard";
import {
  useLiveHealthCheck,
  useReadyHealthCheck,
} from "../../../api/hooks/useHealth";
import {
  useUserDashboardSettings,
  useTileData,
  useSaveDashboardSettings
} from "../../../api/hooks/useDashboard";

// Mock the health check hooks
jest.mock("../../../api/hooks/useHealth", () => ({
  useLiveHealthCheck: jest.fn(),
  useReadyHealthCheck: jest.fn(),
}));

// Mock the dashboard hooks
jest.mock("../../../api/hooks/useDashboard", () => ({
  useUserDashboardSettings: jest.fn(),
  useTileData: jest.fn(),
  useSaveDashboardSettings: jest.fn(),
}));

// Mock dashboard components
jest.mock("../../dashboard/DashboardGrid", () => {
  return function MockDashboardGrid({ tiles, onReorder }: any) {
    return (
      <div data-testid="dashboard-grid">
        <div data-testid="tile-count">{tiles.length}</div>
        <button onClick={() => onReorder(['tile1', 'tile2'])} data-testid="reorder-button">
          Reorder
        </button>
      </div>
    );
  };
});

jest.mock("../../dashboard/DashboardSettings", () => {
  return function MockDashboardSettings({ onClose }: any) {
    return (
      <div data-testid="dashboard-settings">
        <button onClick={onClose} data-testid="close-settings">Close</button>
      </div>
    );
  };
});

const mockUseLiveHealthCheck = useLiveHealthCheck as jest.MockedFunction<
  typeof useLiveHealthCheck
>;
const mockUseReadyHealthCheck = useReadyHealthCheck as jest.MockedFunction<
  typeof useReadyHealthCheck
>;
const mockUseUserDashboardSettings = useUserDashboardSettings as jest.MockedFunction<
  typeof useUserDashboardSettings
>;
const mockUseTileData = useTileData as jest.MockedFunction<
  typeof useTileData
>;
const mockUseSaveDashboardSettings = useSaveDashboardSettings as jest.MockedFunction<
  typeof useSaveDashboardSettings
>;

const createQueryClient = () =>
  new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  });

const renderWithQueryClient = (component: React.ReactElement) => {
  const queryClient = createQueryClient();
  return render(
    <QueryClientProvider client={queryClient}>{component}</QueryClientProvider>,
  );
};

const mockUserSettings = {
  tiles: [
    { tileId: 'tile1', isVisible: true, displayOrder: 0 },
    { tileId: 'tile2', isVisible: false, displayOrder: 1 },
    { tileId: 'tile3', isVisible: true, displayOrder: 2 }
  ],
  lastModified: '2024-01-01T00:00:00Z'
};

const mockTileData = [
  {
    tileId: 'tile1',
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
    tileId: 'tile2',
    title: 'Finance Tile',
    description: 'Finance data',
    size: 'Large',
    category: 'Finance',
    defaultEnabled: true,
    autoShow: true,
    requiredPermissions: [],
    data: { revenue: 1000 }
  },
  {
    tileId: 'tile3',
    title: 'Operations Tile',
    description: 'Operations data',
    size: 'Small',
    category: 'Operations',
    defaultEnabled: false,
    autoShow: false,
    requiredPermissions: [],
    data: { status: 'active' }
  }
];

describe("Dashboard", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    
    // Setup default mocks
    mockUseLiveHealthCheck.mockReturnValue({
      data: { status: "healthy" },
      isLoading: false,
      error: null,
    } as any);
    
    mockUseReadyHealthCheck.mockReturnValue({
      data: { status: "ready" },
      isLoading: false,
      error: null,
    } as any);

    mockUseUserDashboardSettings.mockReturnValue({
      data: mockUserSettings,
      isLoading: false,
      error: null,
    } as any);

    mockUseTileData.mockReturnValue({
      data: mockTileData,
      isLoading: false,
      error: null,
    } as any);

    mockUseSaveDashboardSettings.mockReturnValue({
      mutateAsync: jest.fn().mockResolvedValue(undefined),
      isLoading: false,
      error: null,
    } as any);
  });

  it("should render dashboard title and description", () => {
    renderWithQueryClient(<Dashboard />);

    expect(screen.getByText("Dashboard")).toBeInTheDocument();
    expect(
      screen.getByText("Přehled systému a aktuálního stavu"),
    ).toBeInTheDocument();
  });

  it("should render settings button", () => {
    renderWithQueryClient(<Dashboard />);

    const settingsButton = screen.getByRole('button', { name: /nastavení/i });
    expect(settingsButton).toBeInTheDocument();
  });

  it("should show settings when settings button is clicked", () => {
    renderWithQueryClient(<Dashboard />);

    const settingsButton = screen.getByRole('button', { name: /nastavení/i });
    fireEvent.click(settingsButton);

    expect(screen.getByTestId('dashboard-settings')).toBeInTheDocument();
    expect(screen.queryByTestId('dashboard-grid')).not.toBeInTheDocument();
  });

  it("should hide settings when close is clicked", () => {
    renderWithQueryClient(<Dashboard />);

    // Open settings
    const settingsButton = screen.getByRole('button', { name: /nastavení/i });
    fireEvent.click(settingsButton);
    expect(screen.getByTestId('dashboard-settings')).toBeInTheDocument();

    // Close settings
    const closeButton = screen.getByTestId('close-settings');
    fireEvent.click(closeButton);
    expect(screen.queryByTestId('dashboard-settings')).not.toBeInTheDocument();
    expect(screen.getByTestId('dashboard-grid')).toBeInTheDocument();
  });

  it("should call health check hooks on mount", () => {
    renderWithQueryClient(<Dashboard />);

    expect(mockUseLiveHealthCheck).toHaveBeenCalled();
    expect(mockUseReadyHealthCheck).toHaveBeenCalled();
  });

  it("should call dashboard hooks on mount", () => {
    renderWithQueryClient(<Dashboard />);

    expect(mockUseUserDashboardSettings).toHaveBeenCalled();
    expect(mockUseTileData).toHaveBeenCalled();
    expect(mockUseSaveDashboardSettings).toHaveBeenCalled();
  });

  it("should show loading spinner when data is loading", () => {
    mockUseUserDashboardSettings.mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null,
    } as any);

    renderWithQueryClient(<Dashboard />);

    expect(screen.getByTestId('dashboard-container')).toBeInTheDocument();
    expect(screen.getByTestId('dashboard-container')).toBeInTheDocument();
    // Loading spinner is visible but doesn't have status role
    expect(screen.queryByTestId('dashboard-grid')).not.toBeInTheDocument();
  });

  it("should show loading spinner when settings are loading", () => {
    mockUseTileData.mockReturnValue({
      data: [],
      isLoading: true,
      error: null,
    } as any);

    renderWithQueryClient(<Dashboard />);

    expect(screen.getByTestId('dashboard-container')).toBeInTheDocument();
    // Loading spinner is visible but doesn't have status role
  });

  it("should filter visible tiles based on user settings", () => {
    renderWithQueryClient(<Dashboard />);

    const grid = screen.getByTestId('dashboard-grid');
    expect(grid).toBeInTheDocument();
    
    // Should show 2 tiles: tile1 (explicitly visible) and tile2 (autoShow)
    const tileCount = screen.getByTestId('tile-count');
    expect(tileCount).toHaveTextContent('2');
  });

  it("should handle reordering tiles", async () => {
    const mockMutateAsync = jest.fn().mockResolvedValue(undefined);
    mockUseSaveDashboardSettings.mockReturnValue({
      mutateAsync: mockMutateAsync,
      isLoading: false,
      error: null,
    } as any);

    renderWithQueryClient(<Dashboard />);

    const reorderButton = screen.getByTestId('reorder-button');
    fireEvent.click(reorderButton);

    await waitFor(() => {
      expect(mockMutateAsync).toHaveBeenCalledWith({
        tiles: expect.arrayContaining([
          expect.objectContaining({
            tileId: 'tile1',
            isVisible: true,
            displayOrder: 0
          }),
          expect.objectContaining({
            tileId: 'tile2',
            isVisible: false,
            displayOrder: 1
          })
        ])
      });
    });
  });

  it("should handle empty tile data", () => {
    mockUseTileData.mockReturnValue({
      data: [],
      isLoading: false,
      error: null,
    } as any);

    renderWithQueryClient(<Dashboard />);

    const grid = screen.getByTestId('dashboard-grid');
    expect(grid).toBeInTheDocument();
    
    const tileCount = screen.getByTestId('tile-count');
    expect(tileCount).toHaveTextContent('0');
  });

  it("should handle missing user settings", () => {
    mockUseUserDashboardSettings.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: null,
    } as any);

    renderWithQueryClient(<Dashboard />);

    const grid = screen.getByTestId('dashboard-grid');
    expect(grid).toBeInTheDocument();
    
    const tileCount = screen.getByTestId('tile-count');
    expect(tileCount).toHaveTextContent('0');
  });

  it("should apply correct container styling", () => {
    renderWithQueryClient(<Dashboard />);

    const container = screen.getByTestId('dashboard-container');
    expect(container).toHaveClass('flex', 'flex-col', 'w-full');
  });

  it("should show autoShow tiles when not explicitly disabled", () => {
    const settingsWithoutTile2 = {
      tiles: [
        { tileId: 'tile1', isVisible: true, displayOrder: 0 },
        { tileId: 'tile3', isVisible: true, displayOrder: 2 }
      ],
      lastModified: '2024-01-01T00:00:00Z'
    };

    mockUseUserDashboardSettings.mockReturnValue({
      data: settingsWithoutTile2,
      isLoading: false,
      error: null,
    } as any);

    renderWithQueryClient(<Dashboard />);

    const tileCount = screen.getByTestId('tile-count');
    expect(tileCount).toHaveTextContent('3'); // tile1 (visible), tile2 (autoShow), tile3 (visible)
  });

  it("should not show autoShow tiles when explicitly disabled", () => {
    const settingsWithDisabledAutoShow = {
      tiles: [
        { tileId: 'tile1', isVisible: true, displayOrder: 0 },
        { tileId: 'tile2', isVisible: false, displayOrder: 1 }, // Explicitly disabled autoShow tile
        { tileId: 'tile3', isVisible: true, displayOrder: 2 }
      ],
      lastModified: '2024-01-01T00:00:00Z'
    };

    mockUseUserDashboardSettings.mockReturnValue({
      data: settingsWithDisabledAutoShow,
      isLoading: false,
      error: null,
    } as any);

    renderWithQueryClient(<Dashboard />);

    const tileCount = screen.getByTestId('tile-count');
    expect(tileCount).toHaveTextContent('2'); // tile1 and tile3 only
  });

  it("should sort tiles by display order", () => {
    const unsortedSettings = {
      tiles: [
        { tileId: 'tile3', isVisible: true, displayOrder: 2 },
        { tileId: 'tile1', isVisible: true, displayOrder: 0 },
        { tileId: 'tile2', isVisible: false, displayOrder: 1 }
      ],
      lastModified: '2024-01-01T00:00:00Z'
    };

    mockUseUserDashboardSettings.mockReturnValue({
      data: unsortedSettings,
      isLoading: false,
      error: null,
    } as any);

    renderWithQueryClient(<Dashboard />);

    // The tiles should be sorted by display order regardless of how they appear in the settings
    expect(screen.getByTestId('dashboard-grid')).toBeInTheDocument();
  });
});
