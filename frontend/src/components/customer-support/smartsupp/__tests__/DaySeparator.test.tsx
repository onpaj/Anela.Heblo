import React from "react";
import { render, screen } from "@testing-library/react";
import DaySeparator from "../DaySeparator";

describe("DaySeparator", () => {
  it("renders 'Dnes' for today's date", () => {
    const today = new Date();
    render(<DaySeparator date={today.toISOString()} />);
    expect(screen.getByTestId("day-separator")).toHaveTextContent("Dnes");
  });

  it("renders 'Včera' for yesterday's date", () => {
    const yesterday = new Date();
    yesterday.setDate(yesterday.getDate() - 1);
    render(<DaySeparator date={yesterday.toISOString()} />);
    expect(screen.getByTestId("day-separator")).toHaveTextContent("Včera");
  });

  it("renders a locale date for dates older than yesterday", () => {
    const old = new Date("2025-01-15T10:00:00Z");
    render(<DaySeparator date={old.toISOString()} />);
    const text = screen.getByTestId("day-separator").textContent ?? "";
    expect(text).toMatch(/2025|leden/i);
  });
});
