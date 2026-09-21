import React from "react";
import { render, screen, fireEvent, waitFor, within } from "@testing-library/react";
import PricingScenarioBar from "../PricingScenarioBar";
import * as usePricingSimulatorHook from "../../../api/hooks/usePricingSimulator";
import { PricingRowDto, PricingTotalsDto, SwaggerException } from "../../../api/generated/api-client";

jest.mock("../../../api/hooks/usePricingSimulator");

let mockHasPermission: (perm: string) => boolean = () => true;
let mockPermissionsLoading = false;
jest.mock("../../../auth/PermissionsContext", () => ({
  usePermissionsContext: () => ({
    permissions: [],
    isSuperUser: false,
    groups: [],
    isLoading: mockPermissionsLoading,
    hasPermission: (p: string) => mockHasPermission(p),
  }),
}));

const mockUsePricingScenariosQuery =
  usePricingSimulatorHook.usePricingScenariosQuery as jest.MockedFunction<
    typeof usePricingSimulatorHook.usePricingScenariosQuery
  >;
const mockUsePricingScenarioQuery =
  usePricingSimulatorHook.usePricingScenarioQuery as jest.MockedFunction<
    typeof usePricingSimulatorHook.usePricingScenarioQuery
  >;
const mockUseSavePricingScenarioMutation =
  usePricingSimulatorHook.useSavePricingScenarioMutation as jest.MockedFunction<
    typeof usePricingSimulatorHook.useSavePricingScenarioMutation
  >;
const mockUseDeletePricingScenarioMutation =
  usePricingSimulatorHook.useDeletePricingScenarioMutation as jest.MockedFunction<
    typeof usePricingSimulatorHook.useDeletePricingScenarioMutation
  >;

const buildRow = (overrides: Partial<PricingRowDto> = {}): PricingRowDto =>
  ({
    productCode: "PROD001",
    productName: "Test Product 1",
    baselinePrice: 150,
    baselineMaterialCost: 30,
    baselineManufacturingCost: 20,
    baselineQuantity: 100,
    price: 150,
    materialCost: 30,
    manufacturingCost: 20,
    forecastQuantity: 100,
    m0Amount: 120,
    m0Percentage: 80,
    m1Amount: 100,
    m1Percentage: 66.67,
    isEdited: false,
    isExcluded: false,
    baselineDrifted: false,
    ...overrides,
  }) as PricingRowDto;

const buildTotals = (overrides: Partial<PricingTotalsDto> = {}): PricingTotalsDto =>
  ({
    revenueBefore: 15000,
    revenueAfter: 15000,
    revenueDelta: 0,
    revenueDeltaPercentage: 0,
    m0Before: 12000,
    m0After: 12000,
    m0Delta: 0,
    m0DeltaPercentage: 0,
    m1Before: 10000,
    m1After: 10000,
    m1Delta: 0,
    m1DeltaPercentage: 0,
    editedProductCount: 0,
    excludedProductCount: 0,
    ...overrides,
  }) as PricingTotalsDto;

const scenarioSummaries = [
  {
    id: "scenario-1",
    name: "Jarní akce",
    description: undefined,
    createdBy: "ondra@anela.cz",
    createdAt: new Date("2026-01-01"),
    modifiedAt: new Date("2026-01-02"),
    editedProductCount: 2,
  },
  {
    id: "scenario-2",
    name: "Letní slevy",
    description: undefined,
    createdBy: "ondra@anela.cz",
    createdAt: new Date("2026-02-01"),
    modifiedAt: new Date("2026-02-02"),
    editedProductCount: 1,
  },
];

const defaultProps = {
  productCode: undefined,
  productName: undefined,
  productType: undefined,
  overrides: [],
  onScenarioLoaded: jest.fn(),
  onScenarioSaved: jest.fn(),
};

describe("PricingScenarioBar", () => {
  let mockSaveMutateAsync: jest.Mock;
  let mockDeleteMutateAsync: jest.Mock;

  beforeEach(() => {
    jest.clearAllMocks();
    mockHasPermission = () => true;
    mockPermissionsLoading = false;

    mockUsePricingScenariosQuery.mockReturnValue({
      data: { scenarios: scenarioSummaries },
      isLoading: false,
      error: null,
    } as any);

    mockUsePricingScenarioQuery.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: null,
    } as any);

    mockSaveMutateAsync = jest.fn();
    mockUseSavePricingScenarioMutation.mockReturnValue({
      mutateAsync: mockSaveMutateAsync,
      isPending: false,
    } as any);

    mockDeleteMutateAsync = jest.fn();
    mockUseDeletePricingScenarioMutation.mockReturnValue({
      mutateAsync: mockDeleteMutateAsync,
      isPending: false,
    } as any);
  });

  it("lists saved scenarios in the dropdown", () => {
    render(<PricingScenarioBar {...defaultProps} />);

    const select = screen.getByTestId("pricing-scenario-select");
    const options = within(select).getAllByRole("option");
    // Placeholder option + one per saved scenario.
    expect(options).toHaveLength(3);
    expect(within(select).getByText(/Jarní akce/)).toBeInTheDocument();
    expect(within(select).getByText(/Letní slevy/)).toBeInTheDocument();
  });

  it("blocks saving with an empty name -- no request is fired", () => {
    render(<PricingScenarioBar {...defaultProps} />);

    fireEvent.click(screen.getByTestId("pricing-scenario-save"));

    expect(mockSaveMutateAsync).not.toHaveBeenCalled();
  });

  it("blocks saving when the name is only whitespace", () => {
    render(<PricingScenarioBar {...defaultProps} />);

    fireEvent.change(screen.getByTestId("pricing-scenario-name-input"), {
      target: { value: "   " },
    });
    fireEvent.click(screen.getByTestId("pricing-scenario-save"));

    expect(mockSaveMutateAsync).not.toHaveBeenCalled();
  });

  it("saves the entered name and reports the saved name to the parent", async () => {
    mockSaveMutateAsync.mockResolvedValue({ success: true, id: "scenario-3" });

    render(<PricingScenarioBar {...defaultProps} />);

    fireEvent.change(screen.getByTestId("pricing-scenario-name-input"), {
      target: { value: "Podzimní výprodej" },
    });
    fireEvent.click(screen.getByTestId("pricing-scenario-save"));

    await waitFor(() => expect(mockSaveMutateAsync).toHaveBeenCalledTimes(1));
    expect(mockSaveMutateAsync).toHaveBeenCalledWith(
      expect.objectContaining({ name: "Podzimní výprodej" }),
    );
    await waitFor(() =>
      expect(defaultProps.onScenarioSaved).toHaveBeenCalledWith("Podzimní výprodej"),
    );
  });

  it("shows the Czech message when the server rejects a duplicate scenario name", async () => {
    const body = JSON.stringify({
      success: false,
      errorCode: "PricingScenarioNameConflict",
      params: null,
    });
    mockSaveMutateAsync.mockRejectedValue(
      new SwaggerException("Conflict", 409, body, {}, null),
    );

    render(<PricingScenarioBar {...defaultProps} />);

    fireEvent.change(screen.getByTestId("pricing-scenario-name-input"), {
      target: { value: "Jarní akce" },
    });
    fireEvent.click(screen.getByTestId("pricing-scenario-save"));

    expect(
      await screen.findByText("Scénář s tímto názvem už existuje"),
    ).toBeInTheDocument();
  });

  it("loads the selected scenario and fills the name field", async () => {
    const loadedRows = [buildRow({ price: 175, isEdited: true })];
    const loadedTotals = buildTotals({ revenueAfter: 17500 });
    const loadedOverrides = [{ productCode: "PROD001", price: 175 }];

    mockUsePricingScenarioQuery.mockReturnValue({
      data: {
        success: true,
        scenario: scenarioSummaries[0],
        rows: loadedRows,
        totals: loadedTotals,
        overrides: loadedOverrides,
      },
      isLoading: false,
      error: null,
    } as any);

    render(<PricingScenarioBar {...defaultProps} />);

    fireEvent.change(screen.getByTestId("pricing-scenario-select"), {
      target: { value: "scenario-1" },
    });

    await waitFor(() => expect(defaultProps.onScenarioLoaded).toHaveBeenCalledTimes(1));
    expect(defaultProps.onScenarioLoaded).toHaveBeenCalledWith(
      scenarioSummaries[0],
      loadedRows,
      loadedTotals,
      loadedOverrides,
    );

    await waitFor(() =>
      expect(
        (screen.getByTestId("pricing-scenario-name-input") as HTMLInputElement).value,
      ).toBe("Jarní akce"),
    );
  });

  it("never calls window.confirm and shows an inline confirmation before deleting", async () => {
    const confirmSpy = jest.spyOn(window, "confirm");

    mockUsePricingScenarioQuery.mockReturnValue({
      data: {
        success: true,
        scenario: scenarioSummaries[0],
        rows: [buildRow()],
        totals: buildTotals(),
        overrides: [],
      },
      isLoading: false,
      error: null,
    } as any);

    render(<PricingScenarioBar {...defaultProps} />);

    fireEvent.change(screen.getByTestId("pricing-scenario-select"), {
      target: { value: "scenario-1" },
    });
    await waitFor(() => expect(defaultProps.onScenarioLoaded).toHaveBeenCalledTimes(1));

    fireEvent.click(screen.getByTestId("pricing-scenario-delete"));

    const confirmBox = await screen.findByTestId("pricing-scenario-delete-confirm");
    expect(confirmBox).toBeInTheDocument();
    expect(confirmSpy).not.toHaveBeenCalled();
    expect(mockDeleteMutateAsync).not.toHaveBeenCalled();

    mockDeleteMutateAsync.mockResolvedValue(undefined);
    fireEvent.click(within(confirmBox).getByTestId("pricing-scenario-delete-confirm-yes"));

    expect(mockDeleteMutateAsync).toHaveBeenCalledWith("scenario-1");
    // Wait for the full round trip (not just the mutateAsync call): the confirmation
    // box closes once the delete has actually settled and its state updates land.
    await waitFor(() => {
      expect(screen.queryByTestId("pricing-scenario-delete-confirm")).not.toBeInTheDocument();
    });
    expect(confirmSpy).not.toHaveBeenCalled();

    confirmSpy.mockRestore();
  });

  it("cancels the inline delete confirmation without deleting", async () => {
    mockUsePricingScenarioQuery.mockReturnValue({
      data: {
        success: true,
        scenario: scenarioSummaries[0],
        rows: [buildRow()],
        totals: buildTotals(),
        overrides: [],
      },
      isLoading: false,
      error: null,
    } as any);

    render(<PricingScenarioBar {...defaultProps} />);

    fireEvent.change(screen.getByTestId("pricing-scenario-select"), {
      target: { value: "scenario-1" },
    });
    await waitFor(() => expect(defaultProps.onScenarioLoaded).toHaveBeenCalledTimes(1));

    fireEvent.click(screen.getByTestId("pricing-scenario-delete"));
    const confirmBox = await screen.findByTestId("pricing-scenario-delete-confirm");
    fireEvent.click(within(confirmBox).getByTestId("pricing-scenario-delete-confirm-no"));

    expect(screen.queryByTestId("pricing-scenario-delete-confirm")).not.toBeInTheDocument();
    expect(mockDeleteMutateAsync).not.toHaveBeenCalled();
  });

  describe("write-permission gate", () => {
    it("disables the save control and blocks saving when the user lacks finance.price_analysis.write", () => {
      mockHasPermission = () => false;

      render(<PricingScenarioBar {...defaultProps} />);

      fireEvent.change(screen.getByTestId("pricing-scenario-name-input"), {
        target: { value: "Podzimní výprodej" },
      });

      const saveButton = screen.getByTestId("pricing-scenario-save");
      expect(saveButton).toBeDisabled();
      expect(saveButton).toHaveAttribute("title", "Nemáte oprávnění k úpravě scénářů.");

      // A disabled button does not dispatch a click event, so this proves saving
      // genuinely cannot be triggered -- not just that the handler happens to no-op.
      fireEvent.click(saveButton);
      expect(mockSaveMutateAsync).not.toHaveBeenCalled();
    });

    it("enables the save control and allows saving when the user has finance.price_analysis.write", async () => {
      mockHasPermission = (perm: string) => perm === "finance.price_analysis.write";
      mockSaveMutateAsync.mockResolvedValue({ success: true, id: "scenario-3" });

      render(<PricingScenarioBar {...defaultProps} />);

      const saveButton = screen.getByTestId("pricing-scenario-save");
      expect(saveButton).not.toBeDisabled();
      expect(saveButton).not.toHaveAttribute("title");

      fireEvent.change(screen.getByTestId("pricing-scenario-name-input"), {
        target: { value: "Podzimní výprodej" },
      });
      fireEvent.click(saveButton);

      await waitFor(() => expect(mockSaveMutateAsync).toHaveBeenCalledTimes(1));
      expect(mockSaveMutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({ name: "Podzimní výprodej" }),
      );
    });

    it("disables the save control while permissions are still loading, even when hasPermission would allow it", () => {
      mockPermissionsLoading = true;
      mockHasPermission = () => true;

      render(<PricingScenarioBar {...defaultProps} />);

      const saveButton = screen.getByTestId("pricing-scenario-save");
      expect(saveButton).toBeDisabled();
      expect(saveButton).toHaveAttribute("title", "Nemáte oprávnění k úpravě scénářů.");
    });
  });
});
