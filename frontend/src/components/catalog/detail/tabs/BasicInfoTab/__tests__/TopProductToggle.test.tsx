import React from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import TopProductToggle from "../TopProductToggle";
import { usePermissionsContext } from "../../../../../../auth/PermissionsContext";
import { useSetTopProduct } from "../../../../../../api/hooks/useTopProduct";

jest.mock("../../../../../../auth/PermissionsContext", () => ({
  usePermissionsContext: jest.fn(),
}));
jest.mock("../../../../../../api/hooks/useTopProduct", () => ({
  useSetTopProduct: jest.fn(),
}));

const CATALOG_WRITE = "products.catalog.write";

interface MutationState {
  isPending?: boolean;
  isError?: boolean;
  variables?: { productCode: string; isTopProduct: boolean };
}

const mutate = jest.fn();

function arrange({
  canWrite = true,
  mutation = {},
}: { canWrite?: boolean; mutation?: MutationState } = {}) {
  (usePermissionsContext as jest.Mock).mockReturnValue({
    hasPermission: (perm: string) => canWrite && perm === CATALOG_WRITE,
  });
  (useSetTopProduct as jest.Mock).mockReturnValue({
    mutate,
    isPending: false,
    isError: false,
    variables: undefined,
    ...mutation,
  });
}

describe("TopProductToggle", () => {
  beforeEach(() => {
    mutate.mockReset();
  });

  it("shows the current state of the flag", () => {
    arrange();

    render(<TopProductToggle productCode="ABC" isTopProduct={true} />);

    expect(screen.getByRole("checkbox", { name: "Top produkt" })).toBeChecked();
  });

  it("saves the opposite value when the checkbox is clicked", () => {
    arrange();
    render(<TopProductToggle productCode="ABC" isTopProduct={false} />);

    fireEvent.click(screen.getByRole("checkbox", { name: "Top produkt" }));

    expect(mutate).toHaveBeenCalledWith({ productCode: "ABC", isTopProduct: true });
  });

  it("is read-only without catalog write permission", () => {
    arrange({ canWrite: false });
    render(<TopProductToggle productCode="ABC" isTopProduct={true} />);

    const checkbox = screen.getByRole("checkbox", { name: "Top produkt" });
    expect(checkbox).toBeChecked();
    expect(checkbox).toBeDisabled();
  });

  it("shows the pending value and blocks further clicks while saving", () => {
    arrange({
      mutation: { isPending: true, variables: { productCode: "ABC", isTopProduct: true } },
    });

    render(<TopProductToggle productCode="ABC" isTopProduct={false} />);

    const checkbox = screen.getByRole("checkbox", { name: "Top produkt" });
    expect(checkbox).toBeChecked();
    expect(checkbox).toBeDisabled();
  });

  it("shows an error message when saving fails", () => {
    arrange({ mutation: { isError: true } });

    render(<TopProductToggle productCode="ABC" isTopProduct={false} />);

    expect(screen.getByRole("alert")).toHaveTextContent("nepodařilo uložit");
  });

  it("shows no error message by default", () => {
    arrange();

    render(<TopProductToggle productCode="ABC" isTopProduct={false} />);

    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});
