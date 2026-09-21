import React from "react";
import fs from "fs";
import path from "path";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import MarginLevelsHelpSheet, { MARGIN_LEVELS_DOC_URL } from "../MarginLevelsHelpSheet";

// react-markdown ships ESM that react-scripts' jest transform does not handle;
// the other suites in this repo stub it the same way.
jest.mock("react-markdown", () => ({
  __esModule: true,
  default: ({ children }: { children: string }) => <div data-testid="markdown">{children}</div>,
}));
jest.mock("remark-gfm", () => ({ __esModule: true, default: () => {} }));

describe("MarginLevelsHelpSheet", () => {
  const originalFetch = global.fetch;
  let consoleErrorSpy: jest.SpyInstance;

  beforeEach(() => {
    consoleErrorSpy = jest.spyOn(console, "error").mockImplementation(() => {});
  });

  afterEach(() => {
    global.fetch = originalFetch;
    consoleErrorSpy.mockRestore();
    jest.clearAllMocks();
  });

  const markdownResponse = (body: string, contentType = "text/markdown; charset=utf-8") => ({
    ok: true,
    headers: new Headers({ "content-type": contentType }),
    text: async () => body,
  });

  const mockFetch = (impl: () => Promise<Partial<Response>>) => {
    global.fetch = jest.fn(impl) as unknown as typeof fetch;
  };

  test("fetches the document from the deployed public path", async () => {
    // Arrange
    mockFetch(async () => markdownResponse("obsah"));

    // Act
    render(<MarginLevelsHelpSheet onClose={jest.fn()} />);
    await screen.findByTestId("markdown");

    // Assert - the literal path, not the constant the component itself used
    expect(global.fetch).toHaveBeenCalledWith("/docs/margin-levels.md");
  });

  test("the fetched path matches a file that actually exists in public/", () => {
    // Arrange
    const publicDir = path.join(__dirname, "..", "..", "..", "..", "public");

    // Act
    const servedFile = path.join(publicDir, MARGIN_LEVELS_DOC_URL);

    // Assert
    expect(fs.existsSync(servedFile)).toBe(true);
  });

  test("renders the served document once it loads", async () => {
    // Arrange
    mockFetch(async () => markdownResponse("# Hladiny marže\n\nM0 je materiál."));

    // Act
    render(<MarginLevelsHelpSheet onClose={jest.fn()} />);

    // Assert
    expect(await screen.findByTestId("markdown")).toHaveTextContent("M0 je materiál.");
  });

  test("shows an error message when the document cannot be fetched", async () => {
    // Arrange
    mockFetch(async () => ({ ok: false, status: 404, text: async () => "" }));

    // Act
    render(<MarginLevelsHelpSheet onClose={jest.fn()} />);

    // Assert
    expect(await screen.findByText("Dokumentaci se nepodařilo načíst.")).toBeInTheDocument();
    expect(screen.queryByTestId("markdown")).not.toBeInTheDocument();
  });

  test("shows an error message when the request rejects", async () => {
    // Arrange
    mockFetch(async () => {
      throw new Error("offline");
    });

    // Act
    render(<MarginLevelsHelpSheet onClose={jest.fn()} />);

    // Assert
    expect(await screen.findByText("Dokumentaci se nepodařilo načíst.")).toBeInTheDocument();
  });

  test("treats the SPA index.html fallback as a failure rather than rendering it", async () => {
    // Arrange - UseSpa answers 200 + text/html for an unknown path
    mockFetch(async () => markdownResponse("<!doctype html><html lang=\"cs\">", "text/html"));

    // Act
    render(<MarginLevelsHelpSheet onClose={jest.fn()} />);

    // Assert
    expect(await screen.findByText("Dokumentaci se nepodařilo načíst.")).toBeInTheDocument();
    expect(screen.queryByTestId("markdown")).not.toBeInTheDocument();
  });

  test("logs the underlying cause when loading fails", async () => {
    // Arrange
    mockFetch(async () => ({ ok: false, status: 500, text: async () => "" }));

    // Act
    render(<MarginLevelsHelpSheet onClose={jest.fn()} />);
    await screen.findByText("Dokumentaci se nepodařilo načíst.");

    // Assert
    expect(consoleErrorSpy).toHaveBeenCalledWith(
      expect.stringContaining("nápovědu"),
      MARGIN_LEVELS_DOC_URL,
      expect.objectContaining({ message: "HTTP 500" }),
    );
  });

  test("moves focus into the dialog so the document is readable by keyboard", async () => {
    // Arrange
    mockFetch(async () => markdownResponse("obsah"));

    // Act
    render(<MarginLevelsHelpSheet onClose={jest.fn()} />);
    await screen.findByTestId("markdown");

    // Assert
    expect(screen.getByRole("dialog")).toHaveFocus();
  });

  test("closes on the close button, on the backdrop and on Escape", async () => {
    // Arrange
    mockFetch(async () => markdownResponse("obsah"));
    const onClose = jest.fn();
    render(<MarginLevelsHelpSheet onClose={onClose} />);
    await screen.findByTestId("markdown");
    const backdrop = screen.getByTestId("margin-levels-help-sheet");

    // Act & Assert
    fireEvent.click(screen.getByLabelText("Zavřít"));
    expect(onClose).toHaveBeenCalledTimes(1);

    fireEvent.mouseDown(backdrop);
    fireEvent.mouseUp(backdrop);
    expect(onClose).toHaveBeenCalledTimes(2);

    fireEvent.keyDown(document, { key: "Escape" });
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(3));
  });

  test("does not close when the dialog body itself is clicked", async () => {
    // Arrange
    mockFetch(async () => markdownResponse("obsah"));
    const onClose = jest.fn();
    render(<MarginLevelsHelpSheet onClose={onClose} />);
    await screen.findByTestId("markdown");
    const dialog = screen.getByRole("dialog");

    // Act
    fireEvent.mouseDown(dialog);
    fireEvent.mouseUp(dialog);

    // Assert
    expect(onClose).not.toHaveBeenCalled();
  });

  test("does not close when a text selection started inside the dialog ends on the backdrop", async () => {
    // Arrange - drag-selecting a paragraph and releasing past the dialog edge
    mockFetch(async () => markdownResponse("obsah"));
    const onClose = jest.fn();
    render(<MarginLevelsHelpSheet onClose={onClose} />);
    await screen.findByTestId("markdown");

    // Act
    fireEvent.mouseDown(screen.getByRole("dialog"));
    fireEvent.mouseUp(screen.getByTestId("margin-levels-help-sheet"));

    // Assert
    expect(onClose).not.toHaveBeenCalled();
  });
});
