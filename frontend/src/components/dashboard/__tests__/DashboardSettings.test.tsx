import React from "react";
import { render } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import DashboardSettings from "../DashboardSettings";
import {
  useAvailableTiles,
  useUserDashboardSettings,
  useEnableTile,
  useDisableTile,
} from "../../../api/hooks/useDashboard";

jest.mock("../../../api/hooks/useDashboard", () => ({
  useAvailableTiles: jest.fn(),
  useUserDashboardSettings: jest.fn(),
  useEnableTile: jest.fn(),
  useDisableTile: jest.fn(),
}));

const mockUseAvailableTiles = useAvailableTiles as jest.MockedFunction<typeof useAvailableTiles>;
const mockUseUserDashboardSettings = useUserDashboardSettings as jest.MockedFunction<typeof useUserDashboardSettings>;
const mockUseEnableTile = useEnableTile as jest.MockedFunction<typeof useEnableTile>;
const mockUseDisableTile = useDisableTile as jest.MockedFunction<typeof useDisableTile>;

const renderWithQueryClient = (component: React.ReactElement) => {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>{component}</QueryClientProvider>,
  );
};

describe("DashboardSettings — contract drift", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockUseEnableTile.mockReturnValue({
      mutateAsync: jest.fn(),
      isPending: false,
    } as any);
    mockUseDisableTile.mockReturnValue({
      mutateAsync: jest.fn(),
      isPending: false,
    } as any);
  });

  it("does not throw when useAvailableTiles returns null (contract drift)", () => {
    mockUseAvailableTiles.mockReturnValue({
      data: null,
      isLoading: false,
      error: null,
    } as any);
    mockUseUserDashboardSettings.mockReturnValue({
      data: { tiles: [], lastModified: "2024-01-01T00:00:00Z" },
      isLoading: false,
      error: null,
    } as any);

    expect(() => renderWithQueryClient(<DashboardSettings />)).not.toThrow();
  });

  it("does not throw when userSettings.tiles is null (contract drift)", () => {
    mockUseAvailableTiles.mockReturnValue({
      data: [],
      isLoading: false,
      error: null,
    } as any);
    mockUseUserDashboardSettings.mockReturnValue({
      data: { tiles: null, lastModified: "2024-01-01T00:00:00Z" },
      isLoading: false,
      error: null,
    } as any);

    expect(() => renderWithQueryClient(<DashboardSettings />)).not.toThrow();
  });

  it("does not throw when useAvailableTiles returns a non-array object (contract drift)", () => {
    mockUseAvailableTiles.mockReturnValue({
      data: { unexpected: "shape" } as any,
      isLoading: false,
      error: null,
    } as any);
    mockUseUserDashboardSettings.mockReturnValue({
      data: { tiles: [], lastModified: "2024-01-01T00:00:00Z" },
      isLoading: false,
      error: null,
    } as any);

    expect(() => renderWithQueryClient(<DashboardSettings />)).not.toThrow();
  });
});
