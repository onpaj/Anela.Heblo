import React from "react";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import UserProfile from "./UserProfile";

let mockTokenRoles = ["super_user"];

const mockBaseAuth = {
  isAuthenticated: true,
  login: jest.fn(),
  logout: jest.fn(),
  inProgress: "none",
  getUserInfo: () => ({
    name: "Test User",
    email: "test@anela.cz",
    initials: "TU",
    roles: mockTokenRoles,
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

const openPanel = async () => {
  render(<UserProfile />);
  await userEvent.click(screen.getByRole("button"));
};

describe("UserProfile permissions display", () => {
  beforeEach(() => {
    mockTokenRoles = ["super_user"];
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

    await openPanel();

    expect(screen.getByText("Oprávnění")).toBeInTheDocument();
    expect(screen.getByText("catalog.read")).toBeInTheDocument();
    expect(screen.getByText("journal.read")).toBeInTheDocument();
  });

  it("renders super-user banner alongside full permission list when isSuperUser is true", async () => {
    mockCtx = {
      permissions: ["catalog.read", "journal.read", "finance.read"],
      isSuperUser: true,
      groups: [],
      isLoading: false,
      hasPermission: () => true,
    };

    await openPanel();

    expect(screen.getByText("Super User · vše povoleno")).toBeInTheDocument();
    expect(screen.getByText("catalog.read")).toBeInTheDocument();
    expect(screen.getByText("journal.read")).toBeInTheDocument();
    expect(screen.getByText("finance.read")).toBeInTheDocument();
  });

  it("includes super_user role chip when isSuperUser is true but token roles omit it", async () => {
    mockTokenRoles = ["heblo_user"];
    mockCtx = {
      permissions: ["catalog.read"],
      isSuperUser: true,
      groups: [],
      isLoading: false,
      hasPermission: () => true,
    };

    await openPanel();

    expect(screen.getByText("super_user")).toBeInTheDocument();
    expect(screen.getByText("heblo_user")).toBeInTheDocument();
  });

  it("renders Groups chips when user belongs to DB groups", async () => {
    mockCtx = {
      permissions: ["catalog.read"],
      isSuperUser: false,
      groups: ["Finance", "Marketing"],
      isLoading: false,
      hasPermission: () => true,
    };

    await openPanel();

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

    await openPanel();

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

    await openPanel();

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

    await openPanel();

    expect(screen.queryByText("Oprávnění")).not.toBeInTheDocument();
  });

  it("closes the panel when the trigger is clicked a second time", async () => {
    mockCtx = {
      permissions: ["catalog.read"],
      isSuperUser: false,
      groups: [],
      isLoading: false,
      hasPermission: () => true,
    };

    render(<UserProfile />);
    const button = screen.getByRole("button");

    await userEvent.click(button);
    expect(screen.getByText("Oprávnění")).toBeInTheDocument();

    await userEvent.click(button);
    await waitFor(() => {
      expect(screen.queryByText("Oprávnění")).not.toBeInTheDocument();
    });
  });
});
