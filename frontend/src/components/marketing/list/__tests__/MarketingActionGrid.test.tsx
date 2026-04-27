import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import MarketingActionGrid from "../MarketingActionGrid";
import type { MarketingActionDto } from "../MarketingActionGrid";

const defaultProps = {
  actions: [],
  totalPages: 1,
  pageNumber: 1,
  onPageChange: jest.fn(),
  onActionClick: jest.fn(),
};

const sampleActions: MarketingActionDto[] = [
  {
    id: 1,
    title: "Letní kampaň",
    actionType: "Campaign",
    dateFrom: "2026-06-01",
    dateTo: "2026-06-30",
    associatedProducts: ["AKL001", "AKL002"],
  },
  {
    id: 2,
    title: "Email newsletter",
    actionType: "Launch",
    dateFrom: "2026-07-01",
    dateTo: "2026-07-15",
    associatedProducts: [],
  },
];

beforeEach(() => {
  jest.clearAllMocks();
});

// ─── Loading state ────────────────────────────────────────────────────────────

describe("MarketingActionGrid — loading", () => {
  it("shows loading message when isLoading is true", () => {
    render(<MarketingActionGrid {...defaultProps} isLoading={true} />);
    expect(screen.getByText("Načítání...")).toBeInTheDocument();
  });

  it("does not render a table when loading", () => {
    render(<MarketingActionGrid {...defaultProps} isLoading={true} />);
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });
});

// ─── Empty state ──────────────────────────────────────────────────────────────

describe("MarketingActionGrid — empty state", () => {
  it("shows empty message when actions array is empty", () => {
    render(<MarketingActionGrid {...defaultProps} />);
    expect(
      screen.getByText("Žádné marketingové akce nebyly nalezeny."),
    ).toBeInTheDocument();
  });

  it("does not render a table when empty", () => {
    render(<MarketingActionGrid {...defaultProps} />);
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });
});

// ─── Table rendering ──────────────────────────────────────────────────────────

describe("MarketingActionGrid — table", () => {
  it("renders a row for each action", () => {
    render(<MarketingActionGrid {...defaultProps} actions={sampleActions} />);
    expect(screen.getByText("Letní kampaň")).toBeInTheDocument();
    expect(screen.getByText("Email newsletter")).toBeInTheDocument();
  });

  it("renders column headers", () => {
    render(<MarketingActionGrid {...defaultProps} actions={sampleActions} />);
    expect(screen.getByText("Název")).toBeInTheDocument();
    expect(screen.getByText("Typ")).toBeInTheDocument();
    expect(screen.getByText("Od")).toBeInTheDocument();
    expect(screen.getByText("Do")).toBeInTheDocument();
    expect(screen.getByText("Produkty")).toBeInTheDocument();
  });

  it("shows Czech label for Campaign action type", () => {
    render(<MarketingActionGrid {...defaultProps} actions={sampleActions} />);
    expect(screen.getByText("PR")).toBeInTheDocument();
  });

  it("shows Czech label for Launch action type", () => {
    render(<MarketingActionGrid {...defaultProps} actions={sampleActions} />);
    expect(screen.getByText("Email")).toBeInTheDocument();
  });

  it("falls back to Other badge for unknown action type", () => {
    const action: MarketingActionDto = {
      id: 99,
      title: "Neznámý typ",
      actionType: "Unknown",
      dateFrom: "2026-01-01",
      dateTo: "2026-01-31",
    };
    render(<MarketingActionGrid {...defaultProps} actions={[action]} />);
    // Falls back to raw actionType string
    expect(screen.getByText("Unknown")).toBeInTheDocument();
  });

  it("shows comma-separated associated products", () => {
    render(<MarketingActionGrid {...defaultProps} actions={sampleActions} />);
    expect(screen.getByText("AKL001, AKL002")).toBeInTheDocument();
  });

  it("shows dash when no associated products", () => {
    render(<MarketingActionGrid {...defaultProps} actions={sampleActions} />);
    // The second action has no products → should show "—"
    const dashes = screen.getAllByText("—");
    expect(dashes.length).toBeGreaterThanOrEqual(1);
  });

  it("calls onActionClick with action id on row click", () => {
    const onActionClick = jest.fn();
    render(
      <MarketingActionGrid
        {...defaultProps}
        actions={sampleActions}
        onActionClick={onActionClick}
      />,
    );
    fireEvent.click(screen.getByText("Letní kampaň"));
    expect(onActionClick).toHaveBeenCalledWith(1);
  });

  it("calls onActionClick with second row id", () => {
    const onActionClick = jest.fn();
    render(
      <MarketingActionGrid
        {...defaultProps}
        actions={sampleActions}
        onActionClick={onActionClick}
      />,
    );
    fireEvent.click(screen.getByText("Email newsletter"));
    expect(onActionClick).toHaveBeenCalledWith(2);
  });
});

// ─── Pagination ───────────────────────────────────────────────────────────────

describe("MarketingActionGrid — pagination", () => {
  it("does not render pagination when totalPages is 1", () => {
    render(
      <MarketingActionGrid {...defaultProps} actions={sampleActions} totalPages={1} />,
    );
    expect(screen.queryByRole("button", { name: /chevron/i })).not.toBeInTheDocument();
  });

  it("renders pagination when totalPages > 1", () => {
    render(
      <MarketingActionGrid
        {...defaultProps}
        actions={sampleActions}
        totalPages={3}
        pageNumber={2}
      />,
    );
    expect(screen.getByText("2 / 3")).toBeInTheDocument();
  });

  it("disables prev button on first page", () => {
    render(
      <MarketingActionGrid
        {...defaultProps}
        actions={sampleActions}
        totalPages={3}
        pageNumber={1}
      />,
    );
    const buttons = screen.getAllByRole("button");
    const prevBtn = buttons.find((b) => b.querySelector("svg"));
    expect(prevBtn).toBeDisabled();
  });

  it("disables next button on last page", () => {
    render(
      <MarketingActionGrid
        {...defaultProps}
        actions={sampleActions}
        totalPages={3}
        pageNumber={3}
      />,
    );
    const buttons = screen.getAllByRole("button");
    const nextBtn = buttons[buttons.length - 1];
    expect(nextBtn).toBeDisabled();
  });

  it("calls onPageChange with page - 1 when prev is clicked", () => {
    const onPageChange = jest.fn();
    render(
      <MarketingActionGrid
        {...defaultProps}
        actions={sampleActions}
        totalPages={3}
        pageNumber={2}
        onPageChange={onPageChange}
      />,
    );
    const buttons = screen.getAllByRole("button");
    fireEvent.click(buttons[0]);
    expect(onPageChange).toHaveBeenCalledWith(1);
  });

  it("calls onPageChange with page + 1 when next is clicked", () => {
    const onPageChange = jest.fn();
    render(
      <MarketingActionGrid
        {...defaultProps}
        actions={sampleActions}
        totalPages={3}
        pageNumber={2}
        onPageChange={onPageChange}
      />,
    );
    const buttons = screen.getAllByRole("button");
    fireEvent.click(buttons[buttons.length - 1]);
    expect(onPageChange).toHaveBeenCalledWith(3);
  });
});

// ─── OutlookSyncStatus badge ──────────────────────────────────────────────────

describe("MarketingActionGrid — OutlookSyncStatus badge", () => {
  it("renders red dot badge when outlookSyncStatus is 'Failed'", () => {
    const action: MarketingActionDto = {
      id: 10,
      title: "Akce se selháním",
      actionType: "General",
      dateFrom: "2026-03-01",
      dateTo: "2026-03-15",
      outlookSyncStatus: "Failed",
    };
    render(<MarketingActionGrid {...defaultProps} actions={[action]} />);
    const badge = screen.getByTitle(
      "Synchronizace s Outlookem selhala – bude opakována",
    );
    expect(badge).toBeInTheDocument();
    expect(badge.classList.contains("bg-red-500")).toBe(true);
  });

  it("does not render red dot badge when outlookSyncStatus is 'Synced'", () => {
    const action: MarketingActionDto = {
      id: 11,
      title: "Synchronizovaná akce",
      actionType: "General",
      dateFrom: "2026-03-01",
      dateTo: "2026-03-15",
      outlookSyncStatus: "Synced",
    };
    render(<MarketingActionGrid {...defaultProps} actions={[action]} />);
    expect(
      screen.queryByTitle("Synchronizace s Outlookem selhala – bude opakována"),
    ).not.toBeInTheDocument();
  });

  it("does not render red dot badge when outlookSyncStatus is undefined", () => {
    const action: MarketingActionDto = {
      id: 12,
      title: "Akce bez statusu",
      actionType: "General",
      dateFrom: "2026-03-01",
      dateTo: "2026-03-15",
    };
    render(<MarketingActionGrid {...defaultProps} actions={[action]} />);
    expect(
      screen.queryByTitle("Synchronizace s Outlookem selhala – bude opakována"),
    ).not.toBeInTheDocument();
  });
});
