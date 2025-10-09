import React from "react";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import Dashboard from "../Dashboard";
import {
  useLiveHealthCheck,
  useReadyHealthCheck,
} from "../../../api/hooks/useHealth";

// Mock the health check hooks
jest.mock("../../../api/hooks/useHealth", () => ({
  useLiveHealthCheck: jest.fn(),
  useReadyHealthCheck: jest.fn(),
}));

const mockUseLiveHealthCheck = useLiveHealthCheck as jest.MockedFunction<
  typeof useLiveHealthCheck
>;
const mockUseReadyHealthCheck = useReadyHealthCheck as jest.MockedFunction<
  typeof useReadyHealthCheck
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

describe("Dashboard", () => {
  beforeEach(() => {
    jest.clearAllMocks();
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
  });

  it("should render dashboard title and description", () => {
    renderWithQueryClient(<Dashboard />);

    expect(screen.getByText("Dashboard")).toBeInTheDocument();
    expect(
      screen.getByText("Přehled systému a aktuálního stavu"),
    ).toBeInTheDocument();
  });


  it("should call health check hooks on mount", () => {
    renderWithQueryClient(<Dashboard />);

    expect(mockUseLiveHealthCheck).toHaveBeenCalled();
    expect(mockUseReadyHealthCheck).toHaveBeenCalled();
  });
});
