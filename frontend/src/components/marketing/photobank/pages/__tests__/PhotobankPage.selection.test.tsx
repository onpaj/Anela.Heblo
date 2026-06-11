import React from "react";
import { render, screen, act, fireEvent } from "@testing-library/react";
import PhotobankPage from "../PhotobankPage";
import type { PhotoDto } from "../../../../../api/hooks/usePhotobank";

// ---- Mocks ------------------------------------------------------------------

const mockBulkAddByIdsMutateAsync = jest.fn().mockResolvedValue(undefined);
const mockBulkAddByIdsMutation = {
  mutateAsync: mockBulkAddByIdsMutateAsync,
  isPending: false,
};

jest.mock("../../../../../auth/PermissionsContext", () => ({
  usePermissionsContext: () => ({
    permissions: [],
    isSuperUser: false,
    groups: [],
    isLoading: false,
    hasPermission: (p: string) => p === "marketing.photobank.write",
  }),
}));

const mockPhotos: PhotoDto[] = [
  {
    id: 1,
    sharePointFileId: "f1",
    driveId: null,
    name: "photo-01.jpg",
    folderPath: "/test",
    sharePointWebUrl: null,
    fileSizeBytes: null,
    lastModifiedAt: "2026-01-01T00:00:00Z",
    tags: [],
  },
  {
    id: 2,
    sharePointFileId: "f2",
    driveId: null,
    name: "photo-02.jpg",
    folderPath: "/test",
    sharePointWebUrl: null,
    fileSizeBytes: null,
    lastModifiedAt: "2026-01-01T00:00:00Z",
    tags: [],
  },
];

jest.mock("../../../../../api/hooks/usePhotobank", () => ({
  usePhotos: () => ({ data: { items: mockPhotos, total: 2, page: 1, pageSize: 48 }, isLoading: false }),
  usePhotoTags: () => ({ data: [] }),
  useBulkAddPhotoTagByIds: () => mockBulkAddByIdsMutation,
  useRetagPhotos: () => ({
    mutate: jest.fn(),
    isPending: false,
  }),
}));

jest.mock("react-router-dom", () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => <a href={to}>{children}</a>,
}));

jest.mock("../../PhotoThumbnail", () => ({
  __esModule: true,
  default: ({ alt }: { alt: string }) => <img alt={alt} />,
}));

jest.mock("../../TagSidebar", () => ({
  __esModule: true,
  default: () => <div data-testid="tag-sidebar" />,
}));

jest.mock("../../PhotoDrawer", () => ({
  __esModule: true,
  default: () => <div data-testid="photo-drawer" />,
}));

// ---- Tests ------------------------------------------------------------------

beforeEach(() => {
  localStorage.clear();
  mockBulkAddByIdsMutateAsync.mockClear().mockResolvedValue(undefined);
});

test("initial state: bulk action bar not shown when nothing selected", () => {
  // Arrange & Act
  render(<PhotobankPage />);

  // Assert
  expect(screen.queryByTestId("bulk-action-bar")).not.toBeInTheDocument();
});

// TODO: Add a test for canBulkTag=false (no marketing_writer role) that properly isolates
// the useMsal mock per describe block. The role gate is exercised implicitly by unit tests
// on PhotoGrid and PhotoList (canSelect=false hides checkboxes).

test("photo tiles are rendered in grid view and no checkboxes shown", () => {
  // Arrange & Act
  render(<PhotobankPage />);

  // Assert: tiles present, checkboxes gone (selection is frame-only now)
  expect(screen.getByTestId("photo-tile-1")).toBeInTheDocument();
  expect(screen.getByTestId("photo-tile-2")).toBeInTheDocument();
  expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
});

test("selecting a photo shows the bulk action bar", () => {
  // Arrange
  render(<PhotobankPage />);

  // Act — Cmd+click photo-1 tile to toggle selection
  act(() => {
    fireEvent.click(screen.getByTestId("photo-tile-1"), { metaKey: true });
  });

  // Assert
  expect(screen.getByTestId("bulk-action-bar")).toBeInTheDocument();
  expect(screen.getByText("1 fotek vybráno")).toBeInTheDocument();
});

test("clicking clear in bulk action bar hides the bar", () => {
  // Arrange
  render(<PhotobankPage />);
  act(() => {
    fireEvent.click(screen.getByTestId("photo-tile-1"), { metaKey: true });
  });
  expect(screen.getByTestId("bulk-action-bar")).toBeInTheDocument();

  // Act
  act(() => {
    screen.getByTestId("bulk-clear-btn").click();
  });

  // Assert
  expect(screen.queryByTestId("bulk-action-bar")).not.toBeInTheDocument();
});
