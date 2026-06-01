import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import VisitorInfoCard from "../VisitorInfoCard";
import { useSmartsuppVisitorInfo } from "../../../../api/hooks/useSmartsupp";

jest.mock("../../../../api/hooks/useSmartsupp", () => ({
  ...jest.requireActual("../../../../api/hooks/useSmartsupp"),
  useSmartsuppVisitorInfo: jest.fn(),
}));

const mockUseVisitorInfo = useSmartsuppVisitorInfo as jest.Mock;

const wrap = (ui: React.ReactNode) => {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}>{ui}</QueryClientProvider>;
};

describe("VisitorInfoCard", () => {
  it("renders nothing while loading", () => {
    mockUseVisitorInfo.mockReturnValue({ data: undefined, isLoading: true });
    const { container } = render(wrap(<VisitorInfoCard conversationId="c1" />));
    expect(container.firstChild).toBeNull();
  });

  it("renders nothing when no visitor info", () => {
    mockUseVisitorInfo.mockReturnValue({ data: null, isLoading: false });
    const { container } = render(wrap(<VisitorInfoCard conversationId="c1" />));
    expect(container.firstChild).toBeNull();
  });

  it("renders device info in Zařízení section", () => {
    mockUseVisitorInfo.mockReturnValue({
      isLoading: false,
      data: {
        success: true,
        visitorInfo: {
          os: "OS X",
          browser: "Chrome",
          browserVersion: "148.0.0.0",
          visitsCount: 321,
          chatsCount: 3,
          pages: [],
        },
      },
    });
    render(wrap(<VisitorInfoCard conversationId="c1" />));
    expect(screen.getByText("Zařízení")).toBeInTheDocument();
    expect(screen.getByText("Chrome 148.0.0.0, OS X")).toBeInTheDocument();
  });

  it("renders browsing history pages", () => {
    mockUseVisitorInfo.mockReturnValue({
      isLoading: false,
      data: {
        success: true,
        visitorInfo: {
          os: null,
          browser: null,
          browserVersion: null,
          visitsCount: null,
          chatsCount: 1,
          pages: [
            { url: "https://www.anela.cz/product" },
            { url: "https://www.anela.cz/checkout" },
          ],
        },
      },
    });
    render(wrap(<VisitorInfoCard conversationId="c1" />));
    expect(screen.getByText("Historie procházení")).toBeInTheDocument();
    expect(screen.getByText("https://www.anela.cz/product")).toBeInTheDocument();
    expect(screen.getByText("https://www.anela.cz/checkout")).toBeInTheDocument();
  });

  it("collapses pages beyond 3 and expands on click", () => {
    const pages = Array.from({ length: 5 }, (_, i) => ({
      url: `https://www.anela.cz/page${i + 1}`,
    }));
    mockUseVisitorInfo.mockReturnValue({
      isLoading: false,
      data: {
        success: true,
        visitorInfo: { os: null, browser: null, browserVersion: null, visitsCount: null, chatsCount: 1, pages },
      },
    });
    render(wrap(<VisitorInfoCard conversationId="c1" />));

    expect(screen.getByText("https://www.anela.cz/page1")).toBeInTheDocument();
    expect(screen.queryByText("https://www.anela.cz/page5")).not.toBeInTheDocument();
    expect(screen.getByText("+ 2 stránky")).toBeInTheDocument();

    fireEvent.click(screen.getByText("+ 2 stránky"));
    expect(screen.getByText("https://www.anela.cz/page5")).toBeInTheDocument();
    expect(screen.queryByText("+ 2 stránky")).not.toBeInTheDocument();
  });
});
