import React from "react";
import { render, screen } from "@testing-library/react";
import StatusPill from "../StatusPill";

describe("StatusPill", () => {
  it("renders 'Aktivní' for status 'open' with emerald colors", () => {
    render(<StatusPill status="open" />);
    const pill = screen.getByTestId("status-pill");
    expect(pill).toHaveTextContent("Aktivní");
    expect(pill).toHaveClass("bg-emerald-50");
    expect(pill).toHaveClass("text-emerald-700");
  });

  it("renders 'Čeká' for status 'pending' with amber colors", () => {
    render(<StatusPill status="pending" />);
    const pill = screen.getByTestId("status-pill");
    expect(pill).toHaveTextContent("Čeká");
    expect(pill).toHaveClass("bg-amber-50");
    expect(pill).toHaveClass("text-amber-700");
  });

  it("renders 'Vyřešeno' for status 'closed' with slate colors", () => {
    render(<StatusPill status="closed" />);
    const pill = screen.getByTestId("status-pill");
    expect(pill).toHaveTextContent("Vyřešeno");
    expect(pill).toHaveClass("bg-slate-100");
    expect(pill).toHaveClass("text-slate-600");
  });

  it("is case-insensitive on status (handles 'Open' from backend enum.ToString())", () => {
    render(<StatusPill status="Open" />);
    expect(screen.getByTestId("status-pill")).toHaveTextContent("Aktivní");
  });

  it("falls back to slate pill with the raw status label for unknown values", () => {
    render(<StatusPill status="weird" />);
    const pill = screen.getByTestId("status-pill");
    expect(pill).toHaveTextContent("weird");
    expect(pill).toHaveClass("bg-slate-100");
  });
});
