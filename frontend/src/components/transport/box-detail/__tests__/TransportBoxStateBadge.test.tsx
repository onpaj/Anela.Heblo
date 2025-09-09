import React from "react";
import { render, screen } from "@testing-library/react";
import TransportBoxStateBadge from "../components/TransportBoxStateBadge";

describe("TransportBoxStateBadge", () => {
  it("renders with correct label for known state", () => {
    render(<TransportBoxStateBadge state="New" />);
    expect(screen.getByText("Nový")).toBeInTheDocument();
  });

  it("renders with original state for unknown state", () => {
    render(<TransportBoxStateBadge state="Unknown" />);
    expect(screen.getByText("Unknown")).toBeInTheDocument();
  });

  it("applies correct CSS classes for different sizes", () => {
    const { rerender } = render(
      <TransportBoxStateBadge state="New" size="sm" />,
    );
    expect(screen.getByText("Nový")).toHaveClass("px-2", "py-0.5", "text-xs");

    rerender(<TransportBoxStateBadge state="New" size="lg" />);
    expect(screen.getByText("Nový")).toHaveClass("px-3", "py-1", "text-sm");
  });

  it("applies correct color classes for different states", () => {
    render(<TransportBoxStateBadge state="New" />);
    expect(screen.getByText("Nový")).toHaveClass(
      "bg-gray-100",
      "text-gray-800",
    );
  });

  it("uses medium size by default", () => {
    render(<TransportBoxStateBadge state="New" />);
    expect(screen.getByText("Nový")).toHaveClass("px-2.5", "py-0.5", "text-xs");
  });
});
