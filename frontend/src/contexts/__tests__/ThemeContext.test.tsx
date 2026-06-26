import { render, screen, act } from "@testing-library/react";
import { ThemeProvider, useTheme } from "../ThemeContext";

const STORAGE_KEY = "anela-theme";

// Mock window.matchMedia (always reports dark, to prove the OS preference is ignored)
const createMatchMediaMock = (matches: boolean) => {
  return (query: string) => ({
    matches,
    media: query,
    onchange: null,
    addListener: jest.fn(),
    removeListener: jest.fn(),
    addEventListener: jest.fn(),
    removeEventListener: jest.fn(),
    dispatchEvent: jest.fn(),
  });
};

const TestConsumer = () => {
  const { theme, toggle } = useTheme();
  return (
    <div>
      <span data-testid="theme">{theme}</span>
      <button onClick={toggle}>toggle</button>
    </div>
  );
};

const renderWithProvider = () =>
  render(
    <ThemeProvider>
      <TestConsumer />
    </ThemeProvider>,
  );

describe("ThemeContext", () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.classList.remove("dark");
    Object.defineProperty(window, "matchMedia", {
      writable: true,
      value: createMatchMediaMock(true),
    });
  });

  it("defaults to light when there is no saved preference, even if the OS prefers dark", () => {
    renderWithProvider();

    expect(screen.getByTestId("theme")).toHaveTextContent("light");
    expect(document.documentElement.classList.contains("dark")).toBe(false);
  });

  it("uses the saved dark preference", () => {
    localStorage.setItem(STORAGE_KEY, "dark");

    renderWithProvider();

    expect(screen.getByTestId("theme")).toHaveTextContent("dark");
    expect(document.documentElement.classList.contains("dark")).toBe(true);
  });

  it("uses the saved light preference", () => {
    localStorage.setItem(STORAGE_KEY, "light");

    renderWithProvider();

    expect(screen.getByTestId("theme")).toHaveTextContent("light");
  });

  it("persists the new theme to localStorage when toggled", () => {
    renderWithProvider();

    act(() => {
      screen.getByText("toggle").click();
    });

    expect(screen.getByTestId("theme")).toHaveTextContent("dark");
    expect(localStorage.getItem(STORAGE_KEY)).toBe("dark");
    expect(document.documentElement.classList.contains("dark")).toBe(true);

    act(() => {
      screen.getByText("toggle").click();
    });

    expect(screen.getByTestId("theme")).toHaveTextContent("light");
    expect(localStorage.getItem(STORAGE_KEY)).toBe("light");
    expect(document.documentElement.classList.contains("dark")).toBe(false);
  });
});
