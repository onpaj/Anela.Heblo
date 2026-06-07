import React from "react";
import { render, screen } from "@testing-library/react";
import { MemoryRouter, Routes, Route } from "react-router-dom";
import { RequireAccess } from "./RequireAccess";

const mockCtx = {
  permissions: ["catalog.read"],
  isSuperUser: false,
  groups: [],
  isLoading: false,
  hasPermission: (p: string) => ["catalog.read"].includes(p),
};

jest.mock("../../auth/PermissionsContext", () => ({
  usePermissionsContext: () => mockCtx,
}));

const renderAt = (required: string) =>
  render(
    <MemoryRouter initialEntries={["/secret"]}>
      <Routes>
        <Route path="/" element={<div>home</div>} />
        <Route
          path="/secret"
          element={
            <RequireAccess requiredRole={required}>
              <div>secret</div>
            </RequireAccess>
          }
        />
      </Routes>
    </MemoryRouter>,
  );

describe("RequireAccess", () => {
  it("renders children when permission present", () => {
    renderAt("catalog.read");
    expect(screen.getByText("secret")).toBeInTheDocument();
  });

  it("redirects home when permission missing", () => {
    renderAt("journal.read");
    expect(screen.getByText("home")).toBeInTheDocument();
  });
});
