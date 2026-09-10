import React from "react";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import ProductPickerModal, {
  PRODUCT_PICKER_RESULT_LIMIT,
} from "../ProductPickerModal";

const mockUseCatalogAutocomplete = jest.fn();

jest.mock("../../../api/hooks/useCatalogAutocomplete", () => ({
  useCatalogAutocomplete: (...args: unknown[]) =>
    mockUseCatalogAutocomplete(...args),
}));

const item = (code: string, name: string) => ({
  productCode: code,
  productName: name,
});

const setResults = (
  items: Array<{ productCode: string; productName: string }>,
  overrides: Record<string, unknown> = {},
) => {
  mockUseCatalogAutocomplete.mockReturnValue({
    data: { items },
    isLoading: false,
    isError: false,
    ...overrides,
  });
};

const baseProps = {
  isOpen: true,
  selectedProducts: [],
  maxProducts: 10,
  onConfirm: jest.fn(),
  onClose: jest.fn(),
};

// The search term is debounced before it reaches the query, so every assertion on
// results waits for that timer rather than reading straight after typing.
const search = async (user: ReturnType<typeof userEvent.setup>, term: string) => {
  await user.type(screen.getByLabelText("Hledat produkt"), term);
  await waitFor(() =>
    expect(mockUseCatalogAutocomplete).toHaveBeenCalledWith(
      term,
      PRODUCT_PICKER_RESULT_LIMIT,
    ),
  );
};

describe("ProductPickerModal", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    setResults([]);
  });

  test("renders nothing when closed", () => {
    render(<ProductPickerModal {...baseProps} isOpen={false} />);

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  test("asks for more characters before searching", () => {
    render(<ProductPickerModal {...baseProps} />);

    expect(screen.getByText(/alespoň 2 znaky/i)).toBeInTheDocument();
  });

  test("renders a checkbox per search result", async () => {
    const user = userEvent.setup();
    setResults([item("DOL03", "Důvěrný pan Levandule"), item("DOM12", "Domácí mýdlo")]);
    render(<ProductPickerModal {...baseProps} />);

    await search(user, "DO");

    expect(
      screen.getByRole("checkbox", { name: "Důvěrný pan Levandule (DOL03)" }),
    ).not.toBeChecked();
    expect(
      screen.getByRole("checkbox", { name: "Domácí mýdlo (DOM12)" }),
    ).toBeInTheDocument();
  });

  test("confirming emits the ticked products", async () => {
    const user = userEvent.setup();
    const onConfirm = jest.fn();
    setResults([item("DOL03", "Důvěrný pan Levandule"), item("DOM12", "Domácí mýdlo")]);
    render(<ProductPickerModal {...baseProps} onConfirm={onConfirm} />);

    await search(user, "DO");
    await user.click(
      screen.getByRole("checkbox", { name: "Domácí mýdlo (DOM12)" }),
    );
    await user.click(screen.getByRole("button", { name: "Potvrdit" }));

    expect(onConfirm).toHaveBeenCalledWith([
      { productCode: "DOM12", productName: "Domácí mýdlo" },
    ]);
  });

  test("cancelling discards the draft", async () => {
    const user = userEvent.setup();
    const onConfirm = jest.fn();
    const onClose = jest.fn();
    setResults([item("DOL03", "Důvěrný pan Levandule")]);
    render(
      <ProductPickerModal {...baseProps} onConfirm={onConfirm} onClose={onClose} />,
    );

    await search(user, "DO");
    await user.click(
      screen.getByRole("checkbox", { name: "Důvěrný pan Levandule (DOL03)" }),
    );
    await user.click(screen.getByRole("button", { name: "Zrušit" }));

    expect(onConfirm).not.toHaveBeenCalled();
    expect(onClose).toHaveBeenCalled();
  });

  test("lists already selected products so they can be unticked without searching", async () => {
    const user = userEvent.setup();
    const onConfirm = jest.fn();
    render(
      <ProductPickerModal
        {...baseProps}
        selectedProducts={[{ productCode: "AKL027", productName: "Demineralizovaná voda" }]}
        onConfirm={onConfirm}
      />,
    );

    const selected = screen.getByRole("group", { name: "Vybráno" });
    const checkbox = within(selected).getByRole("checkbox", {
      name: "Demineralizovaná voda (AKL027)",
    });
    expect(checkbox).toBeChecked();

    await user.click(checkbox);
    await user.click(screen.getByRole("button", { name: "Potvrdit" }));

    expect(onConfirm).toHaveBeenCalledWith([]);
  });

  test("disables unticked products once the cap is reached", async () => {
    const user = userEvent.setup();
    setResults([item("DOL03", "Důvěrný pan Levandule"), item("DOM12", "Domácí mýdlo")]);
    render(<ProductPickerModal {...baseProps} maxProducts={1} />);

    await search(user, "DO");
    await user.click(
      screen.getByRole("checkbox", { name: "Důvěrný pan Levandule (DOL03)" }),
    );

    expect(
      screen.getByRole("checkbox", { name: "Domácí mýdlo (DOM12)" }),
    ).toBeDisabled();
    // The ticked one stays clickable, so a wrong pick can be swapped out.
    expect(
      screen.getByRole("checkbox", { name: "Důvěrný pan Levandule (DOL03)" }),
    ).toBeEnabled();
    expect(screen.getByText("Vybráno 1/1")).toBeInTheDocument();
    expect(screen.getByText(/Maximálně 1 produktů/)).toBeInTheDocument();
  });

  test("says when the result list was cut off at the display limit", async () => {
    const user = userEvent.setup();
    setResults(
      Array.from({ length: PRODUCT_PICKER_RESULT_LIMIT }, (_, index) =>
        item(`DO${index}`, `Produkt ${index}`),
      ),
    );
    render(<ProductPickerModal {...baseProps} />);

    await search(user, "DO");

    expect(
      screen.getByText(
        `Zobrazeno prvních ${PRODUCT_PICKER_RESULT_LIMIT} výsledků, upřesněte hledání.`,
      ),
    ).toBeInTheDocument();
  });

  test("reports a failed search", async () => {
    const user = userEvent.setup();
    setResults([], { isError: true, data: undefined });
    render(<ProductPickerModal {...baseProps} />);

    await search(user, "DO");

    expect(screen.getByText("Nepodařilo se načíst produkty.")).toBeInTheDocument();
  });
});
