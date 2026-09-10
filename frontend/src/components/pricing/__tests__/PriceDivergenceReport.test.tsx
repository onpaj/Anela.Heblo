import React from "react";
import { render, screen, fireEvent, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import PriceDivergenceReport from "../PriceDivergenceReport";
import * as hooks from "../../../api/hooks/useProductPricing";
import { PriceDivergenceKind } from "../../../api/generated/api-client";
import { formatCurrency } from "../../../utils/formatters";

jest.mock("../../../api/hooks/useProductPricing", () => {
  const actual = jest.requireActual("../../../api/hooks/useProductPricing");
  return {
    ...actual,
    usePriceDivergenceReport: jest.fn(),
    useSetProductPrice: jest.fn(),
  };
});

const mockUsePriceDivergenceReport = hooks.usePriceDivergenceReport as jest.Mock;
const mockUseSetProductPrice = hooks.useSetProductPrice as jest.Mock;

const inAgreementRow = {
  productCode: "MAS001180",
  productName: "Maska",
  shoptetPriceWithVat: 390.0,
  flexiPriceWithVat: 390.0,
  flexiPriceWithoutVat: 322.31,
  flexiPriceType: "bezDph",
  differenceWithVat: 0,
  differencePercent: 0,
  kind: PriceDivergenceKind.InAgreement,
};

const divergentRow = {
  productCode: "TON002030",
  productName: "Tonikum",
  shoptetPriceWithVat: 390.0,
  flexiPriceWithVat: 447.7,
  flexiPriceWithoutVat: 370.0,
  flexiPriceType: "bezDph",
  differenceWithVat: 57.7,
  differencePercent: 14.79,
  kind: PriceDivergenceKind.FlexiDiffers,
};

const sampleSummary = {
  totalInScope: 2,
  inAgreementCount: 1,
  flexiDiffersCount: 1,
  missingInShoptetCount: 0,
  missingInFlexiCount: 0,
  flexiPriceTypeUnknownCount: 0,
};

beforeEach(() => {
  jest.clearAllMocks();
});

interface RenderReportOptions {
  canWrite?: boolean;
  rows?: unknown[];
  summary?: typeof sampleSummary;
  setPrice?: jest.Mock;
  isPending?: boolean;
  isLoading?: boolean;
  error?: Error | null;
}

const renderReport = ({
  canWrite = false,
  rows = [],
  summary = sampleSummary,
  setPrice = jest.fn(),
  isPending = false,
  isLoading = false,
  error = null,
}: RenderReportOptions = {}) => {
  mockUsePriceDivergenceReport.mockReturnValue(
    isLoading || error
      ? { data: undefined, isLoading, error }
      : { data: { rows, summary }, isLoading: false, error: null },
  );
  mockUseSetProductPrice.mockReturnValue({
    mutateAsync: setPrice,
    isPending,
  });

  return render(<PriceDivergenceReport canWrite={canWrite} />);
};

// Reads the number rendered directly beneath a summary tile's label, so the assertion is
// about that specific count rather than about the substring appearing anywhere on the page.
const summaryCountFor = (label: string): string | null => {
  const summary = screen.getByTestId("divergence-summary");
  const labelNode = within(summary).getByText(label);
  return labelNode.nextElementSibling?.textContent ?? null;
};

test("renders each summary count under its own label", () => {
  // Arrange & Act — deliberately distinct values so no two tiles can satisfy each other's
  // assertion.
  renderReport({
    rows: [inAgreementRow, divergentRow],
    summary: {
      totalInScope: 412,
      inAgreementCount: 380,
      flexiDiffersCount: 17,
      missingInShoptetCount: 9,
      missingInFlexiCount: 4,
      flexiPriceTypeUnknownCount: 2,
    },
  });

  // Assert
  expect(summaryCountFor("Celkem v rozsahu")).toBe("412");
  expect(summaryCountFor("Ve shodě")).toBe("380");
  expect(summaryCountFor("Flexi se liší")).toBe("17");
  expect(summaryCountFor("Chybí v Shoptetu")).toBe("9");
  expect(summaryCountFor("Chybí ve Flexi")).toBe("4");
  expect(summaryCountFor("Neznámý typ ceny")).toBe("2");
});

test("renders a row per product returned by the report", () => {
  // Arrange & Act
  renderReport({ rows: [inAgreementRow, divergentRow] });

  // Assert
  expect(screen.getByText("MAS001180")).toBeInTheDocument();
  expect(screen.getByText("TON002030")).toBeInTheDocument();
});

test("the divergent-only filter hides rows that are in agreement", () => {
  // Arrange
  renderReport({ rows: [inAgreementRow, divergentRow] });
  expect(screen.getByText("MAS001180")).toBeInTheDocument();

  // Act
  fireEvent.click(screen.getByRole("checkbox", { name: "Zobrazit pouze rozdílné", exact: true }));

  // Assert
  expect(screen.queryByText("MAS001180")).not.toBeInTheDocument();
  expect(screen.getByText("TON002030")).toBeInTheDocument();
});

test("unchecking the divergent-only filter shows every row again", () => {
  // Arrange
  renderReport({ rows: [inAgreementRow, divergentRow] });
  const checkbox = screen.getByRole("checkbox", { name: "Zobrazit pouze rozdílné", exact: true });

  // Act
  fireEvent.click(checkbox);
  fireEvent.click(checkbox);

  // Assert
  expect(screen.getByText("MAS001180")).toBeInTheDocument();
  expect(screen.getByText("TON002030")).toBeInTheDocument();
});

test("makes clear the view is read-only when the operator cannot write prices", () => {
  // Arrange & Act
  renderReport({ rows: [inAgreementRow], canWrite: false });

  // Assert
  const banner = screen.getByTestId("divergence-readonly-banner");
  expect(banner).toHaveTextContent(/pouze čtení/i);
  expect(banner).not.toHaveTextContent(/živého/i);
});

test("warns write-capable operators that saving writes straight to the live Shoptet and Flexi systems", () => {
  // Arrange & Act
  renderReport({ rows: [inAgreementRow], canWrite: true });

  // Assert — the read-only reassurance would be actively misleading for someone who can
  // write real prices to production, so it must be replaced, not merely supplemented.
  const banner = screen.getByTestId("divergence-readonly-banner");
  expect(banner).toHaveTextContent(/živého/i);
  expect(banner).not.toHaveTextContent(/pouze čtení/i);
});

test("shows a loading state while the report is fetching", () => {
  // Arrange & Act
  renderReport({ isLoading: true });

  // Assert
  expect(screen.getByText(/Načítání kontroly cen/)).toBeInTheDocument();
});

test("shows an error state when the report fails to load", () => {
  // Arrange & Act
  renderReport({ error: new Error("network down") });

  // Assert
  expect(screen.getByText(/network down/)).toBeInTheDocument();
});

test("does not render an edit button when the operator lacks write permission", () => {
  // Arrange & Act
  renderReport({ canWrite: false, rows: [inAgreementRow] });

  // Assert
  expect(
    screen.queryByRole("button", { name: "Upravit cenu Maska", exact: true }),
  ).not.toBeInTheDocument();
});

test("saves an edited Shoptet price and reloads the comparison", async () => {
  // Arrange
  const setPrice = jest.fn().mockResolvedValue({ priceWithVat: 210 });
  renderReport({
    canWrite: true,
    setPrice,
    rows: [
      {
        productCode: "A",
        productName: "Alpha",
        shoptetPriceWithVat: 190,
        flexiPriceWithVat: 190,
        kind: PriceDivergenceKind.InAgreement,
      },
    ],
  });

  // Act
  await userEvent.click(screen.getByRole("button", { name: "Upravit cenu Alpha", exact: true }));
  const input = screen.getByRole("spinbutton", { name: "Cena s DPH", exact: true });
  await userEvent.clear(input);
  await userEvent.type(input, "210");
  await userEvent.click(screen.getByRole("button", { name: "Uložit", exact: true }));

  // Assert
  await waitFor(() => expect(setPrice).toHaveBeenCalledWith({ productCode: "A", priceWithVat: 210 }));
});

test("cancelling an edit restores the read-only price without saving", async () => {
  // Arrange
  const setPrice = jest.fn();
  renderReport({
    canWrite: true,
    setPrice,
    rows: [
      {
        productCode: "A",
        productName: "Alpha",
        shoptetPriceWithVat: 190,
        flexiPriceWithVat: 190,
        kind: PriceDivergenceKind.InAgreement,
      },
    ],
  });

  // Act
  await userEvent.click(screen.getByRole("button", { name: "Upravit cenu Alpha", exact: true }));
  const input = screen.getByRole("spinbutton", { name: "Cena s DPH", exact: true });
  await userEvent.clear(input);
  await userEvent.type(input, "9999");
  await userEvent.click(screen.getByRole("button", { name: "Zrušit", exact: true }));

  // Assert
  expect(setPrice).not.toHaveBeenCalled();
  expect(
    screen.queryByRole("spinbutton", { name: "Cena s DPH", exact: true }),
  ).not.toBeInTheDocument();
  expect(screen.getByRole("button", { name: "Upravit cenu Alpha", exact: true })).toBeInTheDocument();
});

test("keeps the partial-failure warning visible on the row", async () => {
  // Arrange
  const setPrice = jest
    .fn()
    .mockRejectedValue(Object.assign(new Error("fail"), { errorCode: "ProductPriceFlexiWriteFailed" }));
  renderReport({
    canWrite: true,
    setPrice,
    rows: [
      {
        productCode: "A",
        productName: "Alpha",
        shoptetPriceWithVat: 190,
        flexiPriceWithVat: 190,
        kind: PriceDivergenceKind.InAgreement,
      },
    ],
  });

  // Act
  await userEvent.click(screen.getByRole("button", { name: "Upravit cenu Alpha", exact: true }));
  await userEvent.click(screen.getByRole("button", { name: "Uložit", exact: true }));

  // Assert
  expect(await screen.findByRole("alert")).toHaveTextContent(/Flexi/);
});

test("renders a lighter error when the write never reached Shoptet", async () => {
  // Arrange
  const setPrice = jest
    .fn()
    .mockRejectedValue(Object.assign(new Error("fail"), { errorCode: "ProductPriceNotFoundInShoptet" }));
  renderReport({
    canWrite: true,
    setPrice,
    rows: [
      {
        productCode: "A",
        productName: "Alpha",
        shoptetPriceWithVat: 190,
        flexiPriceWithVat: 190,
        kind: PriceDivergenceKind.InAgreement,
      },
    ],
  });

  // Act
  await userEvent.click(screen.getByRole("button", { name: "Upravit cenu Alpha", exact: true }));
  await userEvent.click(screen.getByRole("button", { name: "Uložit", exact: true }));

  // Assert
  expect(await screen.findByRole("alert")).toHaveTextContent(/maloobchodním ceníku Shoptetu/);
});

test("requires confirmation before saving a price that swings more than 50% from the current Shoptet price, and skips the save when declined", async () => {
  // Arrange
  const setPrice = jest.fn();
  const confirmSpy = jest.spyOn(window, "confirm").mockReturnValue(false);
  renderReport({
    canWrite: true,
    setPrice,
    rows: [
      {
        productCode: "A",
        productName: "Alpha",
        shoptetPriceWithVat: 200,
        flexiPriceWithVat: 200,
        kind: PriceDivergenceKind.InAgreement,
      },
    ],
  });

  // Act
  await userEvent.click(screen.getByRole("button", { name: "Upravit cenu Alpha", exact: true }));
  const input = screen.getByRole("spinbutton", { name: "Cena s DPH", exact: true });
  await userEvent.clear(input);
  await userEvent.type(input, "2000");
  await userEvent.click(screen.getByRole("button", { name: "Uložit", exact: true }));

  // Assert
  expect(confirmSpy).toHaveBeenCalledTimes(1);
  const confirmMessage = confirmSpy.mock.calls[0][0];
  expect(confirmMessage).toEqual(expect.stringContaining(formatCurrency(200)));
  expect(confirmMessage).toEqual(expect.stringContaining(formatCurrency(2000)));
  expect(setPrice).not.toHaveBeenCalled();

  confirmSpy.mockRestore();
});

test("saves once the operator confirms a price change larger than 50%", async () => {
  // Arrange
  const setPrice = jest.fn().mockResolvedValue({ priceWithVat: 2000 });
  const confirmSpy = jest.spyOn(window, "confirm").mockReturnValue(true);
  renderReport({
    canWrite: true,
    setPrice,
    rows: [
      {
        productCode: "A",
        productName: "Alpha",
        shoptetPriceWithVat: 200,
        flexiPriceWithVat: 200,
        kind: PriceDivergenceKind.InAgreement,
      },
    ],
  });

  // Act
  await userEvent.click(screen.getByRole("button", { name: "Upravit cenu Alpha", exact: true }));
  const input = screen.getByRole("spinbutton", { name: "Cena s DPH", exact: true });
  await userEvent.clear(input);
  await userEvent.type(input, "2000");
  await userEvent.click(screen.getByRole("button", { name: "Uložit", exact: true }));

  // Assert
  await waitFor(() => expect(setPrice).toHaveBeenCalledWith({ productCode: "A", priceWithVat: 2000 }));

  confirmSpy.mockRestore();
});

test("does not ask for confirmation when the price change is within 50%", async () => {
  // Arrange
  const setPrice = jest.fn().mockResolvedValue({ priceWithVat: 250 });
  const confirmSpy = jest.spyOn(window, "confirm").mockReturnValue(true);
  renderReport({
    canWrite: true,
    setPrice,
    rows: [
      {
        productCode: "A",
        productName: "Alpha",
        shoptetPriceWithVat: 200,
        flexiPriceWithVat: 200,
        kind: PriceDivergenceKind.InAgreement,
      },
    ],
  });

  // Act
  await userEvent.click(screen.getByRole("button", { name: "Upravit cenu Alpha", exact: true }));
  const input = screen.getByRole("spinbutton", { name: "Cena s DPH", exact: true });
  await userEvent.clear(input);
  await userEvent.type(input, "250");
  await userEvent.click(screen.getByRole("button", { name: "Uložit", exact: true }));

  // Assert
  await waitFor(() => expect(setPrice).toHaveBeenCalledWith({ productCode: "A", priceWithVat: 250 }));
  expect(confirmSpy).not.toHaveBeenCalled();

  confirmSpy.mockRestore();
});

test("always confirms a first price on a row with no Shoptet baseline, and skips the save when declined", async () => {
  // Arrange — a MissingInShoptet row has no shoptetPriceWithVat by construction, so the
  // 50%-change ratio has nothing to compare against. That must not mean "no confirmation":
  // any magnitude here goes straight to the live shop with zero safety net otherwise.
  const setPrice = jest.fn();
  const confirmSpy = jest.spyOn(window, "confirm").mockReturnValue(false);
  renderReport({
    canWrite: true,
    setPrice,
    rows: [
      {
        productCode: "A",
        productName: "Alpha",
        shoptetPriceWithVat: null,
        flexiPriceWithVat: 200,
        kind: PriceDivergenceKind.MissingInShoptet,
      },
    ],
  });

  // Act
  await userEvent.click(screen.getByRole("button", { name: "Upravit cenu Alpha", exact: true }));
  const input = screen.getByRole("spinbutton", { name: "Cena s DPH", exact: true });
  await userEvent.type(input, "350");
  await userEvent.click(screen.getByRole("button", { name: "Uložit", exact: true }));

  // Assert
  expect(confirmSpy).toHaveBeenCalledTimes(1);
  const confirmMessage = confirmSpy.mock.calls[0][0];
  expect(confirmMessage).toEqual(expect.stringContaining("Alpha"));
  expect(confirmMessage).toEqual(expect.stringContaining(formatCurrency(350)));
  expect(setPrice).not.toHaveBeenCalled();

  confirmSpy.mockRestore();
});

test("saves a first price on a row with no Shoptet baseline once the operator confirms", async () => {
  // Arrange
  const setPrice = jest.fn().mockResolvedValue({ priceWithVat: 350 });
  const confirmSpy = jest.spyOn(window, "confirm").mockReturnValue(true);
  renderReport({
    canWrite: true,
    setPrice,
    rows: [
      {
        productCode: "A",
        productName: "Alpha",
        shoptetPriceWithVat: null,
        flexiPriceWithVat: 200,
        kind: PriceDivergenceKind.MissingInShoptet,
      },
    ],
  });

  // Act
  await userEvent.click(screen.getByRole("button", { name: "Upravit cenu Alpha", exact: true }));
  const input = screen.getByRole("spinbutton", { name: "Cena s DPH", exact: true });
  await userEvent.type(input, "350");
  await userEvent.click(screen.getByRole("button", { name: "Uložit", exact: true }));

  // Assert
  await waitFor(() => expect(setPrice).toHaveBeenCalledWith({ productCode: "A", priceWithVat: 350 }));

  confirmSpy.mockRestore();
});

test("rejects a blank price draft without calling the mutation or prompting for confirmation", async () => {
  // Arrange — Number("") is 0, which is finite, so without an explicit blank check this
  // would otherwise slip past validation and fire a live request carrying 0.
  const setPrice = jest.fn();
  const confirmSpy = jest.spyOn(window, "confirm").mockReturnValue(true);
  renderReport({
    canWrite: true,
    setPrice,
    rows: [
      {
        productCode: "A",
        productName: "Alpha",
        shoptetPriceWithVat: 190,
        flexiPriceWithVat: 190,
        kind: PriceDivergenceKind.InAgreement,
      },
    ],
  });

  // Act
  await userEvent.click(screen.getByRole("button", { name: "Upravit cenu Alpha", exact: true }));
  const input = screen.getByRole("spinbutton", { name: "Cena s DPH", exact: true });
  await userEvent.clear(input);
  await userEvent.click(screen.getByRole("button", { name: "Uložit", exact: true }));

  // Assert
  expect(setPrice).not.toHaveBeenCalled();
  expect(confirmSpy).not.toHaveBeenCalled();

  confirmSpy.mockRestore();
});

test("rejects a price draft below the minimum billable amount without calling the mutation", async () => {
  // Arrange
  const setPrice = jest.fn();
  const confirmSpy = jest.spyOn(window, "confirm").mockReturnValue(true);
  renderReport({
    canWrite: true,
    setPrice,
    rows: [
      {
        productCode: "A",
        productName: "Alpha",
        shoptetPriceWithVat: 190,
        flexiPriceWithVat: 190,
        kind: PriceDivergenceKind.InAgreement,
      },
    ],
  });

  // Act
  await userEvent.click(screen.getByRole("button", { name: "Upravit cenu Alpha", exact: true }));
  const input = screen.getByRole("spinbutton", { name: "Cena s DPH", exact: true });
  await userEvent.clear(input);
  await userEvent.type(input, "0");
  await userEvent.click(screen.getByRole("button", { name: "Uložit", exact: true }));

  // Assert
  expect(setPrice).not.toHaveBeenCalled();
  expect(confirmSpy).not.toHaveBeenCalled();

  confirmSpy.mockRestore();
});
