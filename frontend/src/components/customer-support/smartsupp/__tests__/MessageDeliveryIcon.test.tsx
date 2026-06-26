import React from "react";
import { render, screen } from "@testing-library/react";
import MessageDeliveryIcon from "../MessageDeliveryIcon";

describe("MessageDeliveryIcon", () => {
  it("renders nothing when status is null or empty", () => {
    const { container } = render(<MessageDeliveryIcon status={null} />);
    expect(container).toBeEmptyDOMElement();
  });

  it("renders a tooltip with the localized label for 'delivered'", () => {
    render(<MessageDeliveryIcon status="delivered" />);
    expect(screen.getByTestId("delivery-icon")).toHaveAttribute("title", "Doručeno");
  });

  it("renders a tooltip with the localized label for 'failed'", () => {
    render(<MessageDeliveryIcon status="failed" />);
    expect(screen.getByTestId("delivery-icon")).toHaveAttribute("title", "Doručení selhalo");
  });

  it("renders a tooltip with the localized label for 'pending'", () => {
    render(<MessageDeliveryIcon status="pending" />);
    expect(screen.getByTestId("delivery-icon")).toHaveAttribute("title", "Odesílá se");
  });

  it("falls back to the raw status as tooltip for unknown values", () => {
    render(<MessageDeliveryIcon status="weird" />);
    expect(screen.getByTestId("delivery-icon")).toHaveAttribute("title", "weird");
  });
});
