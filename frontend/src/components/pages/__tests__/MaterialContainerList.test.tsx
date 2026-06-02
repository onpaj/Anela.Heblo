import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BrowserRouter } from "react-router-dom";
import "@testing-library/jest-dom";
import MaterialContainerList from "../MaterialContainerList";
import * as useMaterialContainersHooks from "../../../api/hooks/useMaterialContainers";

jest.mock("../../../api/hooks/useMaterialContainers");
jest.mock("../../../telemetry/useScreenView", () => ({
  useScreenView: jest.fn(),
}));

const mockHooks = useMaterialContainersHooks as jest.Mocked<
  typeof useMaterialContainersHooks
>;

const createWrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>{children}</BrowserRouter>
    </QueryClientProvider>
  );
};

const sampleResponse = {
  success: true,
  containers: [
    {
      id: 1,
      code: "M00001234",
      materialCode: "MAT001",
      lotCode: "LOT-2026-04",
      amount: 25,
      unit: "kg",
      createdAt: new Date("2026-04-01T10:00:00Z"),
      createdBy: "user@anela.cz",
      status: "Unassigned",
    },
  ],
  totalCount: 1,
  pageNumber: 1,
  pageSize: 20,
} as any;

describe("MaterialContainerList", () => {
  const mockUseList = jest.fn();
  const mockRefetch = jest.fn();
  const mockPrintMutate = jest.fn();
  const mockUsePrint = jest.fn();

  beforeEach(() => {
    jest.clearAllMocks();
    (mockHooks.useMaterialContainersList as jest.Mock) = mockUseList;
    (mockHooks.usePrintMaterialContainerLabels as jest.Mock) = mockUsePrint;
    mockUseList.mockReturnValue({
      data: sampleResponse,
      isLoading: false,
      error: null,
      refetch: mockRefetch,
    });
    mockUsePrint.mockReturnValue({
      mutate: mockPrintMutate,
      isPending: false,
    });
  });

  it("renders a row for each container", () => {
    render(<MaterialContainerList />, { wrapper: createWrapper });
    expect(screen.getByText("M00001234")).toBeInTheDocument();
    expect(screen.getByText("MAT001")).toBeInTheDocument();
    expect(screen.getByText("LOT-2026-04")).toBeInTheDocument();
    expect(screen.getByText("user@anela.cz")).toBeInTheDocument();
  });

  it("shows a loading indicator while fetching", () => {
    mockUseList.mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null,
      refetch: mockRefetch,
    });
    render(<MaterialContainerList />, { wrapper: createWrapper });
    expect(screen.getByText(/Načítání/i)).toBeInTheDocument();
  });

  it("shows an error message when the query fails", () => {
    mockUseList.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: new Error("boom"),
      refetch: mockRefetch,
    });
    render(<MaterialContainerList />, { wrapper: createWrapper });
    expect(screen.getByText(/Chyba/i)).toBeInTheDocument();
  });

  it("shows an empty state when there are no containers", () => {
    mockUseList.mockReturnValue({
      data: { success: true, containers: [], totalCount: 0, pageNumber: 1, pageSize: 20 },
      isLoading: false,
      error: null,
      refetch: mockRefetch,
    });
    render(<MaterialContainerList />, { wrapper: createWrapper });
    expect(screen.getByText(/Žádné kontejnery/i)).toBeInTheDocument();
  });

  it("renders the print-labels button", () => {
    render(<MaterialContainerList />, { wrapper: createWrapper });
    expect(screen.getByText("Tisk štítků")).toBeInTheDocument();
  });

  it("prints labels with the chosen quantity", () => {
    render(<MaterialContainerList />, { wrapper: createWrapper });

    fireEvent.click(screen.getByText("Tisk štítků"));

    const qtyInput = screen.getByLabelText("Počet štítků");
    fireEvent.change(qtyInput, { target: { value: "5" } });

    fireEvent.click(screen.getByText("Vytisknout 5"));

    expect(mockPrintMutate).toHaveBeenCalledWith(
      { count: 5 },
      expect.objectContaining({ onSuccess: expect.any(Function) }),
    );
  });

  it("renders a Stav column with the container status", () => {
    render(<MaterialContainerList />, { wrapper: createWrapper });
    expect(screen.getByText("Stav")).toBeInTheDocument();
    expect(screen.getByText("Unassigned")).toBeInTheDocument();
  });

  it("applies the material filter and resets to page 1", async () => {
    render(<MaterialContainerList />, { wrapper: createWrapper });

    fireEvent.change(screen.getByPlaceholderText("Materiál"), {
      target: { value: "MAT001" },
    });
    fireEvent.click(screen.getByText("Filtrovat"));

    await waitFor(() =>
      expect(mockUseList).toHaveBeenCalledWith(
        expect.objectContaining({ materialCode: "MAT001", page: 1 }),
      ),
    );
  });
});
