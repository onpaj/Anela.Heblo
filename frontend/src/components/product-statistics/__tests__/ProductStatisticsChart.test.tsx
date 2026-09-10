import React from "react";
import { render, screen } from "@testing-library/react";
import ProductStatisticsChart from "../ProductStatisticsChart";

const capturedProps: any = { current: null };

jest.mock("react-chartjs-2", () => ({
  Line: (props: any) => {
    capturedProps.current = props;
    return <div data-testid="line-chart" />;
  },
}));

describe("ProductStatisticsChart", () => {
  beforeEach(() => {
    capturedProps.current = null;
  });

  test("renders one dataset per product", () => {
    render(
      <ProductStatisticsChart
        months={["2025-01", "2025-02"]}
        series={[
          { productCode: "PROD-A", productName: "Krém", values: [1, 2] },
          { productCode: "PROD-B", productName: "Mýdlo", values: [3, 4] },
        ]}
        yAxisLabel="Kusů prodáno"
      />,
    );

    expect(screen.getByTestId("line-chart")).toBeInTheDocument();
    expect(capturedProps.current.data.datasets).toHaveLength(2);
  });

  test("uses the response months as chart labels", () => {
    render(
      <ProductStatisticsChart
        months={["2024-11", "2024-12", "2025-01"]}
        series={[{ productCode: "PROD-A", productName: "Krém", values: [1, 2, 3] }]}
        yAxisLabel="Kusů prodáno"
      />,
    );

    expect(capturedProps.current.data.labels).toEqual([
      "2024-11",
      "2024-12",
      "2025-01",
    ]);
  });

  test("labels each dataset with the product name and code", () => {
    render(
      <ProductStatisticsChart
        months={["2025-01"]}
        series={[{ productCode: "PROD-A", productName: "Krém", values: [1] }]}
        yAxisLabel="Kusů prodáno"
      />,
    );

    expect(capturedProps.current.data.datasets[0].label).toBe("Krém (PROD-A)");
  });

  test("gives each product a distinct border color", () => {
    render(
      <ProductStatisticsChart
        months={["2025-01"]}
        series={[
          { productCode: "PROD-A", productName: "Krém", values: [1] },
          { productCode: "PROD-B", productName: "Mýdlo", values: [2] },
        ]}
        yAxisLabel="Kusů prodáno"
      />,
    );

    const [first, second] = capturedProps.current.data.datasets;
    expect(first.borderColor).not.toBe(second.borderColor);
  });

  test("shows the empty state when no products are selected", () => {
    render(
      <ProductStatisticsChart months={[]} series={[]} yAxisLabel="Kusů prodáno" />,
    );

    expect(screen.getByText("Žádná data pro zobrazení grafu")).toBeInTheDocument();
    expect(screen.queryByTestId("line-chart")).not.toBeInTheDocument();
  });

  test("shows the empty state when every series is all zero", () => {
    render(
      <ProductStatisticsChart
        months={["2025-01", "2025-02"]}
        series={[{ productCode: "PROD-A", productName: "Krém", values: [0, 0] }]}
        yAxisLabel="Kusů prodáno"
      />,
    );

    expect(screen.getByText("Žádná data pro zobrazení grafu")).toBeInTheDocument();
  });

  test("applies the given y-axis label", () => {
    render(
      <ProductStatisticsChart
        months={["2025-01"]}
        series={[{ productCode: "PROD-A", productName: "Krém", values: [5] }]}
        yAxisLabel="Kusů vyrobeno"
      />,
    );

    expect(capturedProps.current.options.scales.y.title.text).toBe(
      "Kusů vyrobeno",
    );
  });
});
