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
    folderPath: "",
    withoutTags: false,
    useRegex: false,
    onTagToggle: jest.fn(),
    onSearchChange: jest.fn(),
    onFolderPathChange: jest.fn(),
    onWithoutTagsToggle: jest.fn(),
    onClearFilters: jest.fn(),
    onRegexChange: jest.fn(),
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

  test("folder path input is rendered with correct placeholder", () => {
    // Arrange & Act
    renderSidebar();

    // Assert
    expect(screen.getByPlaceholderText("Hledat ve složkách...")).toBeInTheDocument();
  });

  test("folder path input change calls onFolderPathChange after debounce", async () => {
    // Arrange
    const onFolderPathChange = jest.fn();
    renderSidebar({ onFolderPathChange });

    const input = screen.getByPlaceholderText("Hledat ve složkách...");

    // Act
    fireEvent.change(input, { target: { value: "Marketing" } });

    // Assert: not called immediately
    expect(onFolderPathChange).not.toHaveBeenCalled();

    // Fast-forward debounce timer
    act(() => {
      jest.advanceTimersByTime(300);
    });

    expect(onFolderPathChange).toHaveBeenCalledWith("Marketing");
  });

  test("'Clear filters' button appears when folderPath is active", () => {
    // Arrange
    renderSidebar({ folderPath: "Marketing" });

    // Assert
    expect(screen.getByText("Vymazat")).toBeInTheDocument();
  });

  test("'Clear filters' button is absent when no filters active including folderPath", () => {
    // Arrange
    renderSidebar({ selectedTagIds: [], search: "", folderPath: "" });

    // Assert
    expect(screen.queryByText("Vymazat")).not.toBeInTheDocument();
  });

  test("X button on folder path input clears it immediately", () => {
    const onFolderPathChange = jest.fn();
    renderSidebar({ folderPath: "Marketing", onFolderPathChange });
    fireEvent.click(screen.getByRole("button", { name: "Vymazat složku" }));
    expect(onFolderPathChange).toHaveBeenCalledWith("");
  });

  test("regex mode with invalid pattern shows error and does not call onSearchChange after debounce", () => {
    // Arrange
    const onSearchChange = jest.fn();
    renderSidebar({ useRegex: true, onSearchChange });

    const input = screen.getByPlaceholderText("Regex (POSIX, case-insensitive)...");

    // Act — type an invalid regex
    fireEvent.change(input, { target: { value: "[bad" } });

    // Assert: error message appears
    expect(screen.getByText("Neplatný regulární výraz")).toBeInTheDocument();

    // Fast-forward debounce timer
    act(() => {
      jest.advanceTimersByTime(300);
    });

    // Assert: onSearchChange is NOT called when pattern is invalid
    expect(onSearchChange).not.toHaveBeenCalled();
  });

  test("regex mode with valid pattern calls onSearchChange after debounce", () => {
    // Arrange
    const onSearchChange = jest.fn();
    renderSidebar({ useRegex: true, onSearchChange });

    const input = screen.getByPlaceholderText("Regex (POSIX, case-insensitive)...");

    // Act — type a valid regex
    fireEvent.change(input, { target: { value: "^ok$" } });

    // Assert: no error shown
    expect(screen.queryByText("Neplatný regulární výraz")).not.toBeInTheDocument();

    // Fast-forward debounce timer
    act(() => {
      jest.advanceTimersByTime(300);
    });

    // Assert: onSearchChange is called with the valid pattern
    expect(onSearchChange).toHaveBeenCalledWith("^ok$");
  });

  test("regex mode off with invalid regex-like input calls onSearchChange and shows no error", () => {
    // Arrange
    const onSearchChange = jest.fn();
    renderSidebar({ useRegex: false, onSearchChange });

    const input = screen.getByPlaceholderText("Hledat soubory...");

    // Act — type something that would be invalid regex but regex mode is off
    fireEvent.change(input, { target: { value: "[bad" } });

    // Assert: no error shown
    expect(screen.queryByText("Neplatný regulární výraz")).not.toBeInTheDocument();

    // Fast-forward debounce timer
    act(() => {
      jest.advanceTimersByTime(300);
    });

    // Assert: onSearchChange is called as normal
    expect(onSearchChange).toHaveBeenCalledWith("[bad");
  });

  test("toggling regex off clears the error", () => {
    // Arrange — render with regex enabled and type invalid pattern
    const { rerender } = renderSidebar({ useRegex: true });

    const input = screen.getByPlaceholderText("Regex (POSIX, case-insensitive)...");

    // Act — type an invalid regex
    fireEvent.change(input, { target: { value: "[bad" } });

    // Assert: error message appears
    expect(screen.getByText("Neplatný regulární výraz")).toBeInTheDocument();

    // Act — re-render the component with useRegex={false} (simulating parent passing new props)
    rerender(
      <TagSidebar
        tags={mockTags}
        selectedTagIds={[]}
        search="[bad"
        folderPath=""
        withoutTags={false}
        useRegex={false}
        onTagToggle={jest.fn()}
        onSearchChange={jest.fn()}
        onFolderPathChange={jest.fn()}
        onWithoutTagsToggle={jest.fn()}
        onClearFilters={jest.fn()}
        onRegexChange={jest.fn()}
      />
    );

    // Assert: error message is gone
    expect(screen.queryByText("Neplatný regulární výraz")).not.toBeInTheDocument();
  });
});
