import React from "react";
import { render, screen, within } from "@testing-library/react";
import ManufactureOutputModal from "../ManufactureOutputModal";
import { ManufactureOutputMonth } from "../../../api/hooks/useManufactureOutput";

const monthData = {
  month: "2026-03",
  totalOutput: 12,
  products: [
    { productCode: "PRD001", productName: "Krém ND", quantity: 2, difficulty: 3, weightedValue: 6, isNd: true },
    { productCode: "PRD002", productName: "Mýdlo", quantity: 6, difficulty: 1, weightedValue: 6, isNd: false },
  ],
  productionDetails: [],
} as unknown as ManufactureOutputMonth;

describe("ManufactureOutputModal", () => {
  it("shows the ND star only next to ND products", () => {
    // Arrange / Act
    render(<ManufactureOutputModal isOpen onClose={jest.fn()} monthData={monthData} />);

    // Assert
    const ndRow = screen.getByText("Krém ND").closest("tr") as HTMLElement;
    const otherRow = screen.getByText("Mýdlo").closest("tr") as HTMLElement;
    expect(within(ndRow).getByTestId("nd-star")).toBeInTheDocument();
    expect(within(otherRow).queryByTestId("nd-star")).not.toBeInTheDocument();
  });
});
