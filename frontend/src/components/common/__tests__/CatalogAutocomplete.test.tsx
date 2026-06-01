import React from "react";
import { act, render } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { CatalogAutocomplete } from "../CatalogAutocomplete";
import { CatalogItemDto, ProductType } from "../../../api/generated/api-client";
import { useCatalogAutocomplete } from "../../../api/hooks/useCatalogAutocomplete";

jest.mock("../../../api/hooks/useCatalogAutocomplete");
const mockUseCatalogAutocomplete = useCatalogAutocomplete as jest.MockedFunction<
  typeof useCatalogAutocomplete
>;

// Use jest.fn() inside factory (jest is always in scope).
// Retrieve mock references via jest.requireMock() to avoid hoisting/TDZ issues.
jest.mock("react-select", () => ({
  __esModule: true,
  default: jest.fn(() => null),
  components: { Option: () => null, SingleValue: () => null },
}));

const mockCatalogItem = new CatalogItemDto({
  productCode: "TEST001",
  productName: "Test Material",
  type: ProductType.Material,
});

const createTestQueryClient = () =>
  new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

const TestWrapper: React.FC<{ children: React.ReactNode }> = ({ children }) => (
  <QueryClientProvider client={createTestQueryClient()}>
    {children}
  </QueryClientProvider>
);

describe("CatalogAutocomplete", () => {
  let MockSelect: jest.Mock;

  beforeEach(() => {
    jest.clearAllMocks();
    MockSelect = jest.requireMock("react-select").default as jest.Mock;

    mockUseCatalogAutocomplete.mockReturnValue({
      data: { items: [] },
      isLoading: false,
      error: null,
    } as any);
  });

  it("renders the Select component", () => {
    render(
      <TestWrapper>
        <CatalogAutocomplete value={null} onSelect={jest.fn()} />
      </TestWrapper>
    );

    expect(MockSelect).toHaveBeenCalled();
  });

  it("calls onSelect with null when onChange receives null (clear action)", () => {
    const mockOnSelect = jest.fn();

    render(
      <TestWrapper>
        <CatalogAutocomplete
          value={mockCatalogItem}
          onSelect={mockOnSelect}
          clearable={true}
        />
      </TestWrapper>
    );

    expect(MockSelect).toHaveBeenCalled();
    const selectProps = MockSelect.mock.calls[0][0];

    act(() => {
      selectProps.onChange(null, { action: "clear" });
    });

    expect(mockOnSelect).toHaveBeenCalledWith(null);
  });

  it("calls onSelect with adapted item when an option is selected", () => {
    const mockOnSelect = jest.fn();

    render(
      <TestWrapper>
        <CatalogAutocomplete
          value={null}
          onSelect={mockOnSelect}
          itemAdapter={(item) => item.productCode || ""}
        />
      </TestWrapper>
    );

    const selectProps = MockSelect.mock.calls[0][0];

    act(() => {
      selectProps.onChange(
        {
          value: "TEST001",
          label: "Test Material (TEST001)",
          productCode: "TEST001",
          productName: "Test Material",
          data: mockCatalogItem,
        },
        { action: "select-option" }
      );
    });

    expect(mockOnSelect).toHaveBeenCalledWith("TEST001");
  });
});
