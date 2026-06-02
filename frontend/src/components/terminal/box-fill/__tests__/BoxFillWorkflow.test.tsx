import React from "react";
import { render, screen, fireEvent, act, waitFor } from "@testing-library/react";
import BoxFillWorkflow from "../BoxFillWorkflow";
import { ScanProvider } from "../../shell/ScanProvider";
import { FlashOverlay } from "../../shell/FlashOverlay";
import * as inventoryHook from "../../../../api/hooks/useManufacturedProductInventory";
import * as useBoxFill from "../../../../api/hooks/useBoxFill";

jest.mock("../../../../utils/errorHandler", () => ({ getErrorMessage: () => "Chyba" }));

const inventoryItem = {
  id: 7, productCode: "P-1", productName: "Krém", amount: 10,
  lotNumber: "L1", expirationDate: "2027-01-01", createdAt: "", createdBy: "", log: [],
};

const emptyBox: useBoxFill.TerminalBox = { id: 1, code: "B001", state: "Opened", itemCount: 0, items: [] };
const filledBox: useBoxFill.TerminalBox = {
  ...emptyBox, itemCount: 1,
  items: [{ id: 5, productCode: "P-1", productName: "Krém", amount: 2 }],
};

const openMutateAsync = jest.fn();
const transitMutateAsync = jest.fn();
const addMutateAsync = jest.fn();
const removeMutateAsync = jest.fn();

beforeEach(() => {
  jest.useFakeTimers();
  openMutateAsync.mockReset();
  transitMutateAsync.mockReset();
  addMutateAsync.mockReset();
  removeMutateAsync.mockReset();

  jest.spyOn(inventoryHook, "useManufacturedProductInventoryQuery").mockReturnValue({
    data: { items: [inventoryItem], totalCount: 1 }, isLoading: false, error: null,
  } as unknown as ReturnType<typeof inventoryHook.useManufacturedProductInventoryQuery>);
  jest.spyOn(useBoxFill, "useOpenOrResumeBox").mockReturnValue({
    mutateAsync: openMutateAsync, isPending: false,
  } as unknown as ReturnType<typeof useBoxFill.useOpenOrResumeBox>);
  jest.spyOn(useBoxFill, "useSendBoxToTransit").mockReturnValue({
    mutateAsync: transitMutateAsync, isPending: false,
  } as unknown as ReturnType<typeof useBoxFill.useSendBoxToTransit>);
  jest.spyOn(useBoxFill, "useAddBoxItem").mockReturnValue({
    mutateAsync: addMutateAsync, isPending: false,
  } as unknown as ReturnType<typeof useBoxFill.useAddBoxItem>);
  jest.spyOn(useBoxFill, "useRemoveBoxItem").mockReturnValue({
    mutateAsync: removeMutateAsync, isPending: false,
  } as unknown as ReturnType<typeof useBoxFill.useRemoveBoxItem>);
});

afterEach(() => {
  jest.runOnlyPendingTimers();
  jest.useRealTimers();
  jest.restoreAllMocks();
});

const renderScreen = () =>
  render(
    <ScanProvider>
      <BoxFillWorkflow />
      <FlashOverlay />
    </ScanProvider>,
  );

const scan = (code: string) => {
  const input = screen.getByTestId("wedge-input");
  fireEvent.change(input, { target: { value: code } });
  fireEvent.keyDown(input, { key: "Enter" });
};

// Render the screen and open a box (resumed=false) so it is "in hand".
const renderWithBoxInHand = async (box = emptyBox) => {
  openMutateAsync.mockResolvedValue({ success: true, resumed: false, transportBox: box });
  renderScreen();
  await act(async () => {
    scan(box.code);
  });
};

describe("BoxFillWorkflow", () => {
  it("shows the empty prompt before any scan", () => {
    renderScreen();
    expect(screen.getByTestId("subject-empty")).toBeInTheDocument();
    expect(screen.getByText("Naskenujte box k naplnění")).toBeInTheDocument();
  });

  it("rejects an invalid box code: error + err flash, no box opened", () => {
    renderScreen();
    act(() => scan("XYZ"));

    expect(screen.getByRole("alert")).toBeInTheDocument();
    expect(openMutateAsync).not.toHaveBeenCalled();
    expect(screen.getByTestId("flash-overlay")).toHaveAttribute("data-tone", "err");
    expect(screen.getByTestId("subject-empty")).toBeInTheDocument();
  });

  it("opens a valid box, shows the subject, and flashes ok when not resumed", async () => {
    await renderWithBoxInHand();

    expect(openMutateAsync).toHaveBeenCalledWith("B001");
    expect(screen.getByTestId("subject-header")).toBeInTheDocument();
    expect(screen.getByTestId("flash-overlay")).toHaveAttribute("data-tone", "ok");
  });

  it("flashes warn and shows the resumed banner when the box is resumed with items", async () => {
    openMutateAsync.mockResolvedValue({ success: true, resumed: true, transportBox: filledBox });
    renderScreen();
    await act(async () => {
      scan("B001");
    });

    expect(screen.getByTestId("flash-overlay")).toHaveAttribute("data-tone", "warn");
    expect(screen.getByText(/Pokračujete v rozpracovaném boxu/)).toBeInTheDocument();
  });

  it("shows an error and flashes err when opening the box fails", async () => {
    openMutateAsync.mockResolvedValue({ success: false });
    renderScreen();
    await act(async () => {
      scan("B001");
    });

    expect(screen.getByRole("alert")).toBeInTheDocument();
    expect(screen.getByTestId("flash-overlay")).toHaveAttribute("data-tone", "err");
    expect(screen.getByTestId("subject-empty")).toBeInTheDocument();
  });

  it("taps an inventory row, confirms an in-stock amount, adds the item, and flashes ok", async () => {
    await renderWithBoxInHand();

    addMutateAsync.mockResolvedValue({ success: true, transportBox: filledBox });

    fireEvent.click(screen.getByTestId("inventory-row-7"));
    fireEvent.change(screen.getByTestId("amount-entry-input"), { target: { value: "2" } });
    fireEvent.click(screen.getByTestId("amount-entry-confirm"));

    expect(await screen.findByTestId("box-item-5")).toBeInTheDocument();
    expect(addMutateAsync).toHaveBeenCalledWith(
      expect.objectContaining({
        boxId: 1, productCode: "P-1", productName: "Krém", amount: 2,
        sourceInventoryId: 7, lotNumber: "L1", expirationDate: "2027-01-01",
        allowNegativeStock: false,
      }),
    );
    expect(screen.getByTestId("flash-overlay")).toHaveAttribute("data-tone", "ok");
  });

  it("opens the overdraft sheet for over-stock; add-with-negative adds with allowNegativeStock and flashes warn", async () => {
    await renderWithBoxInHand();

    fireEvent.click(screen.getByTestId("inventory-row-7"));
    fireEvent.change(screen.getByTestId("amount-entry-input"), { target: { value: "25" } });
    fireEvent.click(screen.getByTestId("amount-entry-confirm"));

    expect(screen.getByTestId("overdraft-add-negative")).toBeInTheDocument();
    expect(addMutateAsync).not.toHaveBeenCalled();

    addMutateAsync.mockResolvedValue({ success: true, transportBox: filledBox });
    fireEvent.click(screen.getByTestId("overdraft-add-negative"));

    await waitFor(() =>
      expect(screen.getByTestId("flash-overlay")).toHaveAttribute("data-tone", "warn"),
    );
    expect(addMutateAsync).toHaveBeenCalledWith(
      expect.objectContaining({ amount: 25, allowNegativeStock: true }),
    );
  });

  it("removes a box item via the remove button", async () => {
    await renderWithBoxInHand(filledBox);

    removeMutateAsync.mockResolvedValue({ success: true, transportBox: emptyBox });
    fireEvent.click(screen.getByTestId("remove-item-5"));

    await waitFor(() =>
      expect(removeMutateAsync).toHaveBeenCalledWith({ boxId: 1, itemId: 5 }),
    );
  });

  it("disables the dock action when the box has 0 items", async () => {
    await renderWithBoxInHand(emptyBox);
    expect(screen.getByTestId("proceed-to-transit")).toBeDisabled();
  });

  it("enables the dock action when the box has items", async () => {
    await renderWithBoxInHand(filledBox);
    expect(screen.getByTestId("proceed-to-transit")).toBeEnabled();
  });

  it("sends to transit via the dock: resets to empty, flashes ok, shows the lastSent banner", async () => {
    await renderWithBoxInHand(filledBox);

    transitMutateAsync.mockResolvedValue({ success: true });
    fireEvent.click(screen.getByTestId("proceed-to-transit"));

    expect(await screen.findByTestId("subject-empty")).toBeInTheDocument();
    expect(transitMutateAsync).toHaveBeenCalledWith(1);
    expect(screen.getByTestId("flash-overlay")).toHaveAttribute("data-tone", "ok");
    expect(screen.getByText(/byl odeslán do přepravy/)).toBeInTheDocument();
  });

  it("re-scanning the in-hand box code with items finalizes (sends to transit)", async () => {
    await renderWithBoxInHand(filledBox);

    transitMutateAsync.mockResolvedValue({ success: true });
    await act(async () => {
      scan("B001");
    });

    expect(transitMutateAsync).toHaveBeenCalledWith(1);
  });

  it("re-scanning the in-hand box code with 0 items flashes warn and does not send", async () => {
    await renderWithBoxInHand(emptyBox);

    await act(async () => {
      scan("B001");
    });

    expect(transitMutateAsync).not.toHaveBeenCalled();
    expect(screen.getByTestId("flash-overlay")).toHaveAttribute("data-tone", "warn");
  });
});
