import React from "react";
import { render, waitFor } from "@testing-library/react";
import AuthGuard from "../AuthGuard";
import { useAuth } from "../../../auth/useAuth";
import { useMockAuth, shouldUseMockAuth } from "../../../auth/mockAuth";
import { useE2EAuth, isE2ETestMode } from "../../../auth/e2eAuth";

const mockNavigate = jest.fn();

jest.mock("react-router-dom", () => ({
  useNavigate: () => mockNavigate,
}));

jest.mock("../../../auth/useAuth", () => ({
  useAuth: jest.fn(),
}));

jest.mock("../../../auth/mockAuth", () => ({
  useMockAuth: jest.fn(),
  shouldUseMockAuth: jest.fn().mockReturnValue(false),
}));

jest.mock("../../../auth/e2eAuth", () => ({
  useE2EAuth: jest.fn(),
  isE2ETestMode: jest.fn().mockReturnValue(false),
}));

const mockUseAuth = useAuth as jest.MockedFunction<typeof useAuth>;
const mockUseMockAuth = useMockAuth as jest.MockedFunction<typeof useMockAuth>;
const mockUseE2EAuth = useE2EAuth as jest.MockedFunction<typeof useE2EAuth>;

const noopAuth = {
  isAuthenticated: true,
  inProgress: "none" as const,
  login: jest.fn().mockResolvedValue(undefined),
};

beforeEach(() => {
  jest.clearAllMocks();
  mockUseMockAuth.mockReturnValue(noopAuth);
  mockUseE2EAuth.mockReturnValue(noopAuth);
  localStorage.clear();
  Object.defineProperty(window, "location", {
    value: { pathname: "/dashboard", search: "" },
    writable: true,
  });
});

describe("AuthGuard - returnUrl navigation", () => {
  test("navigates to returnUrl when authenticated and returnUrl differs from current path", async () => {
    localStorage.setItem("auth.returnUrl", "/catalog");
    Object.defineProperty(window, "location", {
      value: { pathname: "/dashboard", search: "" },
      writable: true,
    });

    mockUseAuth.mockReturnValue({
      isAuthenticated: true,
      inProgress: "none",
      login: jest.fn().mockResolvedValue(undefined),
    });

    render(
      <AuthGuard>
        <div>content</div>
      </AuthGuard>
    );

    await waitFor(() => {
      expect(mockNavigate).toHaveBeenCalledWith("/catalog");
    });
  });

  test("removes returnUrl from localStorage after navigating", async () => {
    localStorage.setItem("auth.returnUrl", "/catalog");
    Object.defineProperty(window, "location", {
      value: { pathname: "/dashboard", search: "" },
      writable: true,
    });

    mockUseAuth.mockReturnValue({
      isAuthenticated: true,
      inProgress: "none",
      login: jest.fn().mockResolvedValue(undefined),
    });

    render(
      <AuthGuard>
        <div>content</div>
      </AuthGuard>
    );

    await waitFor(() => {
      expect(localStorage.getItem("auth.returnUrl")).toBeNull();
    });
  });

  test("does not navigate when returnUrl equals current pathname", async () => {
    localStorage.setItem("auth.returnUrl", "/dashboard");
    Object.defineProperty(window, "location", {
      value: { pathname: "/dashboard", search: "" },
      writable: true,
    });

    mockUseAuth.mockReturnValue({
      isAuthenticated: true,
      inProgress: "none",
      login: jest.fn().mockResolvedValue(undefined),
    });

    render(
      <AuthGuard>
        <div>content</div>
      </AuthGuard>
    );

    await new Promise((r) => setTimeout(r, 50));
    expect(mockNavigate).not.toHaveBeenCalled();
  });

  test("does not navigate when returnUrl is absent", async () => {
    Object.defineProperty(window, "location", {
      value: { pathname: "/dashboard", search: "" },
      writable: true,
    });

    mockUseAuth.mockReturnValue({
      isAuthenticated: true,
      inProgress: "none",
      login: jest.fn().mockResolvedValue(undefined),
    });

    render(
      <AuthGuard>
        <div>content</div>
      </AuthGuard>
    );

    await new Promise((r) => setTimeout(r, 50));
    expect(mockNavigate).not.toHaveBeenCalled();
  });

  test("does not navigate when not yet authenticated", async () => {
    localStorage.setItem("auth.returnUrl", "/catalog");

    mockUseAuth.mockReturnValue({
      isAuthenticated: false,
      inProgress: "none",
      login: jest.fn().mockResolvedValue(undefined),
    });

    render(
      <AuthGuard>
        <div>content</div>
      </AuthGuard>
    );

    await new Promise((r) => setTimeout(r, 50));
    expect(mockNavigate).not.toHaveBeenCalled();
  });

  test("does not navigate when inProgress is not none", async () => {
    localStorage.setItem("auth.returnUrl", "/catalog");

    mockUseAuth.mockReturnValue({
      isAuthenticated: true,
      inProgress: "login",
      login: jest.fn().mockResolvedValue(undefined),
    });

    render(
      <AuthGuard>
        <div>content</div>
      </AuthGuard>
    );

    await new Promise((r) => setTimeout(r, 50));
    expect(mockNavigate).not.toHaveBeenCalled();
  });
});

// Suppress unused variable warnings for mock typed variables
void mockUseMockAuth;
void mockUseE2EAuth;
