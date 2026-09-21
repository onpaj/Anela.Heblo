import React from "react";
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

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  const mockFetch = (impl: () => Promise<Partial<Response>>) => {
    global.fetch = jest.fn(impl) as unknown as typeof fetch;
  };

  test("renders the served document once it loads", async () => {
    // Arrange
    mockFetch(async () => ({ ok: true, text: async () => "# Hladiny marže\n\nM0 je materiál." }));

    // Act
    render(<MarginLevelsHelpSheet onClose={jest.fn()} />);

    // Assert
    expect(await screen.findByTestId("markdown")).toHaveTextContent("M0 je materiál.");
    expect(global.fetch).toHaveBeenCalledWith(MARGIN_LEVELS_DOC_URL);
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

  test("closes on the close button, on the backdrop and on Escape", async () => {
    // Arrange
    mockFetch(async () => ({ ok: true, text: async () => "obsah" }));
    const onClose = jest.fn();
    render(<MarginLevelsHelpSheet onClose={onClose} />);
    await screen.findByTestId("markdown");

    // Act & Assert
    fireEvent.click(screen.getByLabelText("Zavřít"));
    expect(onClose).toHaveBeenCalledTimes(1);

    fireEvent.click(screen.getByTestId("margin-levels-help-sheet"));
    expect(onClose).toHaveBeenCalledTimes(2);

    fireEvent.keyDown(document, { key: "Escape" });
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(3));
  });

  test("does not close when the dialog body itself is clicked", async () => {
    // Arrange
    mockFetch(async () => ({ ok: true, text: async () => "obsah" }));
    const onClose = jest.fn();
    render(<MarginLevelsHelpSheet onClose={onClose} />);
    await screen.findByTestId("markdown");

    // Act
    fireEvent.click(screen.getByRole("dialog"));

    // Assert
    expect(onClose).not.toHaveBeenCalled();
  });
});
