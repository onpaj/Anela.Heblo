import React from "react";
import { act, render, screen, fireEvent } from "@testing-library/react";
import PhotobankPage from "../pages/PhotobankPage";

jest.mock("@azure/msal-react", () => ({
  useMsal: () => ({
    accounts: [{ idTokenClaims: { roles: ["marketing_writer"] } }],
  }),
}));

jest.mock("../../../../api/hooks/usePhotobank", () => ({
  usePhotos: () => ({
    data: {
      items: [
        { id: 1, name: "p1.jpg", sharePointFileId: "f1", driveId: "d", folderPath: "/", sharePointWebUrl: null, fileSizeBytes: null, lastModifiedAt: "2026-01-01T00:00:00Z", tags: [] },
        { id: 2, name: "p2.jpg", sharePointFileId: "f2", driveId: "d", folderPath: "/", sharePointWebUrl: null, fileSizeBytes: null, lastModifiedAt: "2026-01-01T00:00:00Z", tags: [] },
        { id: 3, name: "p3.jpg", sharePointFileId: "f3", driveId: "d", folderPath: "/", sharePointWebUrl: null, fileSizeBytes: null, lastModifiedAt: "2026-01-01T00:00:00Z", tags: [] },
      ],
      total: 3,
      page: 1,
    },
    isLoading: false,
  }),
  usePhotoTags: () => ({ data: [] }),
  useBulkAddPhotoTagByIds: () => ({
    mutateAsync: jest.fn().mockResolvedValue(undefined),
    isPending: false,
  }),
  useRetagPhotos: () => ({
    mutate: jest.fn(),
    isPending: false,
  }),
}));

jest.mock("react-router-dom", () => ({
  Link: ({ children, to }: any) => <a href={to}>{children}</a>,
}));

jest.mock("../PhotoThumbnail", () => ({
  __esModule: true,
  default: ({ alt }: { alt: string }) => <img alt={alt} />,
}));

let capturedGridProps: any;
jest.mock("../PhotoGrid", () => ({
  __esModule: true,
  default: (props: any) => {
    capturedGridProps = props;
    return <div data-testid="photo-grid" />;
  },
}));

jest.mock("../PhotoList", () => ({
  __esModule: true,
  default: () => <div data-testid="photo-list" />,
}));

jest.mock("../TagSidebar", () => ({
  __esModule: true,
  default: () => <div data-testid="tag-sidebar" />,
}));

jest.mock("../PhotoDrawer", () => ({
  __esModule: true,
  default: () => <div data-testid="photo-drawer" />,
}));

beforeEach(() => {
  localStorage.clear();
});

test("renders tile view by default", () => {
  render(<PhotobankPage />);

  expect(screen.getByTestId("photo-grid")).toBeInTheDocument();
  expect(screen.queryByTestId("photo-list")).not.toBeInTheDocument();
});

test("clicking the list toggle renders list view", () => {
  render(<PhotobankPage />);

  fireEvent.click(screen.getByTitle("Seznam"));

  expect(screen.getByTestId("photo-list")).toBeInTheDocument();
  expect(screen.queryByTestId("photo-grid")).not.toBeInTheDocument();
});

test("clicking back to tiles toggle renders tile view", () => {
  render(<PhotobankPage />);

  fireEvent.click(screen.getByTitle("Seznam"));
  fireEvent.click(screen.getByTitle("Dlaždice"));

  expect(screen.getByTestId("photo-grid")).toBeInTheDocument();
  expect(screen.queryByTestId("photo-list")).not.toBeInTheDocument();
});

test("persists view mode to localStorage when switching to list", () => {
  render(<PhotobankPage />);

  fireEvent.click(screen.getByTitle("Seznam"));

  expect(localStorage.getItem("photobank.view")).toBe("list");
});

test("reads initial view from localStorage", () => {
  localStorage.setItem("photobank.view", "list");

  render(<PhotobankPage />);

  expect(screen.getByTestId("photo-list")).toBeInTheDocument();
  expect(screen.queryByTestId("photo-grid")).not.toBeInTheDocument();
});

test("Shift+click (range mode) with no prior anchor treats click as single toggle", () => {
  // Arrange — render with no prior anchor (fresh page)
  render(<PhotobankPage />);

  // Act — simulate Shift+click on photo 2 (mode=range, no anchor set)
  act(() => {
    capturedGridProps.onPhotoSelection(2, "range");
  });

  // Assert — no anchor means range falls through to single toggle:
  // photo 2 is selected and the bulk action bar appears
  expect(screen.getByTestId("bulk-action-bar")).toBeInTheDocument();
});

test("Cmd+click then Shift+click replaces selection with range (not additive)", () => {
  // Arrange
  render(<PhotobankPage />);

  // Act — Cmd+click photo 1 (anchor=1, selected={1})
  act(() => {
    capturedGridProps.onPhotoSelection(1, "toggle");
  });
  // Shift+click photo 3 (range from anchor=1 to 3, REPLACE)
  act(() => {
    capturedGridProps.onPhotoSelection(3, "range");
  });

  // Assert — bulk action bar shows 3 selected (photos 1, 2, 3)
  expect(screen.getByText(/^3 fotek/)).toBeInTheDocument();
});
