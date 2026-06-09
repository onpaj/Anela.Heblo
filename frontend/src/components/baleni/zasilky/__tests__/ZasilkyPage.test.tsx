import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import type { PackageDto } from "../../../../api/hooks/usePackages";
import { usePackagesQuery, useDeletePackageMutation } from "../../../../api/hooks/usePackages";
import { printLabelPdf } from "../../../../components/baleni/printLabelPdf";
import { ZasilkyPage } from "../ZasilkyPage";

const mockShowSuccess = jest.fn();
const mockShowError = jest.fn();
const mockMutateAsync = jest.fn();

jest.mock("../../../../contexts/ToastContext", () => ({
  useToast: () => ({ showSuccess: mockShowSuccess, showError: mockShowError }),
}));

jest.mock("../../../../api/hooks/usePackages", () => ({
  usePackagesQuery: jest.fn(),
  useDeletePackageMutation: jest.fn(),
}));

jest.mock("../../../../components/baleni/printLabelPdf", () => ({
  printLabelPdf: jest.fn(),
}));

const mockUsePackagesQuery = usePackagesQuery as jest.Mock;
const mockUseDeletePackageMutation = useDeletePackageMutation as jest.Mock;
const mockPrintLabelPdf = printLabelPdf as jest.Mock;

const samplePackage: PackageDto = {
  id: 1,
  orderCode: "ORD-1",
  customerName: "Alice",
  packageNumber: "1",
  trackingNumber: "TRK-1",
  shippingProviderCode: "6",
  shippingProviderName: "PPL",
  packedAt: "2026-05-25T10:00:00Z",
};

function setupDeleteMock(overrides: Partial<{ mutateAsync: jest.Mock; isPending: boolean }> = {}) {
  mockUseDeletePackageMutation.mockReturnValue({
    mutateAsync: mockMutateAsync,
    isPending: false,
    ...overrides,
  });
}

describe("ZasilkyPage", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    setupDeleteMock();
  });

  it("renders loading state", () => {
    mockUsePackagesQuery.mockReturnValue({ data: undefined, isLoading: true, isError: false });
    render(<ZasilkyPage />);
    expect(screen.getByText(/Načítám/)).toBeInTheDocument();
  });

  it("renders error state", () => {
    mockUsePackagesQuery.mockReturnValue({ data: undefined, isLoading: false, isError: true });
    render(<ZasilkyPage />);
    expect(screen.getByText(/Nepodařilo se načíst zásilky/)).toBeInTheDocument();
  });

  it("renders empty table when data has no items", () => {
    mockUsePackagesQuery.mockReturnValue({
      data: { items: [], totalCount: 0, pageNumber: 1, pageSize: 20 },
      isLoading: false,
      isError: false,
    });
    render(<ZasilkyPage />);
    expect(screen.getByText(/Žádné zásilky/)).toBeInTheDocument();
  });

  it("renders package rows when query succeeds", () => {
    mockUsePackagesQuery.mockReturnValue({
      data: { items: [samplePackage], totalCount: 1, pageNumber: 1, pageSize: 20 },
      isLoading: false,
      isError: false,
    });
    render(<ZasilkyPage />);
    expect(screen.getByText("ORD-1")).toBeInTheDocument();
    expect(screen.getByText("Alice")).toBeInTheDocument();
    expect(screen.getByText("TRK-1")).toBeInTheDocument();
    const table = screen.getByRole("table");
    expect(table).toHaveTextContent("PPL");
  });

  it("calls printLabelPdf when reprint button is clicked", () => {
    mockUsePackagesQuery.mockReturnValue({
      data: { items: [samplePackage], totalCount: 1, pageNumber: 1, pageSize: 20 },
      isLoading: false,
      isError: false,
    });
    render(<ZasilkyPage />);
    fireEvent.click(screen.getByRole("button", { name: /Tisk/ }));
    expect(mockPrintLabelPdf).toHaveBeenCalledWith(
      "ORD-1",
      expect.objectContaining({ packageNumber: 1 }),
      expect.any(Function),
      expect.any(Function),
    );
  });

  it("shows delete confirmation dialog when delete button is clicked", () => {
    mockUsePackagesQuery.mockReturnValue({
      data: { items: [samplePackage], totalCount: 1, pageNumber: 1, pageSize: 20 },
      isLoading: false,
      isError: false,
    });
    render(<ZasilkyPage />);
    fireEvent.click(screen.getByRole("button", { name: /Smazat/ }));
    expect(screen.getByText(/Smazat zásilku\?/)).toBeInTheDocument();
  });

  it("calls mutateAsync with package id on delete confirm", async () => {
    mockMutateAsync.mockResolvedValue({});
    mockUsePackagesQuery.mockReturnValue({
      data: { items: [samplePackage], totalCount: 1, pageNumber: 1, pageSize: 20 },
      isLoading: false,
      isError: false,
    });
    render(<ZasilkyPage />);

    // Open dialog — first "Smazat" button is the row action
    fireEvent.click(screen.getAllByRole("button", { name: /Smazat/ })[0]);
    expect(screen.getByText(/Smazat zásilku\?/)).toBeInTheDocument();

    // Confirm — second "Smazat" button is in the dialog
    fireEvent.click(screen.getAllByRole("button", { name: /Smazat/ })[1]);
    await waitFor(() => expect(mockMutateAsync).toHaveBeenCalledWith(1));
  });

  it("shows success toast after successful delete", async () => {
    mockMutateAsync.mockResolvedValue({});
    mockUsePackagesQuery.mockReturnValue({
      data: { items: [samplePackage], totalCount: 1, pageNumber: 1, pageSize: 20 },
      isLoading: false,
      isError: false,
    });
    render(<ZasilkyPage />);
    fireEvent.click(screen.getAllByRole("button", { name: /Smazat/ })[0]);
    fireEvent.click(screen.getAllByRole("button", { name: /Smazat/ })[1]);
    await waitFor(() => expect(mockShowSuccess).toHaveBeenCalledWith("Smazáno", expect.stringContaining("Zásilka 1")));
  });

  it("shows error toast when delete mutation throws", async () => {
    mockMutateAsync.mockRejectedValue(new Error("Server error"));
    mockUsePackagesQuery.mockReturnValue({
      data: { items: [samplePackage], totalCount: 1, pageNumber: 1, pageSize: 20 },
      isLoading: false,
      isError: false,
    });
    render(<ZasilkyPage />);
    fireEvent.click(screen.getAllByRole("button", { name: /Smazat/ })[0]);
    fireEvent.click(screen.getAllByRole("button", { name: /Smazat/ })[1]);
    await waitFor(() => expect(mockShowError).toHaveBeenCalledWith("Chyba", "Server error"));
  });

  it("closes dialog when cancel is clicked", () => {
    mockUsePackagesQuery.mockReturnValue({
      data: { items: [samplePackage], totalCount: 1, pageNumber: 1, pageSize: 20 },
      isLoading: false,
      isError: false,
    });
    render(<ZasilkyPage />);
    fireEvent.click(screen.getAllByRole("button", { name: /Smazat/ })[0]);
    expect(screen.getByText(/Smazat zásilku\?/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /Zrušit/ }));
    expect(screen.queryByText(/Smazat zásilku\?/)).not.toBeInTheDocument();
  });
});
