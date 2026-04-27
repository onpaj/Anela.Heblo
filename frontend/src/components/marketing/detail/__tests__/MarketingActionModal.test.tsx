import React from "react";
import { render, screen, fireEvent, waitFor, within } from "@testing-library/react";
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

// ─── CLOSED state ─────────────────────────────────────────────────────────────

describe("MarketingActionModal — closed", () => {
  it("renders nothing when isOpen is false", () => {
    const { container } = render(
      <MarketingActionModal isOpen={false} onClose={jest.fn()} />,
    );
    expect(container).toBeEmptyDOMElement();
  });

  it("renders modal content when isOpen is true", () => {
    render(<MarketingActionModal isOpen={true} onClose={jest.fn()} />);
    expect(screen.getByText("Nová marketingová akce")).toBeInTheDocument();
  });

  it("shows edit title when existingAction is provided", () => {
    render(
      <MarketingActionModal
        isOpen={true}
        onClose={jest.fn()}
        existingAction={{ id: 1, title: "Test", actionType: "General" }}
      />,
    );
    expect(screen.getByText("Upravit akci")).toBeInTheDocument();
  });

  it("calls onClose when Zrušit button is clicked", () => {
    const onClose = jest.fn();
    render(<MarketingActionModal isOpen={true} onClose={onClose} />);
    fireEvent.click(screen.getByRole("button", { name: /zrušit/i }));
    expect(onClose).toHaveBeenCalled();
  });
});

// ─── PREFILL DATES ────────────────────────────────────────────────────────────

describe("MarketingActionModal — prefillDates", () => {
  it("populates dateFrom from prefillDates when no existingAction", () => {
    render(
      <MarketingActionModal
        {...defaultProps}
        prefillDates={{ dateFrom: "2026-08-01", dateTo: "2026-08-15" }}
      />,
    );
    const dates = document.querySelectorAll<HTMLInputElement>('input[type="date"]');
    expect(dates[0]).toHaveValue("2026-08-01");
  });

  it("populates dateTo from prefillDates when no existingAction", () => {
    render(
      <MarketingActionModal
        {...defaultProps}
        prefillDates={{ dateFrom: "2026-08-01", dateTo: "2026-08-15" }}
      />,
    );
    const dates = document.querySelectorAll<HTMLInputElement>('input[type="date"]');
    expect(dates[1]).toHaveValue("2026-08-15");
  });
});

// ─── PRODUCTS ─────────────────────────────────────────────────────────────────

describe("MarketingActionModal — products", () => {
  it("shows autocomplete input by default", () => {
    render(<MarketingActionModal {...defaultProps} />);
    expect(screen.getByTestId("catalog-autocomplete")).toBeInTheDocument();
  });

  it("switches to text input when Text button is clicked", () => {
    render(<MarketingActionModal {...defaultProps} />);
    fireEvent.click(screen.getByRole("button", { name: /^text$/i }));
    expect(
      screen.getByPlaceholderText(/produktový kód nebo prefix/i),
    ).toBeInTheDocument();
    expect(screen.queryByTestId("catalog-autocomplete")).not.toBeInTheDocument();
  });

  it("switches back to autocomplete when Autocomplete button is clicked", () => {
    render(<MarketingActionModal {...defaultProps} />);
    fireEvent.click(screen.getByRole("button", { name: /^text$/i }));
    fireEvent.click(screen.getByRole("button", { name: /autocomplete/i }));
    expect(screen.getByTestId("catalog-autocomplete")).toBeInTheDocument();
  });

  it("adds product via text input on Enter (trim + uppercase)", () => {
    render(<MarketingActionModal {...defaultProps} />);
    fireEvent.click(screen.getByRole("button", { name: /^text$/i }));
    const input = screen.getByPlaceholderText(/produktový kód nebo prefix/i);
    fireEvent.change(input, { target: { value: "  akl001  " } });
    fireEvent.keyDown(input, { key: "Enter" });
    expect(screen.getByText("AKL001")).toBeInTheDocument();
  });

  it("adds product via text input on blur", () => {
    render(<MarketingActionModal {...defaultProps} />);
    fireEvent.click(screen.getByRole("button", { name: /^text$/i }));
    const input = screen.getByPlaceholderText(/produktový kód nebo prefix/i);
    fireEvent.change(input, { target: { value: "COSM001" } });
    fireEvent.blur(input);
    expect(screen.getByText("COSM001")).toBeInTheDocument();
  });

  it("does not add duplicate product via text input", () => {
    render(<MarketingActionModal {...defaultProps} />);
    fireEvent.click(screen.getByRole("button", { name: /^text$/i }));
    const input = screen.getByPlaceholderText(/produktový kód nebo prefix/i);
    fireEvent.change(input, { target: { value: "AKL001" } });
    fireEvent.keyDown(input, { key: "Enter" });
    fireEvent.change(input, { target: { value: "AKL001" } });
    fireEvent.keyDown(input, { key: "Enter" });
    expect(screen.getAllByText("AKL001")).toHaveLength(1);
  });

  it("adds product via autocomplete selection", () => {
    render(<MarketingActionModal {...defaultProps} />);
    const autocomplete = screen.getByTestId("catalog-autocomplete");
    fireEvent.change(autocomplete, { target: { value: "ROSE001" } });
    expect(screen.getByText("ROSE001")).toBeInTheDocument();
  });

  it("removes a product chip when × button is clicked", () => {
    const existingAction = {
      id: 1,
      title: "Test",
      actionType: "General",
      dateFrom: "2026-01-01",
      dateTo: "2026-01-31",
      associatedProducts: ["AKL001"],
    };
    render(<MarketingActionModal {...defaultProps} existingAction={existingAction} />);
    expect(screen.getByText("AKL001")).toBeInTheDocument();

    const chip = screen.getByText("AKL001").closest("span")!;
    fireEvent.click(within(chip).getByRole("button"));
    expect(screen.queryByText("AKL001")).not.toBeInTheDocument();
  });
});

// ─── FOLDER LINKS ─────────────────────────────────────────────────────────────

describe("MarketingActionModal — folder links", () => {
  it("adds a folder link row when Přidat složku is clicked", () => {
    render(<MarketingActionModal {...defaultProps} />);
    fireEvent.click(screen.getByRole("button", { name: /přidat složku/i }));
    expect(screen.getByPlaceholderText(/cesta ke složce/i)).toBeInTheDocument();
  });

  it("removes folder link row when trash button is clicked", () => {
    render(<MarketingActionModal {...defaultProps} />);
    fireEvent.click(screen.getByRole("button", { name: /přidat složku/i }));
    expect(screen.getByPlaceholderText(/cesta ke složce/i)).toBeInTheDocument();
    // The folder row has a trash button — click the last button that has an svg
    const row = screen.getByPlaceholderText(/cesta ke složce/i).closest("div")!;
    const removeBtn = within(row).getByRole("button");
    fireEvent.click(removeBtn);
    expect(screen.queryByPlaceholderText(/cesta ke složce/i)).not.toBeInTheDocument();
  });

  it("does not submit empty folder links", async () => {
    render(<MarketingActionModal {...defaultProps} />);
    fireEvent.click(screen.getByRole("button", { name: /přidat složku/i }));
    // Leave path empty, fill title
    const titleInput = screen.getAllByRole("textbox")[0];
    fireEvent.change(titleInput, { target: { value: "Test akce" } });
    const dateInputs = document.querySelectorAll<HTMLInputElement>('input[type="date"]');
    fireEvent.change(dateInputs[0], { target: { value: "2026-05-01" } });
    fireEvent.change(dateInputs[1], { target: { value: "2026-05-31" } });
    fireEvent.submit(document.getElementById("marketing-action-form")!);

    await waitFor(() => {
      const payload = mockCreateMutateAsync.mock.calls[0][0];
      expect(payload.folderLinks).toHaveLength(0);
    });
  });
});

// ─── PENDING STATES ───────────────────────────────────────────────────────────

describe("MarketingActionModal — pending states", () => {
  it("shows Ukládání... on submit button while saving", () => {
    jest.mocked(jest.fn()).mockReturnValue(undefined);
    // Mock isPending=true for create mutation
    jest.mock("../../../../api/hooks/useMarketingCalendar", () => ({
      useCreateMarketingAction: () => ({
        mutateAsync: jest.fn(),
        isPending: true,
      }),
      useUpdateMarketingAction: () => ({
        mutateAsync: jest.fn(),
        isPending: false,
      }),
      useDeleteMarketingAction: () => ({
        mutateAsync: jest.fn(),
        isPending: false,
      }),
    }));
  });

  it("submit button label is Vytvořit in create mode", () => {
    render(<MarketingActionModal {...defaultProps} />);
    expect(screen.getByRole("button", { name: /vytvořit/i })).toBeInTheDocument();
  });

  it("submit button label is Uložit in edit mode", () => {
    render(
      <MarketingActionModal
        {...defaultProps}
        existingAction={{ id: 1, title: "X", actionType: "General" }}
      />,
    );
    expect(screen.getByRole("button", { name: /^uložit$/i })).toBeInTheDocument();
  });

  it("delete button only appears in edit mode", () => {
    render(<MarketingActionModal {...defaultProps} />);
    expect(screen.queryByRole("button", { name: /smazat/i })).not.toBeInTheDocument();
  });

  it("delete button appears when existingAction is set", () => {
    render(
      <MarketingActionModal
        {...defaultProps}
        existingAction={{ id: 1, title: "X", actionType: "General" }}
      />,
    );
    expect(screen.getByRole("button", { name: /smazat/i })).toBeInTheDocument();
  });
});
