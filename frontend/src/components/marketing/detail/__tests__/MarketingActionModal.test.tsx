import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import MarketingActionModal from "../MarketingActionModal";
import type { MarketingActionDto } from "../../list/MarketingActionGrid";

const mockCreateMutateAsync = jest.fn();
const mockUpdateMutateAsync = jest.fn();
const mockDeleteMutateAsync = jest.fn();

jest.mock("../../../common/CatalogAutocomplete", () => ({
  CatalogAutocomplete: ({ placeholder, onSelect }: { placeholder?: string; onSelect: (v: string | null) => void }) => (
    <input
      data-testid="catalog-autocomplete"
      placeholder={placeholder}
      onChange={(e) => onSelect(e.target.value || null)}
    />
  ),
}));

jest.mock("../../../common/CatalogAutocompleteAdapters", () => ({
  catalogItemToProductCode: (item: any) => item,
  PRODUCT_TYPE_FILTERS: { ALL: "all" },
}));

jest.mock("../../../../api/hooks/useMarketingCalendar", () => ({
  useCreateMarketingAction: () => ({
    mutateAsync: mockCreateMutateAsync,
    isPending: false,
  }),
  useUpdateMarketingAction: () => ({
    mutateAsync: mockUpdateMutateAsync,
    isPending: false,
  }),
  useDeleteMarketingAction: () => ({
    mutateAsync: mockDeleteMutateAsync,
    isPending: false,
  }),
}));

const defaultProps = {
  isOpen: true,
  onClose: jest.fn(),
  existingAction: null,
};

function fillRequiredFields() {
  const titleInput = screen.getAllByRole("textbox")[0];
  fireEvent.change(titleInput, { target: { value: "Test akce" } });

  const dateInputs = document.querySelectorAll<HTMLInputElement>('input[type="date"]');
  fireEvent.change(dateInputs[0], { target: { value: "2026-05-01" } });
  fireEvent.change(dateInputs[1], { target: { value: "2026-05-31" } });
}

function submitForm() {
  fireEvent.submit(document.getElementById("marketing-action-form")!);
}

beforeEach(() => {
  jest.clearAllMocks();
  mockCreateMutateAsync.mockResolvedValue({});
  mockUpdateMutateAsync.mockResolvedValue({});
  mockDeleteMutateAsync.mockResolvedValue({});
});

// ─── CREATE ────────────────────────────────────────────────────────────────────

describe("MarketingActionModal — create", () => {
  it("submits actionType as a number, not a label string", async () => {
    render(<MarketingActionModal {...defaultProps} />);
    fillRequiredFields();
    submitForm();

    await waitFor(() =>
      expect(mockCreateMutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({ actionType: 0 }),
      ),
    );
    expect(typeof mockCreateMutateAsync.mock.calls[0][0].actionType).toBe("number");
  });

  it('submits "Ostatní" actionType as 99, not 5', async () => {
    render(<MarketingActionModal {...defaultProps} />);
    fillRequiredFields();
    fireEvent.change(screen.getByRole("combobox"), { target: { value: "99" } });
    submitForm();

    await waitFor(() =>
      expect(mockCreateMutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({ actionType: 99 }),
      ),
    );
  });

  it("submits startDate and endDate as Date objects", async () => {
    render(<MarketingActionModal {...defaultProps} />);
    fillRequiredFields();
    submitForm();

    await waitFor(() => {
      const payload = mockCreateMutateAsync.mock.calls[0][0];
      expect(payload.startDate).toBeInstanceOf(Date);
      expect(payload.endDate).toBeInstanceOf(Date);
    });
  });

  it("calls onClose after successful submit", async () => {
    const onClose = jest.fn();
    render(<MarketingActionModal {...defaultProps} onClose={onClose} />);
    fillRequiredFields();
    submitForm();

    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });

  it("shows error message when create fails", async () => {
    mockCreateMutateAsync.mockRejectedValue(new Error("API error"));
    render(<MarketingActionModal {...defaultProps} />);
    fillRequiredFields();
    submitForm();

    await waitFor(() =>
      expect(
        screen.getByText("Nepodařilo se uložit akci. Zkuste to znovu."),
      ).toBeInTheDocument(),
    );
  });
});

// ─── EDIT — field population ───────────────────────────────────────────────────

describe("MarketingActionModal — edit field population", () => {
  const existingAction: MarketingActionDto = {
    id: 1,
    title: "Existující akce",
    detail: "Popis akce",
    actionType: "Other",
    dateFrom: "2026-04-01T10:00:00",
    dateTo: "2026-04-30T23:59:00",
    associatedProducts: ["AKL001", "AKL002"],
    folderLinks: [{ path: "folder/key", label: "Složka", folderType: "Campaign" }],
  };

  it("populates title", () => {
    render(<MarketingActionModal {...defaultProps} existingAction={existingAction} />);
    expect(screen.getAllByRole("textbox")[0]).toHaveValue("Existující akce");
  });

  it("populates dateFrom as YYYY-MM-DD from ISO string", () => {
    render(<MarketingActionModal {...defaultProps} existingAction={existingAction} />);
    const dates = document.querySelectorAll<HTMLInputElement>('input[type="date"]');
    expect(dates[0]).toHaveValue("2026-04-01");
  });

  it("populates dateTo as YYYY-MM-DD from ISO string", () => {
    render(<MarketingActionModal {...defaultProps} existingAction={existingAction} />);
    const dates = document.querySelectorAll<HTMLInputElement>('input[type="date"]');
    expect(dates[1]).toHaveValue("2026-04-30");
  });

  it("populates dateFrom as YYYY-MM-DD from Date object", () => {
    render(
      <MarketingActionModal
        {...defaultProps}
        existingAction={{ ...existingAction, dateFrom: new Date("2026-04-15T00:00:00") }}
      />,
    );
    const dates = document.querySelectorAll<HTMLInputElement>('input[type="date"]');
    expect(dates[0]).toHaveValue("2026-04-15");
  });

  it('restores actionType "Other" (backend name) to select value 99', () => {
    render(<MarketingActionModal {...defaultProps} existingAction={existingAction} />);
    const actionTypeSelect = screen.getAllByRole("combobox")[0] as HTMLSelectElement;
    expect(actionTypeSelect.value).toBe("99");
  });

  it("restores actionType by backend enum name (Launch → 2)", () => {
    render(
      <MarketingActionModal
        {...defaultProps}
        existingAction={{ ...existingAction, actionType: "Launch" }}
      />,
    );
    const actionTypeSelect = screen.getAllByRole("combobox")[0] as HTMLSelectElement;
    expect(actionTypeSelect.value).toBe("2");
  });

  it("restores actionType by numeric string (e.g. '2')", () => {
    render(
      <MarketingActionModal
        {...defaultProps}
        existingAction={{ ...existingAction, actionType: "2" }}
      />,
    );
    const actionTypeSelect = screen.getAllByRole("combobox")[0] as HTMLSelectElement;
    expect(actionTypeSelect.value).toBe("2");
  });

  it("populates associatedProducts chips", () => {
    render(<MarketingActionModal {...defaultProps} existingAction={existingAction} />);
    expect(screen.getByText("AKL001")).toBeInTheDocument();
    expect(screen.getByText("AKL002")).toBeInTheDocument();
  });
});

// ─── EDIT — submit payload ─────────────────────────────────────────────────────

describe("MarketingActionModal — edit submit", () => {
  const existingAction: MarketingActionDto = {
    id: 1,
    title: "Existující akce",
    detail: "Popis",
    actionType: "Other",
    dateFrom: "2026-04-01T00:00:00",
    dateTo: "2026-04-30T00:00:00",
    associatedProducts: [],
    folderLinks: [{ path: "folder/key", label: "Složka", folderType: "Campaign" }],
  };

  it("submits actionType as number 99 when editing Other", async () => {
    render(<MarketingActionModal {...defaultProps} existingAction={existingAction} />);
    submitForm();

    await waitFor(() =>
      expect(mockUpdateMutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({
          id: 1,
          request: expect.objectContaining({ actionType: 99 }),
        }),
      ),
    );
  });

  it("submits folderType as number (Campaign → 3)", async () => {
    render(<MarketingActionModal {...defaultProps} existingAction={existingAction} />);
    submitForm();

    await waitFor(() => {
      const { request } = mockUpdateMutateAsync.mock.calls[0][0];
      expect(request.folderLinks[0].folderType).toBe(3);
    });
  });
});

// ─── DELETE ────────────────────────────────────────────────────────────────────

describe("MarketingActionModal — delete", () => {
  const existingAction: MarketingActionDto = {
    id: 42,
    title: "Ke smazání",
    actionType: "General",
    dateFrom: "2026-04-01",
    dateTo: "2026-04-30",
  };

  it("calls delete mutation with action id after confirmation", async () => {
    window.confirm = jest.fn(() => true);
    render(<MarketingActionModal {...defaultProps} existingAction={existingAction} />);

    fireEvent.click(screen.getByRole("button", { name: /smazat/i }));

    await waitFor(() => expect(mockDeleteMutateAsync).toHaveBeenCalledWith(42));
  });

  it("does not delete when confirmation is cancelled", async () => {
    window.confirm = jest.fn(() => false);
    render(<MarketingActionModal {...defaultProps} existingAction={existingAction} />);

    fireEvent.click(screen.getByRole("button", { name: /smazat/i }));

    await waitFor(() => expect(mockDeleteMutateAsync).not.toHaveBeenCalled());
  });

  it("shows error when delete fails", async () => {
    window.confirm = jest.fn(() => true);
    mockDeleteMutateAsync.mockRejectedValue(new Error("Delete failed"));
    render(<MarketingActionModal {...defaultProps} existingAction={existingAction} />);

    fireEvent.click(screen.getByRole("button", { name: /smazat/i }));

    await waitFor(() =>
      expect(screen.getByText("Nepodařilo se smazat akci.")).toBeInTheDocument(),
    );
  });
});
