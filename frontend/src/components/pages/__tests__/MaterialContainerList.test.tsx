import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BrowserRouter } from "react-router-dom";
import "@testing-library/jest-dom";
import MaterialContainerList from "../MaterialContainerList";
import { SwaggerException } from "../../../api/generated/api-client";
import * as useMaterialContainersHooks from "../../../api/hooks/useMaterialContainers";

jest.mock("../../../api/hooks/useMaterialContainers");
jest.mock("../../../telemetry/useScreenView", () => ({
  useScreenView: jest.fn(),
}));
// The lot-label modal has its own hook/permission dependencies covered by its own test.
jest.mock("../LotLabelPrintModal", () => () => null);

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
  const mockReprintMutate = jest.fn();
  const mockUseReprint = jest.fn();

  beforeEach(() => {
    jest.clearAllMocks();
    (mockHooks.useMaterialContainersList as jest.Mock) = mockUseList;
    (mockHooks.usePrintMaterialContainerLabels as jest.Mock) = mockUsePrint;
    (mockHooks.useReprintMaterialContainerLabel as jest.Mock) = mockUseReprint;
    mockUseReprint.mockReturnValue({
      mutate: mockReprintMutate,
      isPending: false,
    });
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
      { count: 5, mediaChangeConfirmed: false },
      expect.objectContaining({ onSuccess: expect.any(Function) }),
    );
  });

  it("shows the media-change dialog when a print is blocked, then reprints confirmed", () => {
    // The backend blocks the first (unconfirmed) print, then allows the confirmed retry.
    mockPrintMutate.mockImplementation((input, opts) =>
      opts?.onSuccess?.({ requiresMediaChangeConfirmation: !input.mediaChangeConfirmed }),
    );

    render(<MaterialContainerList />, { wrapper: createWrapper });

    fireEvent.click(screen.getByText("Tisk štítků"));
    fireEvent.click(screen.getByText("Vytisknout 10"));

    expect(screen.getByTestId("printer-media-change-dialog")).toBeInTheDocument();
    expect(mockPrintMutate).toHaveBeenCalledTimes(1);
    expect(mockPrintMutate.mock.calls[0][0].mediaChangeConfirmed).toBe(false);

    fireEvent.click(screen.getByText("Pokračovat v tisku"));

    expect(mockPrintMutate).toHaveBeenCalledTimes(2);
    expect(mockPrintMutate.mock.calls[1][0].mediaChangeConfirmed).toBe(true);
    expect(
      screen.queryByTestId("printer-media-change-dialog"),
    ).not.toBeInTheDocument();
  });

  it("shows reprint button per row and calls mutate with that code", () => {
    render(<MaterialContainerList />, { wrapper: createWrapper });
    fireEvent.click(screen.getByLabelText("Dotisknout štítek M00001234"));
    expect(mockReprintMutate).toHaveBeenCalledWith(
      { code: "M00001234", mediaChangeConfirmed: false },
      expect.objectContaining({
        onSuccess: expect.any(Function),
        onError: expect.any(Function),
      }),
    );
  });

  it("disables every row's reprint button while a reprint is pending", () => {
    // One useMutation instance only fires the latest mutate call's callbacks, so a second
    // reprint must not be startable while the first is in flight.
    mockUseReprint.mockReturnValue({ mutate: mockReprintMutate, isPending: true });
    mockUseList.mockReturnValue({
      data: {
        ...sampleResponse,
        containers: [
          sampleResponse.containers[0],
          { ...sampleResponse.containers[0], id: 2, code: "M00005678" },
        ],
      },
      isLoading: false,
      error: null,
      refetch: mockRefetch,
    });
    render(<MaterialContainerList />, { wrapper: createWrapper });

    const first = screen.getByLabelText("Dotisknout štítek M00001234");
    const second = screen.getByLabelText("Dotisknout štítek M00005678");
    expect(first).toBeDisabled();
    expect(second).toBeDisabled();
    fireEvent.click(second);
    expect(mockReprintMutate).not.toHaveBeenCalled();
  });

  it("clears a stale reprint error when the bulk print starts", () => {
    mockReprintMutate.mockImplementation((_input, opts) =>
      opts?.onError?.(
        new SwaggerException(
          "err",
          409,
          JSON.stringify({ errorCode: "MaterialContainerDiscarded" }),
          {},
          null,
        ),
      ),
    );
    render(<MaterialContainerList />, { wrapper: createWrapper });
    fireEvent.click(screen.getByLabelText("Dotisknout štítek M00001234"));
    expect(
      screen.getByText("Kontejner je vyřazený – štítek nelze dotisknout."),
    ).toBeInTheDocument();

    fireEvent.click(screen.getByText("Tisk štítků"));
    fireEvent.click(screen.getByText("Vytisknout 10"));

    expect(
      screen.queryByText("Kontejner je vyřazený – štítek nelze dotisknout."),
    ).not.toBeInTheDocument();
  });

  it("hides reprint button for Discarded rows", () => {
    mockUseList.mockReturnValue({
      data: {
        ...sampleResponse,
        containers: [{ ...sampleResponse.containers[0], status: "Discarded" }],
      },
      isLoading: false,
      error: null,
      refetch: mockRefetch,
    });
    render(<MaterialContainerList />, { wrapper: createWrapper });
    expect(
      screen.queryByLabelText("Dotisknout štítek M00001234"),
    ).not.toBeInTheDocument();
  });

  it("opens media-change dialog when response requires confirmation and resends confirmed", () => {
    mockReprintMutate.mockImplementation((input, opts) =>
      opts?.onSuccess?.({ requiresMediaChangeConfirmation: !input.mediaChangeConfirmed }),
    );
    render(<MaterialContainerList />, { wrapper: createWrapper });

    fireEvent.click(screen.getByLabelText("Dotisknout štítek M00001234"));
    expect(screen.getByTestId("printer-media-change-dialog")).toBeInTheDocument();

    fireEvent.click(screen.getByText("Pokračovat v tisku"));
    expect(mockReprintMutate).toHaveBeenCalledTimes(2);
    expect(mockReprintMutate.mock.calls[1][0]).toEqual({
      code: "M00001234",
      mediaChangeConfirmed: true,
    });
    expect(
      screen.queryByTestId("printer-media-change-dialog"),
    ).not.toBeInTheDocument();
  });

  it("shows error text when reprint fails", () => {
    mockReprintMutate.mockImplementation((_input, opts) =>
      opts?.onError?.(
        new SwaggerException(
          "err",
          409,
          JSON.stringify({ errorCode: "MaterialContainerDiscarded" }),
          {},
          null,
        ),
      ),
    );
    render(<MaterialContainerList />, { wrapper: createWrapper });
    fireEvent.click(screen.getByLabelText("Dotisknout štítek M00001234"));
    expect(
      screen.getByText("Kontejner je vyřazený – štítek nelze dotisknout."),
    ).toBeInTheDocument();
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
