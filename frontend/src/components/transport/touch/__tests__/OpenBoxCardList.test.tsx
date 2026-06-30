import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import OpenBoxCardList from "../OpenBoxCardList";
import { useTransportBoxesQuery } from "../../../../api/hooks/useTransportBoxes";

jest.mock("../../../../api/hooks/useTransportBoxes", () => ({
  useTransportBoxesQuery: jest.fn(),
}));

const mockUseTransportBoxesQuery = useTransportBoxesQuery as jest.Mock;

describe("OpenBoxCardList", () => {
  const onOpenBox = jest.fn();

  beforeEach(() => {
    jest.clearAllMocks();
  });

  it("renders a card for each open box and opens it on tap", () => {
    mockUseTransportBoxesQuery.mockReturnValue({
      data: {
        items: [
          { id: 1, code: "B001", state: "Opened", itemCount: 2 },
          { id: 2, code: "B002", state: "Opened", itemCount: 0 },
        ],
      },
      isLoading: false,
      error: null,
    });

    render(<OpenBoxCardList onOpenBox={onOpenBox} />);

    expect(screen.getByText("B001")).toBeInTheDocument();
    expect(screen.getByText("B002")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /B001/ }));
    expect(onOpenBox).toHaveBeenCalledWith(1);
  });

  it("shows an empty state when there are no open boxes", () => {
    mockUseTransportBoxesQuery.mockReturnValue({
      data: { items: [] },
      isLoading: false,
      error: null,
    });

    render(<OpenBoxCardList onOpenBox={onOpenBox} />);

    expect(screen.getByText(/Žádný otevřený box/i)).toBeInTheDocument();
  });

  it("shows a loading state while fetching", () => {
    mockUseTransportBoxesQuery.mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null,
    });

    render(<OpenBoxCardList onOpenBox={onOpenBox} />);

    expect(screen.getByText(/Načítání otevřených boxů/i)).toBeInTheDocument();
  });
});
