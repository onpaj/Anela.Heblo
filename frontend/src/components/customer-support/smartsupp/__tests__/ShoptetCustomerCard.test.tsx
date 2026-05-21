import React from "react";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import ShoptetCustomerCard from "../ShoptetCustomerCard";
import * as hooks from "../../../../api/hooks/useSmartsupp";

jest.mock("../../../../api/hooks/useSmartsupp", () => ({
  ...jest.requireActual("../../../../api/hooks/useSmartsupp"),
  useSmartsuppShoptetInfo: jest.fn(),
}));

const mockedHook = hooks.useSmartsuppShoptetInfo as jest.Mock;

function wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

const fullResponse: hooks.GetSmartsuppShoptetInfoResponse = {
  success: true,
  contactInfo: {
    customer: {
      fullName: "Jana Nováková",
      email: "jana@test.cz",
      customerGroup: "VIP",
      priceList: "Retail",
      defaultShippingAddress: "CZ, Praha, 12000, Ulice 5",
    },
    recentOrders: [
      {
        code: "2024001",
        statusName: "Balí se",
        totalWithVat: 1250,
        currencyCode: "CZK",
        orderDate: "2026-04-01T00:00:00",
        adminUrl: "https://anela.myshoptet.com/admin/orders/2024001",
      },
    ],
    cartUpdatedAt: null,
  },
};

describe("ShoptetCustomerCard", () => {
  it("renders nothing when hook returns null (404)", () => {
    mockedHook.mockReturnValue({ data: null, isLoading: false });
    const { container } = render(<ShoptetCustomerCard conversationId="c1" />, { wrapper });
    expect(container.firstChild).toBeNull();
  });

  it("renders nothing while loading", () => {
    mockedHook.mockReturnValue({ data: undefined, isLoading: true });
    const { container } = render(<ShoptetCustomerCard conversationId="c1" />, { wrapper });
    expect(container.firstChild).toBeNull();
  });

  it("renders customer name and group", () => {
    mockedHook.mockReturnValue({ data: fullResponse, isLoading: false });
    render(<ShoptetCustomerCard conversationId="c1" />, { wrapper });
    expect(screen.getByText("Jana Nováková")).toBeInTheDocument();
    expect(screen.getByText("VIP")).toBeInTheDocument();
    expect(screen.getByText("Retail")).toBeInTheDocument();
  });

  it("renders recent order with code, status, and total", () => {
    mockedHook.mockReturnValue({ data: fullResponse, isLoading: false });
    render(<ShoptetCustomerCard conversationId="c1" />, { wrapper });
    expect(screen.getByText("2024001")).toBeInTheDocument();
    expect(screen.getByText("Balí se")).toBeInTheDocument();
    expect(screen.getByText(/1 250/)).toBeInTheDocument();
  });

  it("renders admin link for each order", () => {
    mockedHook.mockReturnValue({ data: fullResponse, isLoading: false });
    render(<ShoptetCustomerCard conversationId="c1" />, { wrapper });
    const link = screen.getByRole("link", { name: /zobrazit v shoptet/i });
    expect(link).toHaveAttribute("href", "https://anela.myshoptet.com/admin/orders/2024001");
    expect(link).toHaveAttribute("rel", "noopener noreferrer");
  });

  it("renders shipping address when present", () => {
    mockedHook.mockReturnValue({ data: fullResponse, isLoading: false });
    render(<ShoptetCustomerCard conversationId="c1" />, { wrapper });
    expect(screen.getByText("CZ, Praha, 12000, Ulice 5")).toBeInTheDocument();
  });

  it("renders no customer section when contactInfo.customer is null", () => {
    mockedHook.mockReturnValue({
      data: { success: true, contactInfo: { customer: null, recentOrders: [], cartUpdatedAt: null } },
      isLoading: false,
    });
    render(<ShoptetCustomerCard conversationId="c1" />, { wrapper });
    expect(screen.queryByText("Shoptet Zákazník")).not.toBeInTheDocument();
  });

  it("still renders cart section when customer is null but cartUpdatedAt is set", () => {
    mockedHook.mockReturnValue({
      data: {
        success: true,
        contactInfo: { customer: null, recentOrders: [], cartUpdatedAt: "2026-04-15T12:00:00" },
      },
      isLoading: false,
    });
    render(<ShoptetCustomerCard conversationId="c1" />, { wrapper });
    expect(screen.queryByText("Shoptet Zákazník")).not.toBeInTheDocument();
    expect(screen.getByText("Shoptet Košík")).toBeInTheDocument();
  });
});
