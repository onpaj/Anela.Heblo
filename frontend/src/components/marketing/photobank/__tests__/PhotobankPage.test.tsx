import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import PhotobankPage from "../pages/PhotobankPage";

jest.mock("@azure/msal-react", () => ({
  useMsal: () => ({ accounts: [] }),
}));

jest.mock("../../../../api/hooks/usePhotobank", () => ({
  usePhotos: () => ({ data: undefined, isLoading: false }),
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

jest.mock("../PhotoGrid", () => ({
  __esModule: true,
  default: () => <div data-testid="photo-grid" />,
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
