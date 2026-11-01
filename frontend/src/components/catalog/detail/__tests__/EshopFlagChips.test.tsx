import React from "react";
import { render, screen } from "@testing-library/react";
import EshopFlagChips from "../EshopFlagChips";

describe("EshopFlagChips", () => {
  it("renders e-shop flag chips", () => {
    render(
      <EshopFlagChips
        flags={[
          { code: "bestseller", title: "Bestseller" },
          { code: "sale", title: "Výprodej" },
        ]}
      />,
    );

    expect(screen.getByText("Bestseller")).toBeInTheDocument();
    expect(screen.getByText("Výprodej")).toBeInTheDocument();
    expect(screen.getAllByTestId("eshop-flag-chip")).toHaveLength(2);
  });

  it.each([[undefined], [[]]])("renders nothing when flags are %p", (flags) => {
    const { container } = render(<EshopFlagChips flags={flags as any} />);

    expect(container).toBeEmptyDOMElement();
  });
});
