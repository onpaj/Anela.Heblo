import React from "react";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import Layout from "../Layout";

jest.mock("../Sidebar", () => ({ __esModule: true, default: () => <div data-testid="sidebar" /> }));
jest.mock("../TopBar", () => ({ __esModule: true, default: (_props: unknown) => <div data-testid="topbar" /> }));
jest.mock("../../common/MobileNotice", () => ({
  MobileNotice: () => <div data-testid="mobile-notice" />,
}));

const wrap = (path: string) => (
  <MemoryRouter initialEntries={[path]}>
    <Layout>
      <div>Content</div>
    </Layout>
  </MemoryRouter>
);

describe("Layout", () => {
  it("does not show MobileNotice on the dashboard page", () => {
    render(wrap("/"));
    expect(screen.queryByTestId("mobile-notice")).not.toBeInTheDocument();
  });

  it("shows MobileNotice on other pages", () => {
    render(wrap("/manufacture"));
    expect(screen.getByTestId("mobile-notice")).toBeInTheDocument();
  });

  it("does not show MobileNotice on the smartsupp page", () => {
    render(wrap("/customer/smartsupp"));
    expect(screen.queryByTestId("mobile-notice")).not.toBeInTheDocument();
  });
});
