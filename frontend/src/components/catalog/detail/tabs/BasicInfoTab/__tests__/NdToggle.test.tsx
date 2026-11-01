import React from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import NdToggle from "../NdToggle";
import { usePermissionsContext } from "../../../../../../auth/PermissionsContext";
import { useSetNd } from "../../../../../../api/hooks/useNd";

jest.mock("../../../../../../auth/PermissionsContext", () => ({
  usePermissionsContext: jest.fn(),
}));
jest.mock("../../../../../../api/hooks/useNd", () => ({
  useSetNd: jest.fn(),
}));

const CATALOG_WRITE = "products.catalog.write";

interface MutationState {
  isPending?: boolean;
  isError?: boolean;
  variables?: { productCode: string; isNd: boolean };
}

const mutate = jest.fn();

function arrange({
  canWrite = true,
  mutation = {},
}: { canWrite?: boolean; mutation?: MutationState } = {}) {
  (usePermissionsContext as jest.Mock).mockReturnValue({
    hasPermission: (perm: string) => canWrite && perm === CATALOG_WRITE,
  });
  (useSetNd as jest.Mock).mockReturnValue({
    mutate,
    isPending: false,
    isError: false,
    variables: undefined,
    ...mutation,
  });
}

describe("NdToggle", () => {
  beforeEach(() => {
    mutate.mockReset();
  });

  it("explains the ND abbreviation in a help tooltip", () => {
    arrange();

    render(<NdToggle productCode="ABC" isNd={false} />);

    expect(screen.getByTestId("nd-help")).toHaveAttribute("title", "ND = Nesmí dojít");
  });

  it("shows the current state of the flag", () => {
    arrange();

    render(<NdToggle productCode="ABC" isNd={true} />);

    expect(screen.getByRole("checkbox", { name: "ND" })).toBeChecked();
  });

  it("saves the opposite value when the checkbox is clicked", () => {
    arrange();
    render(<NdToggle productCode="ABC" isNd={false} />);

    fireEvent.click(screen.getByRole("checkbox", { name: "ND" }));

    expect(mutate).toHaveBeenCalledWith({ productCode: "ABC", isNd: true });
  });

  it("is read-only without catalog write permission", () => {
    arrange({ canWrite: false });
    render(<NdToggle productCode="ABC" isNd={true} />);

    const checkbox = screen.getByRole("checkbox", { name: "ND" });
    expect(checkbox).toBeChecked();
    expect(checkbox).toBeDisabled();
  });

  it("shows the pending value and blocks further clicks while saving", () => {
    arrange({
      mutation: { isPending: true, variables: { productCode: "ABC", isNd: true } },
    });

    render(<NdToggle productCode="ABC" isNd={false} />);

    const checkbox = screen.getByRole("checkbox", { name: "ND" });
    expect(checkbox).toBeChecked();
    expect(checkbox).toBeDisabled();
  });

  it("shows an error message when saving fails", () => {
    arrange({ mutation: { isError: true } });

    render(<NdToggle productCode="ABC" isNd={false} />);

    expect(screen.getByRole("alert")).toHaveTextContent("nepodařilo uložit");
  });

  it("shows no error message by default", () => {
    arrange();

    render(<NdToggle productCode="ABC" isNd={false} />);

    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});
