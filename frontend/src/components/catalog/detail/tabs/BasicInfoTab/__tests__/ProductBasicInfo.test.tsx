import React from "react";
import { render, screen } from "@testing-library/react";
import { ProductType } from "../../../../../../api/hooks/useCatalog";
import ProductBasicInfo from "../ProductBasicInfo";

const mockItem = {
  productCode: "TEST-001",
  productName: "Test Product",
  type: ProductType.Product,
  location: "Test Location",
  minimalOrderQuantity: 10,
  minimalManufactureQuantity: 5,
  supplierName: "Test Supplier",
  note: "Test note for product",
};

describe("ProductBasicInfo", () => {
  it("renders product information correctly", () => {
    render(<ProductBasicInfo item={mockItem} />);

    expect(screen.getByText("Základní informace")).toBeInTheDocument();
    expect(screen.getByText("Produkt")).toBeInTheDocument();
    expect(screen.getByText("Test Location")).toBeInTheDocument();
    expect(screen.getByText("10")).toBeInTheDocument();
    expect(screen.getByText("5")).toBeInTheDocument();
    expect(screen.getByText("Test Supplier")).toBeInTheDocument();
    expect(screen.getByText("Test note for product")).toBeInTheDocument();
  });

  it("handles missing optional fields", () => {
    const minimalItem = {
      productCode: "TEST-002",
      productName: "Minimal Product",
      type: ProductType.Material,
    };

    render(<ProductBasicInfo item={minimalItem} />);

    expect(screen.getByText("Základní informace")).toBeInTheDocument();
    expect(screen.getByText("Materiál")).toBeInTheDocument();
    expect(screen.getAllByText("Není uvedeno")).toHaveLength(4); // location, minOrder, minManuf, supplier
  });
});
