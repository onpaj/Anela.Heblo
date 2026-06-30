import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import TransportBoxList from "../TransportBoxList";
import {
  useTransportBoxesQuery,
  useTransportBoxSummaryQuery,
} from "../../../api/hooks/useTransportBoxes";
import { useStockUpOperationsSummary } from "../../../api/hooks/useStockUpOperations";
import { TestRouterWrapper } from "../../../test-utils/router-wrapper";

// Controllable touch-layout switch (must be prefixed `mock` for jest hoisting).
let mockIsTouchLayout = true;

jest.mock("../../../hooks/useMediaQuery", () => ({
  useIsTouchLayout: () => mockIsTouchLayout,
  useIsMobile: () => false,
  useMediaQuery: () => false,
}));

// Stub the touch panel — we only test the list's switch + wiring here.
jest.mock("../../transport/touch/TransportBoxTouchPanel", () => ({
  __esModule: true,
  default: ({ onOpenBox, onShowAll }: any) => (
    <div data-testid="touch-panel">
      <button onClick={() => onOpenBox(7)}>panel-open</button>
      <button onClick={onShowAll}>panel-show-all</button>
    </div>
  ),
}));

jest.mock("../../../auth/PermissionsContext", () => ({
  usePermissionsContext: () => ({
    permissions: [],
    isSuperUser: false,
    groups: [],
    isLoading: false,
    hasPermission: () => true,
  }),
}));

jest.mock("../../../api/hooks/useTransportBoxes", () => ({
  useTransportBoxesQuery: jest.fn(),
  useTransportBoxSummaryQuery: jest.fn(),
  transportBoxKeys: { all: ["transport-boxes"] },
}));

jest.mock("../../../api/hooks/useStockUpOperations", () => ({
  useStockUpOperationsSummary: jest.fn(),
}));

jest.mock("../../common/CatalogAutocomplete", () => ({
  CatalogAutocomplete: () => <input data-testid="catalog-autocomplete" />,
}));

jest.mock("../TransportBoxDetail", () => {
  return function MockTransportBoxDetail({ isOpen, boxId }: any) {
    return isOpen ? (
      <div data-testid="transport-box-detail-modal">Box ID: {boxId}</div>
    ) : null;
  };
});

jest.mock("../../common/StockUpOperationStatusIndicator", () => {
  return function MockStockUpOperationStatusIndicator() {
    return null;
  };
});

jest.mock("../../../api/client", () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: { transportBox: ["transport-boxes"] },
}));

jest.mock("../../../api/generated/api-client", () => ({
  CreateNewTransportBoxRequest: jest.fn().mockImplementation((data) => data),
  ProductType: {},
  StockUpSourceType: { TransportBox: "TransportBox" },
}));

const mockUseTransportBoxesQuery = useTransportBoxesQuery as jest.Mock;
const mockUseTransportBoxSummaryQuery =
  useTransportBoxSummaryQuery as jest.Mock;
const mockUseStockUpOperationsSummary = useStockUpOperationsSummary as jest.Mock;

const createWrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
  return (
    <TestRouterWrapper>
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    </TestRouterWrapper>
  );
};

describe("TransportBoxList — touch entry switch", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockIsTouchLayout = true;

    mockUseTransportBoxesQuery.mockReturnValue({
      data: { items: [], totalCount: 0, totalPages: 0 },
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    });
    mockUseTransportBoxSummaryQuery.mockReturnValue({
      data: { totalBoxes: 0, activeBoxes: 0, statesCounts: {} },
      isLoading: false,
      error: null,
    });
    mockUseStockUpOperationsSummary.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: null,
    });
  });

  it("renders the touch panel instead of the table on touch layouts", () => {
    render(<TransportBoxList />, { wrapper: createWrapper });

    expect(screen.getByTestId("touch-panel")).toBeInTheDocument();
    expect(screen.queryByText("Filtry")).not.toBeInTheDocument();
  });

  it("opens the shared detail modal from the touch panel", () => {
    render(<TransportBoxList />, { wrapper: createWrapper });

    fireEvent.click(screen.getByText("panel-open"));

    expect(
      screen.getByTestId("transport-box-detail-modal"),
    ).toHaveTextContent("Box ID: 7");
  });

  it("reveals the full table when 'show all' is triggered on touch", () => {
    render(<TransportBoxList />, { wrapper: createWrapper });

    fireEvent.click(screen.getByText("panel-show-all"));

    expect(screen.queryByTestId("touch-panel")).not.toBeInTheDocument();
    expect(screen.getByText("Filtry")).toBeInTheDocument();
    expect(
      screen.getByText("Zpět na dotykové zobrazení"),
    ).toBeInTheDocument();
  });

  it("renders the table (not the panel) on desktop layouts", () => {
    mockIsTouchLayout = false;

    render(<TransportBoxList />, { wrapper: createWrapper });

    expect(screen.queryByTestId("touch-panel")).not.toBeInTheDocument();
    expect(screen.getByText("Filtry")).toBeInTheDocument();
  });
});
