import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import ErrorBoundary from "../ErrorBoundary";

let shouldThrow = true;

const Thrower: React.FC<{ message?: string }> = ({ message = "Boom" }) => {
  if (shouldThrow) {
    throw new Error(message);
  }
  return <div>Child content</div>;
};

const customFallbackSpy = jest.fn();
const CustomFallback: React.FC<{
  error?: Error;
  resetError: () => void;
}> = (props) => {
  customFallbackSpy(props);
  return (
    <div>
      <div>Custom fallback: {props.error?.message}</div>
      <button onClick={props.resetError}>Custom reset</button>
    </div>
  );
};

describe("ErrorBoundary", () => {
  let consoleErrorSpy: jest.SpyInstance;

  beforeEach(() => {
    shouldThrow = true;
    customFallbackSpy.mockClear();
    consoleErrorSpy = jest.spyOn(console, "error").mockImplementation(() => {});
  });

  afterEach(() => {
    consoleErrorSpy.mockRestore();
  });

  it("renders children when no error", () => {
    shouldThrow = false;
    render(
      <ErrorBoundary>
        <Thrower />
      </ErrorBoundary>,
    );
    expect(screen.getByText("Child content")).toBeInTheDocument();
    expect(
      screen.queryByText("Nastala chyba při načítání komponenty"),
    ).not.toBeInTheDocument();
  });

  it("renders default fallback UI with error message when child throws", () => {
    render(
      <ErrorBoundary>
        <Thrower />
      </ErrorBoundary>,
    );
    expect(
      screen.getByText("Nastala chyba při načítání komponenty"),
    ).toBeInTheDocument();
    expect(screen.getByText(/Boom/)).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Zkusit znovu" }),
    ).toBeInTheDocument();
    expect(screen.queryByText("Child content")).not.toBeInTheDocument();
  });

  it("logs the error via console.error from componentDidCatch", () => {
    render(
      <ErrorBoundary>
        <Thrower />
      </ErrorBoundary>,
    );
    expect(consoleErrorSpy).toHaveBeenCalledWith(
      "ErrorBoundary caught an error:",
      expect.any(Error),
    );
  });

  it("shows 'Neznámá chyba' when error message is empty", () => {
    render(
      <ErrorBoundary>
        <Thrower message="" />
      </ErrorBoundary>,
    );
    expect(screen.getByText(/Neznámá chyba/)).toBeInTheDocument();
  });

  it("resets state and re-mounts child when 'Zkusit znovu' is clicked", () => {
    render(
      <ErrorBoundary>
        <Thrower />
      </ErrorBoundary>,
    );
    shouldThrow = false;
    fireEvent.click(screen.getByRole("button", { name: "Zkusit znovu" }));
    expect(screen.getByText("Child content")).toBeInTheDocument();
    expect(
      screen.queryByText("Nastala chyba při načítání komponenty"),
    ).not.toBeInTheDocument();
  });

  it("re-shows error UI if child still throws after reset", () => {
    render(
      <ErrorBoundary>
        <Thrower />
      </ErrorBoundary>,
    );
    fireEvent.click(screen.getByRole("button", { name: "Zkusit znovu" }));
    expect(
      screen.getByText("Nastala chyba při načítání komponenty"),
    ).toBeInTheDocument();
    expect(screen.queryByText("Child content")).not.toBeInTheDocument();
  });

  it("passes error and resetError to custom fallbackComponent and hides default UI", () => {
    render(
      <ErrorBoundary fallbackComponent={CustomFallback}>
        <Thrower />
      </ErrorBoundary>,
    );
    expect(screen.getByText(/Custom fallback: Boom/)).toBeInTheDocument();
    const props = customFallbackSpy.mock.calls[0][0];
    expect(props.error).toBeInstanceOf(Error);
    expect(props.error.message).toBe("Boom");
    expect(typeof props.resetError).toBe("function");
    expect(
      screen.queryByText("Nastala chyba při načítání komponenty"),
    ).not.toBeInTheDocument();
  });

  it("custom fallback resetError clears state and re-mounts child", () => {
    render(
      <ErrorBoundary fallbackComponent={CustomFallback}>
        <Thrower />
      </ErrorBoundary>,
    );
    shouldThrow = false;
    fireEvent.click(screen.getByRole("button", { name: "Custom reset" }));
    expect(screen.getByText("Child content")).toBeInTheDocument();
    expect(screen.queryByText(/Custom fallback/)).not.toBeInTheDocument();
  });
});
