import React from "react";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import UserProfile from "./UserProfile";

const mockBaseAuth = {
  isAuthenticated: true,
  login: jest.fn(),
  logout: jest.fn(),
  inProgress: "none",
  getUserInfo: () => ({
    name: "Test User",
    email: "test@anela.cz",
    initials: "TU",
    roles: ["super_user"],
  }),
  getStoredUserInfo: () => null,
};

interface MockPermissionsCtx {
  permissions: string[];
  isSuperUser: boolean;
  groups: string[];
  isLoading: boolean;
  hasPermission: (p: string) => boolean;
}

let mockCtx: MockPermissionsCtx = {
  permissions: [],
  isSuperUser: false,
  groups: [],
  isLoading: false,
  hasPermission: (_: string) => false,
};

jest.mock("../../auth/useAuth", () => ({ useAuth: () => mockBaseAuth }));
jest.mock("../../auth/mockAuth", () => ({
  useMockAuth: () => mockBaseAuth,
  shouldUseMockAuth: () => false,
}));
jest.mock("../../auth/PermissionsContext", () => ({
  usePermissionsContext: () => mockCtx,
}));

const openModal = async () => {
  render(<UserProfile />);
  await userEvent.click(screen.getByRole("button"));
};

describe("UserProfile permissions display", () => {
  beforeEach(() => {
    mockCtx = {
      permissions: [],
      isSuperUser: false,
      groups: [],
      isLoading: false,
      hasPermission: () => false,
    };
  });

  it("renders Permissions chips when user has DB permissions", async () => {
    mockCtx = {
      permissions: ["catalog.read", "journal.read"],
      isSuperUser: false,
      groups: [],
      isLoading: false,
      hasPermission: () => true,
    };

    await openModal();

    expect(screen.getByText("Oprávnění")).toBeInTheDocument();
    expect(screen.getByText("catalog.read")).toBeInTheDocument();
    expect(screen.getByText("journal.read")).toBeInTheDocument();
  });

  it("renders a single super-user badge when isSuperUser is true", async () => {
    mockCtx = {
      permissions: ["catalog.read", "journal.read", "finance.read"],
      isSuperUser: true,
      groups: [],
      isLoading: false,
      hasPermission: () => true,
    };

    await openModal();

    expect(screen.getByText("Super User · vše povoleno")).toBeInTheDocument();
    expect(screen.queryByText("catalog.read")).not.toBeInTheDocument();
    expect(screen.queryByText("journal.read")).not.toBeInTheDocument();
  });

  it("renders Groups chips when user belongs to DB groups", async () => {
    mockCtx = {
      permissions: ["catalog.read"],
      isSuperUser: false,
      groups: ["Finance", "Marketing"],
      isLoading: false,
      hasPermission: () => true,
    };

    await openModal();

    expect(screen.getByText("Skupiny")).toBeInTheDocument();
    expect(screen.getByText("Finance")).toBeInTheDocument();
    expect(screen.getByText("Marketing")).toBeInTheDocument();
  });

  it("hides Groups section when user has no groups", async () => {
    mockCtx = {
      permissions: ["catalog.read"],
      isSuperUser: false,
      groups: [],
      isLoading: false,
      hasPermission: () => true,
    };

    await openModal();

    expect(screen.queryByText("Skupiny")).not.toBeInTheDocument();
  });

  it("hides Permissions and Groups sections while loading", async () => {
    mockCtx = {
      permissions: ["catalog.read"],
      isSuperUser: false,
      groups: ["Finance"],
      isLoading: true,
      hasPermission: () => true,
    };

    await openModal();

    expect(screen.queryByText("Oprávnění")).not.toBeInTheDocument();
    expect(screen.queryByText("Skupiny")).not.toBeInTheDocument();
  });

  it("hides Permissions section when non-super-user has no permissions", async () => {
    mockCtx = {
      permissions: [],
      isSuperUser: false,
      groups: [],
      isLoading: false,
      hasPermission: () => false,
    };

    await openModal();

    expect(screen.queryByText("Oprávnění")).not.toBeInTheDocument();
  });
});
