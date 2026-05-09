import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import "@testing-library/jest-dom";
import PhotoGrid from "../PhotoGrid";

jest.mock("../PhotoThumbnail", () => {
  const React = require("react");
  return {
    __esModule: true,
    default: ({ alt }) => React.createElement("div", { "data-testid": "thumbnail" }, alt),
  };
});

const makePhoto = (overrides = {}) => ({
  id: 1,
  sharePointFileId: "file-001",
  driveId: "drive-xyz",
  name: "photo-01.jpg",
  folderPath: "/Fotky/2026",
  sharePointWebUrl: "https://sp.example.com/photo-01.jpg",
  fileSizeBytes: 2048,
  lastModifiedAt: "2026-04-01T10:00:00Z",
  tags: [],
  ...overrides,
});

const mockPhotos = [
  makePhoto({ id: 1, name: "photo-01.jpg" }),
  makePhoto({ id: 2, name: "photo-02.jpg", sharePointFileId: "file-002" }),
  makePhoto({ id: 3, name: "photo-03.jpg", sharePointFileId: "file-003" }),
];

function renderGrid(overrides = {}) {
  const defaults = {
    photos: mockPhotos,
    selectedPhotoId: null,
    total: 3,
    page: 1,
    pageSize: 48,
    isLoading: false,
    onPhotoSelect: jest.fn(),
    onPageChange: jest.fn(),
    selectedIds: new Set(),
    onPhotoSelection: jest.fn(),
    canSelect: false,
    ...overrides,
  };
  return { ...render(React.createElement(PhotoGrid, defaults)), props: defaults };
}

describe("PhotoGrid", () => {
  afterEach(() => {
    jest.clearAllMocks();
  });

  test("renders photo thumbnails for each photo", () => {
    // Arrange & Act
    renderGrid();

    // Assert
    const thumbnails = screen.getAllByTestId("thumbnail");
    expect(thumbnails).toHaveLength(3);
  });

  test("clicking a photo calls onPhotoSelect with the correct photo", () => {
    // Arrange
    const onPhotoSelect = jest.fn();
    renderGrid({ onPhotoSelect });

    // Act
    fireEvent.click(screen.getByLabelText("photo-01.jpg"));

    // Assert
    expect(onPhotoSelect).toHaveBeenCalledWith(mockPhotos[0]);
  });

  test("multi-selected photo has aria-pressed true", () => {
    // Arrange
    renderGrid({ selectedIds: new Set([2]) });

    // Assert
    const btn = screen.getByLabelText("photo-02.jpg");
    expect(btn).toHaveAttribute("aria-pressed", "true");
  });

  test("non-selected photo has aria-pressed false", () => {
    // Arrange
    renderGrid({ selectedIds: new Set([2]) });

    // Assert
    const btn = screen.getByLabelText("photo-01.jpg");
    expect(btn).toHaveAttribute("aria-pressed", "false");
  });

  test("drawer-open photo has aria-expanded true", () => {
    // Arrange — selectedPhotoId=2 means the drawer is open for photo 2
    renderGrid({ selectedPhotoId: 2 });

    // Assert
    expect(screen.getByLabelText("photo-02.jpg")).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByLabelText("photo-01.jpg")).toHaveAttribute("aria-expanded", "false");
  });

  test("pagination shows correct page info", () => {
    // Arrange
    renderGrid({ total: 100, page: 2, pageSize: 48 });

    // Assert
    expect(screen.getByText("Stránka 2 z 3")).toBeInTheDocument();
  });

  test("Prev button is disabled on first page", () => {
    // Arrange
    renderGrid({ total: 100, page: 1, pageSize: 48 });

    // Assert
    expect(screen.getByLabelText("Předchozí stránka")).toBeDisabled();
  });

  test("Next button is disabled on last page", () => {
    // Arrange
    renderGrid({ total: 48, page: 1, pageSize: 48 });

    // Assert
    expect(screen.getByLabelText("Další stránka")).toBeDisabled();
  });

  test("clicking Next calls onPageChange with incremented page", () => {
    // Arrange
    const onPageChange = jest.fn();
    renderGrid({ total: 100, page: 1, pageSize: 48, onPageChange });

    // Act
    fireEvent.click(screen.getByLabelText("Další stránka"));

    // Assert
    expect(onPageChange).toHaveBeenCalledWith(2);
  });

  test("clicking Prev calls onPageChange with decremented page", () => {
    // Arrange
    const onPageChange = jest.fn();
    renderGrid({ total: 100, page: 3, pageSize: 48, onPageChange });

    // Act
    fireEvent.click(screen.getByLabelText("Předchozí stránka"));

    // Assert
    expect(onPageChange).toHaveBeenCalledWith(2);
  });

  test("shows skeleton placeholders while loading", () => {
    // Arrange
    renderGrid({ isLoading: true, photos: [] });

    // Assert — skeletons are rendered, no thumbnails
    expect(screen.queryByTestId("thumbnail")).not.toBeInTheDocument();
  });

  test("shows empty state message when no photos", () => {
    // Arrange
    renderGrid({ photos: [], total: 0, isLoading: false });

    // Assert
    expect(screen.getByText("Žádné fotografie nenalezeny")).toBeInTheDocument();
  });

  describe("selection", () => {
    test("no checkboxes are rendered regardless of canSelect", () => {
      // Arrange & Act
      renderGrid({ canSelect: true, selectedIds: new Set() });

      // Assert — checkboxes are gone; selection is frame-only
      expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
    });

    test("selected tile gets ring class when canSelect=true", () => {
      // Arrange & Act
      renderGrid({ canSelect: true, selectedIds: new Set([1]) });

      // Assert — the tile wrapper for photo 1 has the ring class
      const tile = screen.getByTestId("photo-tile-1").parentElement;
      expect(tile?.className).toContain("ring-2");
    });

    test("plain click calls onPhotoSelect and does NOT call onPhotoSelection", () => {
      // Arrange
      const onPhotoSelect = jest.fn();
      const onPhotoSelection = jest.fn();
      renderGrid({ canSelect: true, onPhotoSelect, onPhotoSelection });

      // Act
      fireEvent.click(screen.getByTestId("photo-tile-1"));

      // Assert
      expect(onPhotoSelect).toHaveBeenCalledWith(mockPhotos[0]);
      expect(onPhotoSelection).not.toHaveBeenCalled();
    });

    test("Cmd+click calls onPhotoSelection(id, toggle) and does NOT open drawer", () => {
      // Arrange
      const onPhotoSelect = jest.fn();
      const onPhotoSelection = jest.fn();
      renderGrid({ canSelect: true, onPhotoSelect, onPhotoSelection });

      // Act
      fireEvent.click(screen.getByTestId("photo-tile-1"), { metaKey: true });

      // Assert
      expect(onPhotoSelection).toHaveBeenCalledWith(1, "toggle");
      expect(onPhotoSelect).not.toHaveBeenCalled();
    });

    test("Ctrl+click calls onPhotoSelection(id, toggle) and does NOT open drawer", () => {
      // Arrange
      const onPhotoSelect = jest.fn();
      const onPhotoSelection = jest.fn();
      renderGrid({ canSelect: true, onPhotoSelect, onPhotoSelection });

      // Act
      fireEvent.click(screen.getByTestId("photo-tile-1"), { ctrlKey: true });

      // Assert
      expect(onPhotoSelection).toHaveBeenCalledWith(1, "toggle");
      expect(onPhotoSelect).not.toHaveBeenCalled();
    });

    test("Shift+click calls onPhotoSelection(id, range) and does NOT open drawer", () => {
      // Arrange
      const onPhotoSelect = jest.fn();
      const onPhotoSelection = jest.fn();
      renderGrid({ canSelect: true, onPhotoSelect, onPhotoSelection });

      // Act
      fireEvent.click(screen.getByTestId("photo-tile-2"), { shiftKey: true });

      // Assert
      expect(onPhotoSelection).toHaveBeenCalledWith(2, "range");
      expect(onPhotoSelect).not.toHaveBeenCalled();
    });

    test("when canSelect=false, Cmd+click falls through to onPhotoSelect", () => {
      // Arrange
      const onPhotoSelect = jest.fn();
      const onPhotoSelection = jest.fn();
      renderGrid({ canSelect: false, onPhotoSelect, onPhotoSelection });

      // Act
      fireEvent.click(screen.getByTestId("photo-tile-1"), { metaKey: true });

      // Assert — read-only users always open drawer regardless of modifier
      expect(onPhotoSelect).toHaveBeenCalledWith(mockPhotos[0]);
      expect(onPhotoSelection).not.toHaveBeenCalled();
    });
  });

  test("renders tag overlay when showTags is true and photo has tags", () => {
    // Arrange
    const photo = makePhoto({
      id: 1,
      name: "tagged.jpg",
      tags: [
        { id: 10, name: "Léto", source: "manual" },
        { id: 11, name: "Příroda", source: "manual" },
      ],
    });

    // Act
    renderGrid({ photos: [photo], total: 1, showTags: true });

    // Assert — tag text is visible, confirming overlay rendered
    expect(screen.getByText("Léto")).toBeInTheDocument();
    expect(screen.getByText("Příroda")).toBeInTheDocument();
  });

  test("does not render tag overlay when showTags is false", () => {
    // Arrange
    const photo = makePhoto({
      id: 1,
      name: "tagged.jpg",
      tags: [{ id: 10, name: "Léto", source: "manual" }],
    });

    // Act
    renderGrid({ photos: [photo], total: 1, showTags: false });

    // Assert
    expect(screen.queryByText("Léto")).not.toBeInTheDocument();
  });

  test("does not render tag overlay when showTags is omitted (default false)", () => {
    // Arrange
    const photo = makePhoto({
      id: 1,
      name: "tagged.jpg",
      tags: [{ id: 10, name: "Léto", source: "manual" }],
    });

    // Act — no showTags prop passed
    renderGrid({ photos: [photo], total: 1 });

    // Assert
    expect(screen.queryByText("Léto")).not.toBeInTheDocument();
  });

  test("shows overflow chip when photo has more than 3 tags", () => {
    // Arrange
    const photo = makePhoto({
      id: 1,
      name: "many-tags.jpg",
      tags: [
        { id: 1, name: "Tag1", source: "manual" },
        { id: 2, name: "Tag2", source: "manual" },
        { id: 3, name: "Tag3", source: "manual" },
        { id: 4, name: "Tag4", source: "manual" },
        { id: 5, name: "Tag5", source: "manual" },
      ],
    });

    // Act
    renderGrid({ photos: [photo], total: 1, showTags: true });

    // Assert — first 3 tags visible, rest truncated, overflow chip shows "+2"
    expect(screen.getByText("Tag1")).toBeInTheDocument();
    expect(screen.getByText("Tag2")).toBeInTheDocument();
    expect(screen.getByText("Tag3")).toBeInTheDocument();
    expect(screen.queryByText("Tag4")).not.toBeInTheDocument();
    expect(screen.queryByText("Tag5")).not.toBeInTheDocument();
    expect(screen.getByText("+2")).toBeInTheDocument();
  });

  test("overlay has pointer-events-none so click reaches the button", () => {
    // Arrange
    const onPhotoSelect = jest.fn();
    const photo = makePhoto({
      id: 1,
      name: "click-test.jpg",
      tags: [{ id: 10, name: "Léto", source: "manual" }],
    });

    // Act
    renderGrid({ photos: [photo], total: 1, showTags: true, onPhotoSelect });
    fireEvent.click(screen.getByLabelText("click-test.jpg"));

    // Assert — click propagated through the overlay to the button
    expect(onPhotoSelect).toHaveBeenCalledWith(photo);
  });
});
