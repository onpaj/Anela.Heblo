import { render, screen } from "@testing-library/react";
import { MemoryRouter, Routes, Route } from "react-router-dom";
import { RequireAccess } from "./RequireAccess";
import { useAuth, UserInfo } from "../../auth/useAuth";

jest.mock("../../auth/useAuth");

const mockUseAuth = useAuth as jest.MockedFunction<typeof useAuth>;

const createMockAuth = (userInfo: UserInfo | null) => ({
  isAuthenticated: userInfo !== null,
  account: null as any, // AccountInfo type not needed for mock
  inProgress: "none" as const,
  login: jest.fn(),
  logout: jest.fn(),
  getAccessToken: jest.fn(),
  getUserInfo: () => userInfo,
  getStoredUserInfo: () => null,
});

describe("RequireAccess", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  test("renders children when the user has the required role", () => {
    mockUseAuth.mockReturnValue(
      createMockAuth({ name: "Test User", email: "test@example.com", initials: "TU", roles: ["catalog.read"] })
    );

    render(
      <MemoryRouter initialEntries={["/catalog"]}>
        <Routes>
          <Route path="/" element={<div>dashboard</div>} />
          <Route path="/catalog" element={<RequireAccess requiredRole="catalog.read"><div>secret</div></RequireAccess>} />
        </Routes>
      </MemoryRouter>,
    );

    expect(screen.getByText("secret")).toBeInTheDocument();
  });

  test("redirects to dashboard when the role is missing", () => {
    mockUseAuth.mockReturnValue(
      createMockAuth({ name: "Test User", email: "test@example.com", initials: "TU", roles: ["catalog.read"] })
    );

    render(
      <MemoryRouter initialEntries={["/finance/overview"]}>
        <Routes>
          <Route path="/" element={<div>dashboard</div>} />
          <Route path="/finance/overview" element={<RequireAccess requiredRole="financial_overview.read"><div>secret</div></RequireAccess>} />
        </Routes>
      </MemoryRouter>,
    );

    expect(screen.getByText("dashboard")).toBeInTheDocument();
    expect(screen.queryByText("secret")).not.toBeInTheDocument();
  });

  test("renders children when requiredRole is undefined", () => {
    mockUseAuth.mockReturnValue(
      createMockAuth({ name: "Test User", email: "test@example.com", initials: "TU", roles: [] })
    );

    render(
      <MemoryRouter initialEntries={["/public"]}>
        <Routes>
          <Route path="/public" element={<RequireAccess><div>public content</div></RequireAccess>} />
        </Routes>
      </MemoryRouter>,
    );

    expect(screen.getByText("public content")).toBeInTheDocument();
  });

  test("renders children when user roles are undefined", () => {
    mockUseAuth.mockReturnValue(
      createMockAuth({ name: "Test User", email: "test@example.com", initials: "TU" })
    );

    render(
      <MemoryRouter initialEntries={["/public"]}>
        <Routes>
          <Route path="/" element={<div>dashboard</div>} />
          <Route path="/public" element={<RequireAccess><div>public content</div></RequireAccess>} />
        </Routes>
      </MemoryRouter>,
    );

    expect(screen.getByText("public content")).toBeInTheDocument();
  });

  test("handles null getUserInfo response", () => {
    mockUseAuth.mockReturnValue(createMockAuth(null));

    render(
      <MemoryRouter initialEntries={["/protected"]}>
        <Routes>
          <Route path="/" element={<div>dashboard</div>} />
          <Route path="/protected" element={<RequireAccess requiredRole="admin"><div>secret</div></RequireAccess>} />
        </Routes>
      </MemoryRouter>,
    );

    expect(screen.getByText("dashboard")).toBeInTheDocument();
    expect(screen.queryByText("secret")).not.toBeInTheDocument();
  });
});
