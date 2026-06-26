import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import OpenBoxByCodeField from "../OpenBoxByCodeField";
import { useOpenOrResumeBox } from "../../../../api/hooks/useBoxFill";

jest.mock("../../../../api/hooks/useBoxFill", () => ({
  useOpenOrResumeBox: jest.fn(),
}));

const mockUseOpenOrResumeBox = useOpenOrResumeBox as jest.Mock;

describe("OpenBoxByCodeField", () => {
  const mutateAsync = jest.fn();
  const onOpenBox = jest.fn();

  beforeEach(() => {
    jest.clearAllMocks();
    mockUseOpenOrResumeBox.mockReturnValue({ mutateAsync, isPending: false });
  });

  const typeCode = (value: string) => {
    fireEvent.change(screen.getByLabelText("Otevřít box"), {
      target: { value },
    });
  };

  it("opens the box and calls onOpenBox with the returned id for a valid code", async () => {
    mutateAsync.mockResolvedValue({
      success: true,
      transportBox: { id: 42, code: "B001" },
    });

    render(<OpenBoxByCodeField onOpenBox={onOpenBox} />);

    typeCode("b001");
    fireEvent.click(screen.getByRole("button", { name: "Otevřít" }));

    await waitFor(() => {
      expect(onOpenBox).toHaveBeenCalledWith(42);
    });
    expect(mutateAsync).toHaveBeenCalledWith("B001");
  });

  it("shows a validation error and does not call the mutation for an invalid code", async () => {
    render(<OpenBoxByCodeField onOpenBox={onOpenBox} />);

    typeCode("XYZ");
    fireEvent.click(screen.getByRole("button", { name: "Otevřít" }));

    await waitFor(() => {
      expect(screen.getByText(/Neplatný kód boxu/i)).toBeInTheDocument();
    });
    expect(mutateAsync).not.toHaveBeenCalled();
    expect(onOpenBox).not.toHaveBeenCalled();
  });

  it("shows an error when the backend reports failure", async () => {
    mutateAsync.mockResolvedValue({ success: false, errorCode: "Conflict" });

    render(<OpenBoxByCodeField onOpenBox={onOpenBox} />);

    typeCode("B999");
    fireEvent.click(screen.getByRole("button", { name: "Otevřít" }));

    await waitFor(() => {
      expect(
        screen.getByText(/Box se nepodařilo otevřít/i),
      ).toBeInTheDocument();
    });
    expect(onOpenBox).not.toHaveBeenCalled();
  });

  it("disables the submit button while a code is empty", () => {
    render(<OpenBoxByCodeField onOpenBox={onOpenBox} />);

    expect(screen.getByRole("button", { name: "Otevřít" })).toBeDisabled();
  });
});
