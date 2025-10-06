import React from "react";
import { render, screen } from "@testing-library/react";
import MarginsSummary from "../MarginsSummary";
import { CatalogItemDto } from "../../../../../../api/hooks/useCatalog";
import { ManufactureCostDto } from "../../../../../../api/generated/api-client";

const mockManufactureCostHistory: ManufactureCostDto[] = [
  {
    date: new Date("2024-01-01"),
    materialCost: 30,
    handlingCost: 20,
    total: 50,
  },
  {
    date: new Date("2024-02-01"),
    materialCost: 35,
    handlingCost: 25,
    total: 60,
  },
];

const mockItemWithoutM0M3: CatalogItemDto = {
  productCode: "PROD001",
  productName: "Test Product",
  price: {
    eshopPrice: {
      priceWithoutVat: 150,
      priceWithVat: 181.5,
    },
  },
  marginPercentage: 33.33,
  marginAmount: 50,
} as CatalogItemDto;

const mockItemWithM0M3 = {
  ...mockItemWithoutM0M3,
  m0Percentage: 80.0,
  m1Percentage: 66.67,
  m2Percentage: 50.0,
  m3Percentage: 33.33,
} as any;

describe("MarginsSummary", () => {
  it("renders cost breakdown correctly", () => {
    render(
      <MarginsSummary
        item={mockItemWithoutM0M3}
        manufactureCostHistory={mockManufactureCostHistory}
      />
    );

    expect(screen.getByText("Přehled nákladů a marže")).toBeInTheDocument();
    
    // Check average material cost calculation: (30 + 35) / 2 = 32.5
    expect(screen.getByText("32,50")).toBeInTheDocument();
    
    // Check average handling cost calculation: (20 + 25) / 2 = 22.5
    expect(screen.getByText("22,50")).toBeInTheDocument();
    
    // Check average total cost calculation: (50 + 60) / 2 = 55
    expect(screen.getByText("55,00")).toBeInTheDocument();
  });

  it("displays legacy margin format when M0-M3 data is not available", () => {
    render(
      <MarginsSummary
        item={mockItemWithoutM0M3}
        manufactureCostHistory={mockManufactureCostHistory}
      />
    );

    // Should show legacy format
    expect(screen.getByText("Marže")).toBeInTheDocument();
    expect(screen.getByText("Marže v %")).toBeInTheDocument();
    expect(screen.getByText("Marže v Kč")).toBeInTheDocument();
    expect(screen.getByText("33,3%")).toBeInTheDocument();
    expect(screen.getByText("50,00 Kč")).toBeInTheDocument();

    // Should not show M0-M3 levels
    expect(screen.queryByText("Úrovně marže")).not.toBeInTheDocument();
    expect(screen.queryByText("M0 - Materiál")).not.toBeInTheDocument();
  });

  it("displays M0-M3 margin levels when data is available", () => {
    render(
      <MarginsSummary
        item={mockItemWithM0M3}
        manufactureCostHistory={mockManufactureCostHistory}
      />
    );

    // Should show M0-M3 format
    expect(screen.getByText("Úrovně marže")).toBeInTheDocument();
    expect(screen.getByText("M0 - Materiál")).toBeInTheDocument();
    expect(screen.getByText("M1 - + Výroba")).toBeInTheDocument();
    expect(screen.getByText("M2 - + Prodej")).toBeInTheDocument();
    expect(screen.getByText("M3 - Celkem")).toBeInTheDocument();

    // Check percentage values
    expect(screen.getByText("80,0%")).toBeInTheDocument(); // M0
    expect(screen.getByText("66,7%")).toBeInTheDocument(); // M1
    expect(screen.getByText("50,0%")).toBeInTheDocument(); // M2
    expect(screen.getByText("33,3%")).toBeInTheDocument(); // M3

    // Should not show legacy format
    expect(screen.queryByText("Marže v %")).not.toBeInTheDocument();
    expect(screen.queryByText("Marže v Kč")).not.toBeInTheDocument();
  });

  it("applies correct color coding for margin levels", () => {
    render(
      <MarginsSummary
        item={mockItemWithM0M3}
        manufactureCostHistory={mockManufactureCostHistory}
      />
    );

    // M0 (80%) should be green (>= 80%)
    expect(screen.getByText("80,0%")).toBeInTheDocument();
    expect(screen.getByText("základní marže")).toBeInTheDocument();

    // M1 (66.67%) should be yellow (50-80%)
    expect(screen.getByText("66,7%")).toBeInTheDocument();
    expect(screen.getByText("s výrobou")).toBeInTheDocument();

    // M2 (50%) should be yellow (50-80%)
    expect(screen.getByText("50,0%")).toBeInTheDocument();
    expect(screen.getByText("s prodejem")).toBeInTheDocument();

    // M3 (33.33%) should be orange (30-50%)
    expect(screen.getByText("33,3%")).toBeInTheDocument();
    expect(screen.getByText("finální marže")).toBeInTheDocument();
  });

  it("shows warning when no selling price is available", () => {
    const itemWithoutPrice = {
      ...mockItemWithoutM0M3,
      price: {
        eshopPrice: {
          priceWithoutVat: 0,
          priceWithVat: 0,
        },
      },
    } as CatalogItemDto;

    render(
      <MarginsSummary
        item={itemWithoutPrice}
        manufactureCostHistory={mockManufactureCostHistory}
      />
    );

    expect(
      screen.getByText("Není dostupná prodejní cena - marže nelze vypočítat")
    ).toBeInTheDocument();
  });

  it("shows message when no manufacturing cost history is available", () => {
    render(
      <MarginsSummary
        item={mockItemWithoutM0M3}
        manufactureCostHistory={[]}
      />
    );

    expect(
      screen.getByText("Žádná data o nákladech za posledních 13 měsíců")
    ).toBeInTheDocument();
  });

  it("handles null/undefined item gracefully", () => {
    render(
      <MarginsSummary
        item={null}
        manufactureCostHistory={mockManufactureCostHistory}
      />
    );

    // Should still render basic structure
    expect(screen.getByText("Přehled nákladů a marže")).toBeInTheDocument();
    
    // Should show zero values
    expect(screen.getByText("0,0%")).toBeInTheDocument();
    expect(screen.getByText("0,00 Kč")).toBeInTheDocument();
  });

  it("handles edge case margins correctly", () => {
    const itemWithEdgeCaseMargins = {
      ...mockItemWithoutM0M3,
      m0Percentage: 25, // Should be red (< 30%)
      m1Percentage: 45, // Should be orange (30-50%)
      m2Percentage: 75, // Should be yellow (50-80%)
      m3Percentage: 85, // Should be green (>= 80%)
    } as any;

    render(
      <MarginsSummary
        item={itemWithEdgeCaseMargins}
        manufactureCostHistory={mockManufactureCostHistory}
      />
    );

    // Check that all percentages are displayed correctly
    expect(screen.getByText("25,0%")).toBeInTheDocument();
    expect(screen.getByText("45,0%")).toBeInTheDocument();
    expect(screen.getByText("75,0%")).toBeInTheDocument();
    expect(screen.getByText("85,0%")).toBeInTheDocument();

    // Verify the M0-M3 structure is shown
    expect(screen.getByText("Úrovně marže")).toBeInTheDocument();
    expect(screen.getByText("M0 - Materiál")).toBeInTheDocument();
    expect(screen.getByText("M1 - + Výroba")).toBeInTheDocument();
    expect(screen.getByText("M2 - + Prodej")).toBeInTheDocument();
    expect(screen.getByText("M3 - Celkem")).toBeInTheDocument();
  });
});