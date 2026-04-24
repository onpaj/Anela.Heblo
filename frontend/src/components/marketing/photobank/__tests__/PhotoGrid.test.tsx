import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import PhotoGrid from "../PhotoGrid";
import type { PhotoDto } from "../../../../api/hooks/usePhotobank";

// Stub PhotoThumbnail to avoid MSAL/fetch dependencies in unit tests
jest.mock("../PhotoThumbnail", () => ({
  __esModule: true,
  default: ({ alt }: { alt: string }) => <div data-testid="thumbnail">{alt}</div>,
}));

const makePhoto = (overrides: Partial<PhotoDto> = {}): PhotoDto => ({
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

const mockPhotos: PhotoDto[] = [
  makePhoto({ id: 1, name: "photo-01.jpg" }),
  makePhoto({ id: 2, name: "photo-02.jpg", sharePointFileId: "file-002" }),
  makePhoto({ id: 3, name: "photo-03.jpg", sharePointFileId: "file-003" }),
];

function renderGrid(overrides: Partial<React.ComponentProps<typeof PhotoGrid>> = {}) {
  const defaults: React.ComponentProps<typeof PhotoGrid> = {
    photos: mockPhotos,
    selectedPhotoId: null,
    total: 3,
    page: 1,
    pageSize: 48,
    isLoading: false,
    onPhotoSelect: jest.fn(),
    onPageChange: jest.fn(),
    ...overrides,
  };
  return { ...render(<PhotoGrid {...defaults} />), props: defaults };
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

  test("selected photo has aria-pressed true", () => {
    // Arrange
    renderGrid({ selectedPhotoId: 2 });

    // Assert
    const btn = screen.getByLabelText("photo-02.jpg");
    expect(btn).toHaveAttribute("aria-pressed", "true");
  });

  test("non-selected photo has aria-pressed false", () => {
    // Arrange
    renderGrid({ selectedPhotoId: 2 });

    // Assert
    const btn = screen.getByLabelText("photo-01.jpg");
    expect(btn).toHaveAttribute("aria-pressed", "false");
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
});
