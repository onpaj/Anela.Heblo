import React from "react";
import { render, screen, fireEvent, act } from "@testing-library/react";
import TagSidebar from "../TagSidebar";
import type { TagWithCountDto } from "../../../../api/hooks/usePhotobank";

const mockTags: TagWithCountDto[] = [
  { id: 1, name: "výroba", count: 10 },
  { id: 2, name: "marketing", count: 5 },
  { id: 3, name: "produkty", count: 3 },
];

function renderSidebar(overrides: Partial<React.ComponentProps<typeof TagSidebar>> = {}) {
  const defaults: React.ComponentProps<typeof TagSidebar> = {
    tags: mockTags,
    selectedTagIds: [],
    search: "",
    onTagToggle: jest.fn(),
    onSearchChange: jest.fn(),
    onClearFilters: jest.fn(),
    ...overrides,
  };
  return { ...render(<TagSidebar {...defaults} />), props: defaults };
}

describe("TagSidebar", () => {
  beforeEach(() => {
    jest.useFakeTimers();
  });

  afterEach(() => {
    jest.useRealTimers();
    jest.clearAllMocks();
  });

  test("renders tag list with counts", () => {
    // Arrange & Act
    renderSidebar();

    // Assert
    expect(screen.getByText("výroba")).toBeInTheDocument();
    expect(screen.getByText("10")).toBeInTheDocument();
    expect(screen.getByText("marketing")).toBeInTheDocument();
    expect(screen.getByText("5")).toBeInTheDocument();
    expect(screen.getByText("produkty")).toBeInTheDocument();
    expect(screen.getByText("3")).toBeInTheDocument();
  });

  test("clicking a tag calls onTagToggle with tag id", () => {
    // Arrange
    const onTagToggle = jest.fn();
    renderSidebar({ onTagToggle });

    // Act
    fireEvent.click(screen.getByText("výroba"));

    // Assert
    expect(onTagToggle).toHaveBeenCalledWith(1);
  });

  test("selected tag has highlighted aria-pressed state", () => {
    // Arrange
    renderSidebar({ selectedTagIds: [2] });

    // Act — buttons contain tag name + count; match by accessible name pattern
    const marketingBtn = screen.getByRole("button", { name: /marketing/ });
    const výrobaBtn = screen.getByRole("button", { name: /výroba/ });

    // Assert
    expect(marketingBtn).toHaveAttribute("aria-pressed", "true");
    expect(výrobaBtn).toHaveAttribute("aria-pressed", "false");
  });

  test("clicking a selected tag calls onTagToggle to deselect it", () => {
    // Arrange
    const onTagToggle = jest.fn();
    renderSidebar({ selectedTagIds: [1], onTagToggle });

    // Act
    fireEvent.click(screen.getByText("výroba"));

    // Assert
    expect(onTagToggle).toHaveBeenCalledWith(1);
  });

  test("'Clear filters' button appears when tags are selected", () => {
    // Arrange
    renderSidebar({ selectedTagIds: [1] });

    // Assert
    expect(screen.getByText("Vymazat")).toBeInTheDocument();
  });

  test("'Clear filters' button appears when search is active", () => {
    // Arrange
    renderSidebar({ search: "foto" });

    // Assert
    expect(screen.getByText("Vymazat")).toBeInTheDocument();
  });

  test("'Clear filters' button is absent when no filters active", () => {
    // Arrange
    renderSidebar({ selectedTagIds: [], search: "" });

    // Assert
    expect(screen.queryByText("Vymazat")).not.toBeInTheDocument();
  });

  test("clicking 'Clear filters' calls onClearFilters", () => {
    // Arrange
    const onClearFilters = jest.fn();
    renderSidebar({ selectedTagIds: [1], onClearFilters });

    // Act
    fireEvent.click(screen.getByText("Vymazat"));

    // Assert
    expect(onClearFilters).toHaveBeenCalledTimes(1);
  });

  test("search input change calls onSearchChange after debounce", async () => {
    // Arrange
    const onSearchChange = jest.fn();
    renderSidebar({ onSearchChange });

    const input = screen.getByPlaceholderText("Hledat soubory...");

    // Act
    fireEvent.change(input, { target: { value: "foto" } });

    // Assert: not called immediately
    expect(onSearchChange).not.toHaveBeenCalled();

    // Fast-forward debounce timer
    act(() => {
      jest.advanceTimersByTime(300);
    });

    expect(onSearchChange).toHaveBeenCalledWith("foto");
  });

  test("renders empty state when no tags provided", () => {
    // Arrange
    renderSidebar({ tags: [] });

    // Assert
    expect(screen.getByText("Žádné štítky")).toBeInTheDocument();
  });
});
