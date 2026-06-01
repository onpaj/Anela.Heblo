import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import PhotoViewToggle from "../PhotoViewToggle";

const options = [
  { value: "tiles", icon: <span>grid</span>, label: "Dlaždice" },
  { value: "list", icon: <span>list</span>, label: "Seznam" },
  { value: "table", icon: <span>table</span>, label: "Tabulka" },
];

function renderToggle(value: string, onChange = jest.fn()) {
  return { ...render(<PhotoViewToggle options={options} value={value} onChange={onChange} />), onChange };
}

describe("PhotoViewToggle", () => {
  afterEach(() => {
    jest.clearAllMocks();
  });

  test("renders a button for each option", () => {
    // Arrange & Act
    renderToggle("tiles");

    // Assert
    expect(screen.getAllByRole("button")).toHaveLength(3);
  });

  test("active option button has aria-pressed true", () => {
    // Arrange & Act
    renderToggle("list");

    // Assert
    expect(screen.getByTitle("Seznam")).toHaveAttribute("aria-pressed", "true");
  });

  test("inactive option buttons have aria-pressed false", () => {
    // Arrange & Act
    renderToggle("list");

    // Assert
    expect(screen.getByTitle("Dlaždice")).toHaveAttribute("aria-pressed", "false");
    expect(screen.getByTitle("Tabulka")).toHaveAttribute("aria-pressed", "false");
  });

  test("clicking an inactive button fires onChange with correct value", () => {
    // Arrange
    const { onChange } = renderToggle("tiles");

    // Act
    fireEvent.click(screen.getByTitle("Seznam"));

    // Assert
    expect(onChange).toHaveBeenCalledWith("list");
  });

  test("clicking the active button calls onChange with the same value", () => {
    // Arrange
    const { onChange } = renderToggle("tiles");

    // Act
    fireEvent.click(screen.getByTitle("Dlaždice"));

    // Assert — component does not guard against re-calling; caller is idempotent
    expect(onChange).toHaveBeenCalledWith("tiles");
  });

  test("each button has a title attribute matching option.label", () => {
    // Arrange & Act
    renderToggle("tiles");

    // Assert
    expect(screen.getByTitle("Dlaždice")).toBeInTheDocument();
    expect(screen.getByTitle("Seznam")).toBeInTheDocument();
    expect(screen.getByTitle("Tabulka")).toBeInTheDocument();
  });
});
