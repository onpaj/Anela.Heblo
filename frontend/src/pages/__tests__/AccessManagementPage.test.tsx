import React from "react";
import { render, screen } from "@testing-library/react";
import AccessManagementPage from "../AccessManagementPage";

jest.mock("../../api/hooks/useAccessManagement", () => ({
  useGroups: () => ({
    data: {
      groups: [
        {
          id: "1",
          name: "Spravce",
          isSystem: true,
          permissionCount: 52,
          memberCount: 1,
        },
      ],
    },
    isLoading: false,
  }),
  useUsers: () => ({ data: { users: [] }, isLoading: false }),
  useCatalogue: () => ({ data: { permissions: ["catalog.read"] } }),
  useDeleteGroup: () => ({ mutate: jest.fn(), isPending: false }),
  useSetUserActive: () => ({ mutate: jest.fn(), isPending: false }),
}));

describe("AccessManagementPage", () => {
  it("renders groups tab with a system group badge", () => {
    render(<AccessManagementPage />);
    expect(screen.getByText("Spravce")).toBeInTheDocument();
    expect(screen.getByText("system")).toBeInTheDocument();
  });
});
