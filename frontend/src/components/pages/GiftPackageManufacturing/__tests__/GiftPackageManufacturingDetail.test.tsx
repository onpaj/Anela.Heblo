import React from "react";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { toast } from "react-hot-toast";
import GiftPackageManufacturingDetail from "../GiftPackageManufacturingDetail";
import {
  useDisassembleGiftPackage,
  useGiftPackageDetail,
} from "../../../../api/hooks/useGiftPackageManufacturing";

// The manufacture failure path is the user-visible half of the warehouse-stock validation:
// the backend refuses the run with a BaseResponse envelope and the modal must surface
// Params["ErrorMessage"] as a toast. These tests pin the envelope contract and the fallback.

jest.mock("react-hot-toast", () => ({
  __esModule: true,
  toast: { success: jest.fn(), error: jest.fn() },
}));

jest.mock("react-router-dom", () => ({
  ...jest.requireActual("react-router-dom"),
  useNavigate: () => jest.fn(),
}));

jest.mock("../../../../api/hooks/useGiftPackageManufacturing", () => ({
  useGiftPackageDetail: jest.fn(),
  useDisassembleGiftPackage: jest.fn(),
}));

jest.mock("../DisassemblyTabContent", () => {
  return function MockDisassemblyTabContent() {
    return <div data-testid="disassembly-tab-content" />;
  };
});

const SELECTED_PACKAGE = {
  code: "SET001",
  name: "Dárkový balíček",
  availableStock: 0,
  dailySales: 0,
  suggestedQuantity: 1,
  severity: "Optimal" as const,
  overstockOptimal: 0,
  overstockMinimal: 0,
  stockCoveragePercent: 0,
};

// Ingredient stock comfortably covers the run so the manufacture button is enabled.
const DETAIL_WITH_STOCK = {
  giftPackage: {
    code: "SET001",
    ingredients: [
      { productCode: "ING001", productName: "Krém", requiredQuantity: 1, availableStock: 10 },
    ],
  },
};

const MANUFACTURE_BUTTON = /Zadat k výrobě/;

const renderDetail = (onManufacture: (quantity: number) => Promise<void>) =>
  render(
    <GiftPackageManufacturingDetail
      selectedPackage={SELECTED_PACKAGE}
      isOpen={true}
      onClose={jest.fn()}
      onManufacture={onManufacture}
    />
  );

const swaggerLikeError = (body: unknown) => ({
  message: "An unexpected server error occurred.",
  status: 400,
  response: JSON.stringify(body),
});

describe("GiftPackageManufacturingDetail - manufacture failure toast", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    jest.spyOn(console, "error").mockImplementation(() => undefined);
    (useGiftPackageDetail as jest.Mock).mockReturnValue({
      data: DETAIL_WITH_STOCK,
      isLoading: false,
    });
    (useDisassembleGiftPackage as jest.Mock).mockReturnValue({
      mutateAsync: jest.fn(),
      isPending: false,
    });
  });

  afterEach(() => {
    (console.error as jest.Mock).mockRestore();
  });

  it("shows the backend ErrorMessage from the envelope when the run is refused", async () => {
    // Arrange - the generated client throws a SwaggerException whose `response` is the raw
    // BaseResponse JSON; the message lives under the exact key `ErrorMessage`.
    const onManufacture = jest.fn().mockRejectedValue(
      swaggerLikeError({
        success: false,
        errorCode: "InvalidOperation",
        params: { ErrorMessage: "Nedostatek zásob: ING001 (potřeba 5, skladem 3)" },
      })
    );
    renderDetail(onManufacture);

    // Act
    fireEvent.click(screen.getByRole("button", { name: MANUFACTURE_BUTTON }));

    // Assert
    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith("Nedostatek zásob: ING001 (potřeba 5, skladem 3)")
    );
  });

  it("falls back to the Czech generic message when the failure carries no envelope", async () => {
    // Arrange - a bare Error must not leak its English message into the toast.
    const onManufacture = jest.fn().mockRejectedValue(new Error("Network Error"));
    renderDetail(onManufacture);

    // Act
    fireEvent.click(screen.getByRole("button", { name: MANUFACTURE_BUTTON }));

    // Assert
    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith("Nepodařilo se zadat balíček k výrobě")
    );
    expect(toast.error).not.toHaveBeenCalledWith("Network Error");
  });

  it("disables the button while a run is in flight so a double click submits once", async () => {
    // Arrange - the backend stock check is best-effort, not a lock, so two overlapping
    // submits can both pass it. Hold the promise open to observe the in-flight state.
    let resolveRun: () => void = () => undefined;
    const onManufacture = jest.fn(
      () => new Promise<void>((resolve) => { resolveRun = resolve; })
    );
    renderDetail(onManufacture);
    const button = screen.getByRole("button", { name: MANUFACTURE_BUTTON });

    // Act
    fireEvent.click(button);
    fireEvent.click(button);

    // Assert
    await waitFor(() => expect(button).toBeDisabled());
    expect(onManufacture).toHaveBeenCalledTimes(1);

    resolveRun();
    await waitFor(() => expect(button).not.toBeDisabled());
  });

  it("does not toast and closes the modal when the run succeeds", async () => {
    // Arrange
    const onManufacture = jest.fn().mockResolvedValue(undefined);
    const onClose = jest.fn();
    render(
      <GiftPackageManufacturingDetail
        selectedPackage={SELECTED_PACKAGE}
        isOpen={true}
        onClose={onClose}
        onManufacture={onManufacture}
      />
    );

    // Act
    fireEvent.click(screen.getByRole("button", { name: MANUFACTURE_BUTTON }));

    // Assert
    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(toast.error).not.toHaveBeenCalled();
  });
});
