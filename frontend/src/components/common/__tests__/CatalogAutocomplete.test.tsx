import React from "react";
import { act, render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { CatalogAutocomplete } from "../CatalogAutocomplete";
import { CatalogItemDto, ProductType } from "../../../api/generated/api-client";
import { useCatalogAutocomplete } from "../../../api/hooks/useCatalogAutocomplete";
import { ThemeProvider } from "../../../contexts/ThemeContext";

jest.mock("../../../api/hooks/useCatalogAutocomplete");

jest.mock("../../../contexts/ThemeContext", () => ({
  useTheme: () => ({ theme: "light", toggle: jest.fn() }),
  ThemeProvider: ({ children }: any) => children,
}));
const mockUseCatalogAutocomplete = useCatalogAutocomplete as jest.MockedFunction<
  typeof useCatalogAutocomplete
>;

// Use jest.fn() inside factory (jest is always in scope).
// Retrieve mock references via jest.requireMock() to avoid hoisting/TDZ issues.
jest.mock("react-select", () => ({
  __esModule: true,
  default: jest.fn(() => null),
  // Pass-through wrappers so the custom Option/SingleValue content can be rendered in tests.
  components: {
    Option: ({ children }: any) => children,
    SingleValue: ({ children }: any) => children,
  },
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
    <ThemeProvider>{children}</ThemeProvider>
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

describe("CatalogAutocomplete multi-select", () => {
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

  it("renders a multi-value option for every selected value in isMulti mode", () => {
    render(
      <TestWrapper>
        <CatalogAutocomplete
          isMulti
          values={[
            { productCode: "PROD-A", productName: "Krém" } as any,
            { productCode: "PROD-B", productName: "Mýdlo" } as any,
          ]}
          onSelect={jest.fn()}
          onSelectMany={jest.fn()}
        />
      </TestWrapper>
    );

    expect(MockSelect).toHaveBeenCalled();
    const selectProps = MockSelect.mock.calls[0][0];

    // One select option per selected value == one chip rendered by react-select in multi mode
    expect(selectProps.isMulti).toBe(true);
    expect(selectProps.value).toHaveLength(2);
    expect(selectProps.value[0]).toEqual(
      expect.objectContaining({ label: expect.stringContaining("Krém") })
    );
    expect(selectProps.value[1]).toEqual(
      expect.objectContaining({ label: expect.stringContaining("Mýdlo") })
    );
  });

  it("calls onSelectMany with an empty array when the selection is cleared", () => {
    const onSelectMany = jest.fn();

    render(
      <TestWrapper>
        <CatalogAutocomplete
          isMulti
          values={[{ productCode: "PROD-A", productName: "Krém" } as any]}
          onSelect={jest.fn()}
          onSelectMany={onSelectMany}
        />
      </TestWrapper>
    );

    const selectProps = MockSelect.mock.calls[0][0];

    // Removing the last remaining chip is reported by react-select as an empty MultiValue array
    act(() => {
      selectProps.onChange([], { action: "remove-value" });
    });

    expect(onSelectMany).toHaveBeenCalledWith([]);
  });

  it("single-select mode still calls onSelect and ignores onSelectMany, without multi-value chips", () => {
    const onSelect = jest.fn();
    const onSelectMany = jest.fn();

    render(
      <TestWrapper>
        <CatalogAutocomplete
          value={{ productCode: "PROD-A", productName: "Krém" } as any}
          onSelect={onSelect}
          onSelectMany={onSelectMany}
        />
      </TestWrapper>
    );

    const selectProps = MockSelect.mock.calls[0][0];

    // No multi-value chips in single mode: isMulti is falsy and value is a single option, not an array
    expect(selectProps.isMulti).toBeFalsy();
    expect(Array.isArray(selectProps.value)).toBe(false);

    act(() => {
      selectProps.onChange(
        {
          value: "PROD-B",
          label: "Mýdlo (PROD-B)",
          productCode: "PROD-B",
          productName: "Mýdlo",
        },
        { action: "select-option" }
      );
    });

    expect(onSelect).toHaveBeenCalled();
    expect(onSelectMany).not.toHaveBeenCalled();
  });
});

describe("CatalogAutocomplete product name suffix", () => {
  let MockSelect: jest.Mock;

  const suffixedItem = new CatalogItemDto({
    productCode: "MAT100",
    productName: "Acmella In-Tense extrakt",
    productNameSuffix: "Gatuline Expression AF",
    type: ProductType.Material,
  });

  const lastSelectProps = () =>
    MockSelect.mock.calls[MockSelect.mock.calls.length - 1][0];

  beforeEach(() => {
    jest.clearAllMocks();
    MockSelect = jest.requireMock("react-select").default as jest.Mock;

    mockUseCatalogAutocomplete.mockReturnValue({
      data: { items: [suffixedItem, mockCatalogItem] },
      isLoading: false,
      error: null,
    } as any);
  });

  it("builds inline option labels with the suffix and unchanged labels without it", () => {
    // Arrange & Act
    render(
      <TestWrapper>
        <CatalogAutocomplete value={null} onSelect={jest.fn()} />
      </TestWrapper>
    );

    // Assert
    const options = lastSelectProps().options;
    expect(options[0].label).toBe(
      "Acmella In-Tense extrakt · Gatuline Expression AF (MAT100)"
    );
    expect(options[1].label).toBe("Test Material (TEST001)");
  });

  it("renders the suffix as a muted second line in the option list", () => {
    // Arrange
    render(
      <TestWrapper>
        <CatalogAutocomplete value={null} onSelect={jest.fn()} />
      </TestWrapper>
    );
    const { components: selectComponents, options } = lastSelectProps();
    const Option = selectComponents.Option;

    // Act
    render(<Option data={options[0]} />);

    // Assert
    expect(screen.getByText("Acmella In-Tense extrakt", { exact: false })).toBeInTheDocument();
    const suffixLine = screen.getByTestId("product-name-suffix");
    expect(suffixLine).toHaveTextContent("Gatuline Expression AF");
    expect(suffixLine).toHaveClass("text-xs", "text-gray-500");
  });

  it("renders no suffix line for an option without a suffix", () => {
    // Arrange
    render(
      <TestWrapper>
        <CatalogAutocomplete value={null} onSelect={jest.fn()} />
      </TestWrapper>
    );
    const { components: selectComponents, options } = lastSelectProps();
    const Option = selectComponents.Option;

    // Act
    render(<Option data={options[1]} />);

    // Assert
    expect(screen.getByText("Test Material", { exact: false })).toBeInTheDocument();
    expect(screen.queryByTestId("product-name-suffix")).not.toBeInTheDocument();
  });

  it("shows the selected value inline as Name · Suffix", () => {
    // Arrange
    render(
      <TestWrapper>
        <CatalogAutocomplete value={suffixedItem} onSelect={jest.fn()} />
      </TestWrapper>
    );
    const { components: selectComponents, value } = lastSelectProps();
    const SingleValue = selectComponents.SingleValue;

    // Act
    render(<SingleValue data={value} />);

    // Assert
    expect(value.label).toBe(
      "Acmella In-Tense extrakt · Gatuline Expression AF (MAT100)"
    );
    expect(
      screen.getByText("Acmella In-Tense extrakt · Gatuline Expression AF")
    ).toBeInTheDocument();
    expect(screen.queryByTestId("product-name-suffix")).not.toBeInTheDocument();
  });

  it("passes the suffix through to the selected item", () => {
    // Arrange
    const onSelect = jest.fn();
    render(
      <TestWrapper>
        <CatalogAutocomplete value={null} onSelect={onSelect} />
      </TestWrapper>
    );
    const { options, onChange } = lastSelectProps();

    // Act
    act(() => {
      onChange(options[0], { action: "select-option" });
    });

    // Assert
    expect(onSelect).toHaveBeenCalledWith(
      expect.objectContaining({ productNameSuffix: "Gatuline Expression AF" })
    );
  });

  it("builds inline multi-value chip labels with the suffix", () => {
    // Arrange & Act
    render(
      <TestWrapper>
        <CatalogAutocomplete
          isMulti
          values={[suffixedItem, mockCatalogItem]}
          onSelect={jest.fn()}
          onSelectMany={jest.fn()}
        />
      </TestWrapper>
    );

    // Assert
    const chips = lastSelectProps().value;
    expect(chips[0].label).toBe(
      "Acmella In-Tense extrakt · Gatuline Expression AF (MAT100)"
    );
    expect(chips[1].label).toBe("Test Material (TEST001)");
  });
});
