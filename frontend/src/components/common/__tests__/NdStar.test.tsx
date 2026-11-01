import React from "react";
import { render, screen } from "@testing-library/react";
import NdStar from "../NdStar";

describe("NdStar", () => {
  it("renders an accessible star when the product is a top product", () => {
    render(<NdStar isNd={true} />);

    const star = screen.getByTestId("nd-star");
    expect(star).toBeInTheDocument();
    expect(star).toHaveAttribute("title", "ND – Nesmí dojít");
    expect(screen.getByLabelText("ND – Nesmí dojít")).toBeInTheDocument();
  });

  it.each([false, null, undefined])(
    "renders nothing when isNd is %s",
    (value) => {
      const { container } = render(<NdStar isNd={value} />);

      expect(container).toBeEmptyDOMElement();
    },
  );

  it("appends a custom className to the star", () => {
    render(<NdStar isNd={true} className="ml-2" />);

    expect(screen.getByTestId("nd-star").getAttribute("class")).toContain("ml-2");
  });
});
