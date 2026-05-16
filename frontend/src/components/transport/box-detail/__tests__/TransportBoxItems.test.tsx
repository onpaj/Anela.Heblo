import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import TransportBoxItems from "../TransportBoxItems";
import { TransportBoxDto, TransportBoxItemDto } from "../../../../api/generated/api-client";

jest.mock("../../../../api/hooks/useManufacturedProductInventory", () => ({
  useManufacturedProductInventoryQuery: () => ({
    data: { items: [] },
    isLoading: false,
    error: null,
  }),
}));

jest.mock("../../../common/CatalogAutocomplete", () => ({
  CatalogAutocomplete: () => null,
}));

function makeItem(overrides: Partial<TransportBoxItemDto> = {}): TransportBoxItemDto {
  const item = new TransportBoxItemDto();
  item.id = 1;
  item.productCode = "P001";
  item.productName = "Test Product";
  item.amount = 10;
  item.imageUrl = undefined;
  item.dateAdded = new Date("2024-01-01T10:00:00Z");
  item.userAdded = "Jan Novák";
  return Object.assign(item, overrides);
}

function makeBox(items: TransportBoxItemDto[]): TransportBoxDto {
  const box = new TransportBoxDto();
  box.id = 1;
  box.code = "B001";
  box.state = "New";
  box.items = items;
  box.stateLog = [];
  box.allowedTransitions = [];
  return box;
}

const defaultProps = {
  isFormEditable: jest.fn((_field: "items" | "notes" | "boxNumber") => false),
  formatDate: () => "01.01.2024",
  handleRemoveItem: jest.fn(),
  quantityInput: "",
  setQuantityInput: jest.fn(),
  selectedProduct: null,
  setSelectedProduct: jest.fn(),
  handleAddItem: jest.fn(),
  handleAddManufacturedItem: jest.fn(),
  lastAddedItem: null,
  handleQuickAdd: jest.fn(),
  lastManufacturedItems: [],
};

describe("TransportBoxItems — product thumbnails", () => {
  it("renders an img element when item has imageUrl", () => {
    const item = makeItem({ imageUrl: "https://example.com/product.jpg" });
    render(<TransportBoxItems {...defaultProps} transportBox={makeBox([item])} />);

    const img = screen.getByRole("img");
    expect(img).toBeInTheDocument();
    expect(img).toHaveAttribute("src", "https://example.com/product.jpg");
    expect(img).toHaveAttribute("alt", "Test Product");
  });

  it("renders a grey placeholder when imageUrl is undefined", () => {
    const item = makeItem({ imageUrl: undefined });
    render(<TransportBoxItems {...defaultProps} transportBox={makeBox([item])} />);

    expect(screen.queryByRole("img")).not.toBeInTheDocument();
    expect(screen.getByTestId("product-thumbnail-placeholder")).toBeInTheDocument();
  });

  it("still shows product name and code alongside the image", () => {
    const item = makeItem({ imageUrl: "https://example.com/product.jpg" });
    render(<TransportBoxItems {...defaultProps} transportBox={makeBox([item])} />);

    expect(screen.getByText("Test Product")).toBeInTheDocument();
    expect(screen.getByText("P001")).toBeInTheDocument();
  });

  it("shows grey placeholder when image fails to load", () => {
    const item = makeItem({ imageUrl: "https://example.com/broken.jpg" });
    render(
      <TransportBoxItems {...defaultProps} transportBox={makeBox([item])} />
    );

    const img = screen.getByRole("img");
    fireEvent.error(img);

    expect(screen.queryByRole("img")).not.toBeInTheDocument();
    expect(screen.getByTestId("product-thumbnail-placeholder")).toBeInTheDocument();
  });
});
