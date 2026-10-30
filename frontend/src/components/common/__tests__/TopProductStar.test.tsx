import React from "react";
import { render, screen } from "@testing-library/react";
import TopProductStar from "../TopProductStar";

describe("TopProductStar", () => {
  it("renders an accessible star when the product is a top product", () => {
    render(<TopProductStar isTopProduct={true} />);

    const star = screen.getByTestId("top-product-star");
    expect(star).toBeInTheDocument();
    expect(star).toHaveAttribute("title", "Top produkt");
    expect(screen.getByLabelText("Top produkt")).toBeInTheDocument();
  });

  it.each([false, null, undefined])(
    "renders nothing when isTopProduct is %s",
    (value) => {
      const { container } = render(<TopProductStar isTopProduct={value} />);

      expect(container).toBeEmptyDOMElement();
    },
  );

  it("appends a custom className to the star", () => {
    render(<TopProductStar isTopProduct={true} className="ml-2" />);

    expect(screen.getByTestId("top-product-star").getAttribute("class")).toContain("ml-2");
  });
});
