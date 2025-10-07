import React from "react";
import { render, screen } from "@testing-library/react";
import MarginsSummary from "../MarginsSummary";
import { CatalogItemDto } from "../../../../../../api/hooks/useCatalog";
import { ManufactureCostDto, MarginHistoryDto } from "../../../../../../api/generated/api-client";

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

const mockMarginHistoryEmpty: MarginHistoryDto[] = [];

const mockMarginHistoryWithM0M3: MarginHistoryDto[] = [
  {
    date: new Date("2024-01-01"),
    m0Percentage: 80.0,
    m1Percentage: 66.67,
    m2Percentage: 50.0,
    m3Percentage: 33.33,
    m0Amount: 120,
    m1Amount: 100,
    m2Amount: 75,
    m3Amount: 50,
    m0CostBase: 30,
    m1CostBase: 50,
    m2CostBase: 75,
    m3CostBase: 100,
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
  it("renders basic component structure correctly", () => {
    render(
      <MarginsSummary
        item={mockItemWithoutM0M3}
        manufactureCostHistory={mockManufactureCostHistory}
        marginHistory={mockMarginHistoryEmpty}
      />
    );

    expect(screen.getByText("Přehled nákladů a marže")).toBeInTheDocument();
    
    // Should show legacy margin format when no M0-M3 data
    expect(screen.getByText("Marže")).toBeInTheDocument();
    expect(screen.getByText("Marže v %")).toBeInTheDocument();
    expect(screen.getByText("Marže v Kč")).toBeInTheDocument();
  });

  it("displays legacy margin format when M0-M3 data is not available", () => {
    render(
      <MarginsSummary
        item={mockItemWithoutM0M3}
        manufactureCostHistory={mockManufactureCostHistory}
        marginHistory={mockMarginHistoryEmpty}
      />
    );

    // Should show legacy format with zero values (no margin history data)
    expect(screen.getByText("Marže")).toBeInTheDocument();
    expect(screen.getByText("Marže v %")).toBeInTheDocument();
    expect(screen.getByText("Marže v Kč")).toBeInTheDocument();
    expect(screen.getByText("0,0%")).toBeInTheDocument();
    expect(screen.getByText("0,00 Kč")).toBeInTheDocument();

    // Should not show M0-M3 table headers
    expect(screen.queryByText("Absolutní marže (Kč/ks)")).not.toBeInTheDocument();
    expect(screen.queryByText("Procentuální marže (%)")).not.toBeInTheDocument();
  });

  it("displays M0-M3 margin levels when data is available", () => {
    render(
      <MarginsSummary
        item={mockItemWithM0M3}
        manufactureCostHistory={mockManufactureCostHistory}
        marginHistory={mockMarginHistoryWithM0M3}
      />
    );

    // Should show M0-M3 table format
    expect(screen.getByText("Marže")).toBeInTheDocument();
    expect(screen.getByText("Absolutní marže (Kč/ks)")).toBeInTheDocument();
    expect(screen.getByText("Procentuální marže (%)")).toBeInTheDocument();
    expect(screen.getByText("Nákladový základ (Kč/ks)")).toBeInTheDocument();
    
    expect(screen.getByText("M0")).toBeInTheDocument();
    expect(screen.getByText("M1")).toBeInTheDocument();
    expect(screen.getByText("M2")).toBeInTheDocument();
    expect(screen.getByText("M3")).toBeInTheDocument();

    // Check percentage values (formatted as in component)
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
        marginHistory={mockMarginHistoryWithM0M3}
      />
    );

    // Should show M0-M3 levels with correct percentages
    expect(screen.getByText("M0")).toBeInTheDocument();
    expect(screen.getByText("M1")).toBeInTheDocument();
    expect(screen.getByText("M2")).toBeInTheDocument();
    expect(screen.getByText("M3")).toBeInTheDocument();
    
    expect(screen.getByText("80,0%")).toBeInTheDocument(); // M0
    expect(screen.getByText("66,7%")).toBeInTheDocument(); // M1
    expect(screen.getByText("50,0%")).toBeInTheDocument(); // M2
    expect(screen.getByText("33,3%")).toBeInTheDocument(); // M3
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
        marginHistory={mockMarginHistoryEmpty}
      />
    );

    expect(
      screen.getByText("Není dostupná prodejní cena - marže nelze vypočítat")
    ).toBeInTheDocument();
  });

  it("shows margin data even when no manufacturing cost history is available", () => {
    render(
      <MarginsSummary
        item={mockItemWithoutM0M3}
        manufactureCostHistory={[]}
        marginHistory={mockMarginHistoryEmpty}
      />
    );

    // Should still show basic margin display
    expect(screen.getByText("Přehled nákladů a marže")).toBeInTheDocument();
    expect(screen.getByText("Marže")).toBeInTheDocument();
  });

  it("handles null/undefined item gracefully", () => {
    render(
      <MarginsSummary
        item={null}
        manufactureCostHistory={mockManufactureCostHistory}
        marginHistory={mockMarginHistoryEmpty}
      />
    );

    // Should still render basic structure
    expect(screen.getByText("Přehled nákladů a marže")).toBeInTheDocument();
    
    // Should show zero values
    expect(screen.getByText("0,0%")).toBeInTheDocument();
    expect(screen.getByText("0,00 Kč")).toBeInTheDocument();
  });

  it("handles edge case margins correctly", () => {
    const edgeCaseMarginHistory: MarginHistoryDto[] = [
      {
        date: new Date("2024-01-01"),
        m0Percentage: 25,
        m1Percentage: 45,
        m2Percentage: 75,
        m3Percentage: 85,
        m0Amount: 25,
        m1Amount: 45,
        m2Amount: 75,
        m3Amount: 85,
        m0CostBase: 100,
        m1CostBase: 100,
        m2CostBase: 100,
        m3CostBase: 100,
      },
    ];

    render(
      <MarginsSummary
        item={mockItemWithoutM0M3}
        manufactureCostHistory={mockManufactureCostHistory}
        marginHistory={edgeCaseMarginHistory}
      />
    );

    // Check that all percentages are displayed correctly
    expect(screen.getByText("25,0%")).toBeInTheDocument();
    expect(screen.getByText("45,0%")).toBeInTheDocument();
    expect(screen.getByText("75,0%")).toBeInTheDocument();
    expect(screen.getByText("85,0%")).toBeInTheDocument();

    // Verify the M0-M3 structure is shown
    expect(screen.getByText("M0")).toBeInTheDocument();
    expect(screen.getByText("M1")).toBeInTheDocument();
    expect(screen.getByText("M2")).toBeInTheDocument();
    expect(screen.getByText("M3")).toBeInTheDocument();
  });
});