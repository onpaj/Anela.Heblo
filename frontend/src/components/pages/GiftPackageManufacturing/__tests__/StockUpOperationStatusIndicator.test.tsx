import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { BrowserRouter } from "react-router-dom";
import StockUpOperationStatusIndicator from "../StockUpOperationStatusIndicator";
import { GetStockUpOperationsSummaryResponse } from "../../../../api/generated/api-client";

const mockNavigate = jest.fn();

jest.mock("react-router-dom", () => ({
  ...jest.requireActual("react-router-dom"),
  useNavigate: () => mockNavigate,
}));

describe("StockUpOperationStatusIndicator", () => {
  beforeEach(() => {
    mockNavigate.mockClear();
  });

  const renderComponent = (summary: GetStockUpOperationsSummaryResponse) => {
    return render(
      <BrowserRouter>
        <StockUpOperationStatusIndicator summary={summary} />
      </BrowserRouter>
    );
  };

  it("should display in-queue count when totalInQueue > 0", () => {
    const summary: GetStockUpOperationsSummaryResponse = {
      success: true,
      pendingCount: 2,
      submittedCount: 1,
      failedCount: 0,
      totalInQueue: 3,
    };

    renderComponent(summary);

    expect(screen.getByTestId("queue-indicator")).toBeInTheDocument();
    expect(screen.getByText("3 operací ve frontě")).toBeInTheDocument();
  });

  it("should display failed count when failedCount > 0", () => {
    const summary: GetStockUpOperationsSummaryResponse = {
      success: true,
      pendingCount: 0,
      submittedCount: 0,
      failedCount: 2,
      totalInQueue: 0,
    };

    renderComponent(summary);

    expect(screen.getByTestId("failed-indicator")).toBeInTheDocument();
    expect(screen.getByText("2 selhalo")).toBeInTheDocument();
  });

  it("should display both indicators when both counts > 0", () => {
    const summary: GetStockUpOperationsSummaryResponse = {
      success: true,
      pendingCount: 1,
      submittedCount: 2,
      failedCount: 3,
      totalInQueue: 3,
    };

    renderComponent(summary);

    expect(screen.getByTestId("queue-indicator")).toBeInTheDocument();
    expect(screen.getByTestId("failed-indicator")).toBeInTheDocument();
    expect(screen.getByText("3 operací ve frontě")).toBeInTheDocument();
    expect(screen.getByText("3 selhalo")).toBeInTheDocument();
  });

  it("should navigate to stock-up operations page on click", () => {
    const summary: GetStockUpOperationsSummaryResponse = {
      success: true,
      pendingCount: 1,
      submittedCount: 0,
      failedCount: 0,
      totalInQueue: 1,
    };

    renderComponent(summary);

    const indicator = screen.getByTestId("stockup-status-indicator");
    fireEvent.click(indicator);

    expect(mockNavigate).toHaveBeenCalledWith(
      "/stock-up-operations?sourceType=GiftPackageManufacture&state=Pending,Submitted,Failed"
    );
  });
});
