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
    withoutTags: false,
    useRegex: false,
    onTagToggle: jest.fn(),
    onSearchChange: jest.fn(),
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
    renderSidebar();

    expect(screen.getByText("výroba")).toBeInTheDocument();
    expect(screen.getByText("10")).toBeInTheDocument();
    expect(screen.getByText("marketing")).toBeInTheDocument();
    expect(screen.getByText("5")).toBeInTheDocument();
    expect(screen.getByText("produkty")).toBeInTheDocument();
    expect(screen.getByText("3")).toBeInTheDocument();
  });

  test("clicking a tag calls onTagToggle with tag id", () => {
    const onTagToggle = jest.fn();
    renderSidebar({ onTagToggle });

    fireEvent.click(screen.getByText("výroba"));

    expect(onTagToggle).toHaveBeenCalledWith(1);
  });

  test("selected tag has highlighted aria-pressed state", () => {
    renderSidebar({ selectedTagIds: [2] });

    const marketingBtn = screen.getByRole("button", { name: /marketing/ });
    const výrobaBtn = screen.getByRole("button", { name: /výroba/ });

    expect(marketingBtn).toHaveAttribute("aria-pressed", "true");
    expect(výrobaBtn).toHaveAttribute("aria-pressed", "false");
  });

  test("clicking a selected tag calls onTagToggle to deselect it", () => {
    const onTagToggle = jest.fn();
    renderSidebar({ selectedTagIds: [1], onTagToggle });

    fireEvent.click(screen.getByText("výroba"));

    expect(onTagToggle).toHaveBeenCalledWith(1);
  });

  test("'Clear filters' button appears when tags are selected", () => {
    renderSidebar({ selectedTagIds: [1] });

    expect(screen.getByText("Vymazat")).toBeInTheDocument();
  });

  test("'Clear filters' button appears when search is active", () => {
    renderSidebar({ search: "foto" });

    expect(screen.getByText("Vymazat")).toBeInTheDocument();
  });

  test("'Clear filters' button is absent when no filters active", () => {
    renderSidebar({ selectedTagIds: [], search: "" });

    expect(screen.queryByText("Vymazat")).not.toBeInTheDocument();
  });

  test("clicking 'Clear filters' calls onClearFilters", () => {
    const onClearFilters = jest.fn();
    renderSidebar({ selectedTagIds: [1], onClearFilters });

    fireEvent.click(screen.getByText("Vymazat"));

    expect(onClearFilters).toHaveBeenCalledTimes(1);
  });

  test("search input change calls onSearchChange after debounce", async () => {
    const onSearchChange = jest.fn();
    renderSidebar({ onSearchChange });

    const input = screen.getByPlaceholderText("Hledat soubory a složky...");

    fireEvent.change(input, { target: { value: "foto" } });

    expect(onSearchChange).not.toHaveBeenCalled();

    act(() => {
      jest.advanceTimersByTime(300);
    });

    expect(onSearchChange).toHaveBeenCalledWith("foto");
  });

  test("renders empty state when no tags provided", () => {
    renderSidebar({ tags: [] });

    expect(screen.getByText("Žádné štítky")).toBeInTheDocument();
  });

  test("regex mode with invalid pattern shows error and does not call onSearchChange after debounce", () => {
    const onSearchChange = jest.fn();
    renderSidebar({ useRegex: true, onSearchChange });

    const input = screen.getByPlaceholderText("Regex (POSIX, case-insensitive)...");

    fireEvent.change(input, { target: { value: "[bad" } });

    expect(screen.getByText("Neplatný regulární výraz")).toBeInTheDocument();

    act(() => {
      jest.advanceTimersByTime(300);
    });

    expect(onSearchChange).not.toHaveBeenCalled();
  });

  test("regex mode with valid pattern calls onSearchChange after debounce", () => {
    const onSearchChange = jest.fn();
    renderSidebar({ useRegex: true, onSearchChange });

    const input = screen.getByPlaceholderText("Regex (POSIX, case-insensitive)...");

    fireEvent.change(input, { target: { value: "^ok$" } });

    expect(screen.queryByText("Neplatný regulární výraz")).not.toBeInTheDocument();

    act(() => {
      jest.advanceTimersByTime(300);
    });

    expect(onSearchChange).toHaveBeenCalledWith("^ok$");
  });

  test("regex mode off with invalid regex-like input calls onSearchChange and shows no error", () => {
    const onSearchChange = jest.fn();
    renderSidebar({ useRegex: false, onSearchChange });

    const input = screen.getByPlaceholderText("Hledat soubory a složky...");

    fireEvent.change(input, { target: { value: "[bad" } });

    expect(screen.queryByText("Neplatný regulární výraz")).not.toBeInTheDocument();

    act(() => {
      jest.advanceTimersByTime(300);
    });

    expect(onSearchChange).toHaveBeenCalledWith("[bad");
  });

  test("toggling regex off clears the error", () => {
    const { rerender } = renderSidebar({ useRegex: true });

    const input = screen.getByPlaceholderText("Regex (POSIX, case-insensitive)...");

    fireEvent.change(input, { target: { value: "[bad" } });

    expect(screen.getByText("Neplatný regulární výraz")).toBeInTheDocument();

    rerender(
      <TagSidebar
        tags={mockTags}
        selectedTagIds={[]}
        search="[bad"
        withoutTags={false}
        useRegex={false}
        onTagToggle={jest.fn()}
        onSearchChange={jest.fn()}
        onWithoutTagsToggle={jest.fn()}
        onClearFilters={jest.fn()}
        onRegexChange={jest.fn()}
      />
    );

    expect(screen.queryByText("Neplatný regulární výraz")).not.toBeInTheDocument();
  });
});
