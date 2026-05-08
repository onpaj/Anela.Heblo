import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import PhotoList from "../PhotoList";
import type { PhotoDto } from "../../../../api/hooks/usePhotobank";

jest.mock("../PhotoThumbnail", () => ({
  __esModule: true,
  default: ({ alt }: { alt: string }) => <img alt={alt} />,
}));

function makePhoto(overrides: Partial<PhotoDto> = {}): PhotoDto {
  return {
    id: 1,
    sharePointFileId: "file-1",
    driveId: "drive-1",
    name: "photo.jpg",
    folderPath: "/Marketing",
    sharePointWebUrl: "https://example.com/photo",
    fileSizeBytes: 2048,
    lastModifiedAt: "2024-01-15T10:00:00Z",
    tags: [],
    ...overrides,
  };
}

const mockPhotos: PhotoDto[] = [
  makePhoto({ id: 1, name: "photo-01.jpg" }),
  makePhoto({ id: 2, name: "photo-02.jpg", sharePointFileId: "file-2" }),
  makePhoto({ id: 3, name: "photo-03.jpg", sharePointFileId: "file-3" }),
];

function renderList(overrides: Partial<React.ComponentProps<typeof PhotoList>> = {}) {
  const defaults: React.ComponentProps<typeof PhotoList> = {
    photos: mockPhotos,
    selectedPhotoId: null,
    total: 3,
    page: 1,
    pageSize: 48,
    isLoading: false,
    onPhotoSelect: jest.fn(),
    onPageChange: jest.fn(),
    selectedIds: new Set<number>(),
    onPhotoSelection: jest.fn(),
    canSelect: false,
    ...overrides,
  };
  return { ...render(<PhotoList {...defaults} />), props: defaults };
}

describe("PhotoList", () => {
  afterEach(() => {
    jest.clearAllMocks();
  });

  test("renders one row per photo", () => {
    // Arrange & Act
    renderList();

    // Assert — each photo renders an img with its name as alt
    expect(screen.getByAltText("photo-01.jpg")).toBeInTheDocument();
    expect(screen.getByAltText("photo-02.jpg")).toBeInTheDocument();
    expect(screen.getByAltText("photo-03.jpg")).toBeInTheDocument();
  });

  test("formats lastModifiedAt as Czech date", () => {
    // Arrange & Act
    renderList({ photos: [makePhoto({ lastModifiedAt: "2024-01-15T10:00:00Z" })] });

    // Assert
    const expected = new Date("2024-01-15T10:00:00Z").toLocaleDateString("cs-CZ");
    expect(screen.getByText(expected)).toBeInTheDocument();
  });

  test("formats fileSizeBytes: 1024 bytes → 1.0 KB", () => {
    // Arrange & Act
    renderList({ photos: [makePhoto({ fileSizeBytes: 1024 })] });

    // Assert
    expect(screen.getByText("1.0 KB")).toBeInTheDocument();
  });

  test("formats fileSizeBytes: null → —", () => {
    // Arrange & Act
    renderList({ photos: [makePhoto({ fileSizeBytes: null })] });

    // Assert
    expect(screen.getByText("—")).toBeInTheDocument();
  });

  test("shows all tags when count is ≤5", () => {
    // Arrange
    const tags = [
      { id: 1, name: "Tag1", source: "Manual" as const },
      { id: 2, name: "Tag2", source: "Manual" as const },
      { id: 3, name: "Tag3", source: "AI" as const },
    ];

    // Act
    renderList({ photos: [makePhoto({ tags })] });

    // Assert
    expect(screen.getByText("Tag1")).toBeInTheDocument();
    expect(screen.getByText("Tag2")).toBeInTheDocument();
    expect(screen.getByText("Tag3")).toBeInTheDocument();
    expect(screen.queryByText(/^\+/)).not.toBeInTheDocument();
  });

  test("shows +N chip when tag count exceeds 5", () => {
    // Arrange
    const tags = [
      { id: 1, name: "Tag1", source: "Manual" as const },
      { id: 2, name: "Tag2", source: "Manual" as const },
      { id: 3, name: "Tag3", source: "AI" as const },
      { id: 4, name: "Tag4", source: "Rule" as const },
      { id: 5, name: "Tag5", source: "Manual" as const },
      { id: 6, name: "Tag6", source: "AI" as const },
      { id: 7, name: "Tag7", source: "Rule" as const },
    ];

    // Act
    renderList({ photos: [makePhoto({ tags })] });

    // Assert: first 5 visible, +2 overflow chip
    expect(screen.getByText("Tag1")).toBeInTheDocument();
    expect(screen.getByText("Tag5")).toBeInTheDocument();
    expect(screen.queryByText("Tag6")).not.toBeInTheDocument();
    expect(screen.getByText("+2")).toBeInTheDocument();
  });

  test("clicking a row calls onPhotoSelect with the photo", () => {
    // Arrange
    const onPhotoSelect = jest.fn();
    renderList({ onPhotoSelect });

    // Act
    fireEvent.click(screen.getByLabelText("photo-01.jpg"));

    // Assert
    expect(onPhotoSelect).toHaveBeenCalledWith(mockPhotos[0]);
  });

  test("multi-selected row has aria-pressed true", () => {
    // Arrange
    renderList({ selectedIds: new Set([2]) });

    // Assert
    expect(screen.getByLabelText("photo-02.jpg")).toHaveAttribute("aria-pressed", "true");
  });

  test("non-selected row has aria-pressed false", () => {
    // Arrange
    renderList({ selectedIds: new Set([2]) });

    // Assert
    expect(screen.getByLabelText("photo-01.jpg")).toHaveAttribute("aria-pressed", "false");
  });

  test("drawer-open row has aria-expanded true", () => {
    // Arrange — selectedPhotoId=2 means the drawer is open for photo 2
    renderList({ selectedPhotoId: 2 });

    // Assert
    expect(screen.getByLabelText("photo-02.jpg")).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByLabelText("photo-01.jpg")).toHaveAttribute("aria-expanded", "false");
  });

  test("clicking SharePoint link does NOT fire onPhotoSelect", () => {
    // Arrange
    const onPhotoSelect = jest.fn();
    renderList({
      photos: [makePhoto({ sharePointWebUrl: "https://example.com/photo" })],
      onPhotoSelect,
    });

    // Act — click the SharePoint link
    const link = screen.getByRole("link");
    fireEvent.click(link);

    // Assert
    expect(onPhotoSelect).not.toHaveBeenCalled();
  });

  test("shows loading skeleton when isLoading is true", () => {
    // Arrange & Act
    renderList({ isLoading: true, photos: [] });

    // Assert — no photos rendered, skeleton present via animate-pulse divs
    expect(screen.queryByRole("button", { name: /photo/ })).not.toBeInTheDocument();
    expect(screen.getByTestId("loading-skeleton")).toBeInTheDocument();
  });

  test("shows empty state when photos is empty and not loading", () => {
    // Arrange & Act
    renderList({ photos: [], total: 0, isLoading: false });

    // Assert
    expect(screen.getByText("Žádné fotografie nenalezeny")).toBeInTheDocument();
  });

  describe("selection", () => {
    test("no checkboxes are rendered regardless of canSelect", () => {
      // Arrange & Act
      renderList({ canSelect: true, selectedIds: new Set<number>() });

      // Assert — checkboxes gone
      expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
    });

    test("plain click calls onPhotoSelect and does NOT call onPhotoSelection", () => {
      // Arrange
      const onPhotoSelect = jest.fn();
      const onPhotoSelection = jest.fn();
      renderList({ canSelect: true, onPhotoSelect, onPhotoSelection });

      // Act
      fireEvent.click(screen.getByTestId("photo-row-1"));

      // Assert
      expect(onPhotoSelect).toHaveBeenCalledWith(mockPhotos[0]);
      expect(onPhotoSelection).not.toHaveBeenCalled();
    });

    test("Cmd+click calls onPhotoSelection(id, toggle) and does NOT open drawer", () => {
      // Arrange
      const onPhotoSelect = jest.fn();
      const onPhotoSelection = jest.fn();
      renderList({ canSelect: true, onPhotoSelect, onPhotoSelection });

      // Act
      fireEvent.click(screen.getByTestId("photo-row-1"), { metaKey: true });

      // Assert
      expect(onPhotoSelection).toHaveBeenCalledWith(1, "toggle");
      expect(onPhotoSelect).not.toHaveBeenCalled();
    });

    test("Ctrl+click calls onPhotoSelection(id, toggle) and does NOT open drawer", () => {
      // Arrange
      const onPhotoSelect = jest.fn();
      const onPhotoSelection = jest.fn();
      renderList({ canSelect: true, onPhotoSelect, onPhotoSelection });

      // Act
      fireEvent.click(screen.getByTestId("photo-row-1"), { ctrlKey: true });

      // Assert
      expect(onPhotoSelection).toHaveBeenCalledWith(1, "toggle");
      expect(onPhotoSelect).not.toHaveBeenCalled();
    });

    test("Shift+click calls onPhotoSelection(id, range) and does NOT open drawer", () => {
      // Arrange
      const onPhotoSelect = jest.fn();
      const onPhotoSelection = jest.fn();
      renderList({ canSelect: true, onPhotoSelect, onPhotoSelection });

      // Act
      fireEvent.click(screen.getByTestId("photo-row-2"), { shiftKey: true });

      // Assert
      expect(onPhotoSelection).toHaveBeenCalledWith(2, "range");
      expect(onPhotoSelect).not.toHaveBeenCalled();
    });

    test("when canSelect=false, Cmd+click falls through to onPhotoSelect", () => {
      // Arrange
      const onPhotoSelect = jest.fn();
      const onPhotoSelection = jest.fn();
      renderList({ canSelect: false, onPhotoSelect, onPhotoSelection });

      // Act
      fireEvent.click(screen.getByTestId("photo-row-1"), { metaKey: true });

      // Assert
      expect(onPhotoSelect).toHaveBeenCalledWith(mockPhotos[0]);
      expect(onPhotoSelection).not.toHaveBeenCalled();
    });
  });
});
