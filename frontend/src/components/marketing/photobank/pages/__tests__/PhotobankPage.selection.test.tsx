import React from "react";
import { render, screen, act } from "@testing-library/react";
import PhotobankPage from "../PhotobankPage";
import type { PhotoDto } from "../../../../../api/hooks/usePhotobank";

// ---- Mocks ------------------------------------------------------------------

const mockBulkAddByIdsMutateAsync = jest.fn().mockResolvedValue(undefined);
const mockBulkAddByIdsMutation = {
  mutateAsync: mockBulkAddByIdsMutateAsync,
  isPending: false,
};

jest.mock("@azure/msal-react", () => ({
  useMsal: () => ({ accounts: [{ idTokenClaims: { roles: ["marketing_writer"] } }] }),
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

test("checkboxes are rendered in grid view when canBulkTag=true", () => {
  // Arrange & Act
  render(<PhotobankPage />);

  // Assert: checkboxes should be present for each photo
  expect(screen.getByTestId("photo-select-checkbox-1")).toBeInTheDocument();
  expect(screen.getByTestId("photo-select-checkbox-2")).toBeInTheDocument();
});

test("selecting a photo shows the bulk action bar", () => {
  // Arrange
  render(<PhotobankPage />);

  // Act — click photo-1 checkbox
  act(() => {
    screen.getByTestId("photo-select-checkbox-1").click();
  });

  // Assert
  expect(screen.getByTestId("bulk-action-bar")).toBeInTheDocument();
  expect(screen.getByText("1 fotek vybráno")).toBeInTheDocument();
});

test("clicking clear in bulk action bar hides the bar", () => {
  // Arrange
  render(<PhotobankPage />);
  act(() => {
    screen.getByTestId("photo-select-checkbox-1").click();
  });
  expect(screen.getByTestId("bulk-action-bar")).toBeInTheDocument();

  // Act
  act(() => {
    screen.getByTestId("bulk-clear-btn").click();
  });

  // Assert
  expect(screen.queryByTestId("bulk-action-bar")).not.toBeInTheDocument();
});
