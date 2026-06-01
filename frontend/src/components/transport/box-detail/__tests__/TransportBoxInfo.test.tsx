import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import TransportBoxInfo from "../TransportBoxInfo";
import { TransportBoxDto } from "../../../../api/generated/api-client";

const mockTransportBox: TransportBoxDto = {
  id: 1,
  code: "B001",
  state: "Opened",
  itemCount: 5,
  lastStateChanged: "2024-01-01T10:00:00Z",
  description: "Test box description",
  location: null,
  items: [],
  stateLog: [],
  allowedTransitions: [],
};

const defaultProps = {
  transportBox: mockTransportBox,
  descriptionInput: "Test box description",
  handleDescriptionChange: jest.fn(),
  isDescriptionChanged: false,
  isFormEditable: jest.fn((fieldType: string) => fieldType === "notes"),
  formatDate: (date: string | Date | undefined) => "01.01.2024 11:00",
};

describe("TransportBoxInfo", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it("renders basic box information correctly", () => {
    render(<TransportBoxInfo {...defaultProps} />);

    expect(screen.getByText("1")).toBeInTheDocument(); // ID
    expect(screen.getByText("B001")).toBeInTheDocument(); // Code
    expect(screen.getByText("5")).toBeInTheDocument(); // Item count
    expect(screen.getByText("01.01.2024 11:00")).toBeInTheDocument(); // Last changed
  });

  it("shows editable description when form is editable", () => {
    const mockIsFormEditable = jest.fn((fieldType: string) => {
      return fieldType === "notes";
    });

    const props = {
      ...defaultProps,
      isFormEditable: mockIsFormEditable,
    };

    render(<TransportBoxInfo {...props} />);

    const textarea = screen.getByRole("textbox");
    expect(textarea).toBeInTheDocument();
    expect(textarea).toHaveValue("Test box description");
    expect(textarea.tagName.toLowerCase()).toBe("textarea");
  });

  it("shows readonly description when form is not editable", () => {
    const props = {
      ...defaultProps,
      isFormEditable: jest.fn((fieldType) => fieldType !== "notes"), // Make notes non-editable
    };

    render(<TransportBoxInfo {...props} />);

    expect(screen.getByText("Test box description")).toBeInTheDocument();
    expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
  });

  it("shows location when box is in Reserve state", () => {
    const reserveBox = {
      ...mockTransportBox,
      state: "Reserve",
      location: "Warehouse A",
    };

    render(<TransportBoxInfo {...defaultProps} transportBox={reserveBox} />);

    expect(screen.getByText("Warehouse A")).toBeInTheDocument();
  });

  it("calls handleDescriptionChange when description is edited", () => {
    const handleDescriptionChange = jest.fn();
    const props = {
      ...defaultProps,
      handleDescriptionChange,
      isFormEditable: jest.fn((fieldType) => fieldType === "notes"), // Make notes editable
    };

    render(<TransportBoxInfo {...props} />);

    const textarea = screen.getByRole("textbox");
    fireEvent.change(textarea, { target: { value: "Updated description" } });

    expect(handleDescriptionChange).toHaveBeenCalledWith("Updated description");
  });
});
