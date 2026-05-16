import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import ScanBoxStep from "../ScanBoxStep";
import * as useBoxFill from "../../../../api/hooks/useBoxFill";

jest.mock("../../../../utils/errorHandler", () => ({
  getErrorMessage: () => "Chyba kódu",
}));

const mockMutateAsync = jest.fn();

const scan = (code: string) => {
  const input = screen.getByRole('textbox');
  fireEvent.change(input, { target: { value: code } });
  fireEvent.submit(input.closest('form')!);
};

describe("ScanBoxStep", () => {
  beforeEach(() => {
    mockMutateAsync.mockReset();
    jest.spyOn(useBoxFill, "useOpenOrResumeBox").mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: false,
    } as unknown as ReturnType<typeof useBoxFill.useOpenOrResumeBox>);
  });

  afterEach(() => jest.restoreAllMocks());

  it("rejects an invalid box code without calling the API", () => {
    render(<ScanBoxStep onBoxReady={jest.fn()} />);
    scan("XYZ");
    expect(screen.getByRole("alert")).toBeInTheDocument();
    expect(mockMutateAsync).not.toHaveBeenCalled();
  });

  it("calls onBoxReady when the box opens successfully", async () => {
    const box = { id: 1, code: "B001", state: "Opened", itemCount: 0, items: [] };
    mockMutateAsync.mockResolvedValue({ success: true, resumed: true, transportBox: box });
    const onBoxReady = jest.fn();

    render(<ScanBoxStep onBoxReady={onBoxReady} />);
    scan("B001");

    await waitFor(() => expect(onBoxReady).toHaveBeenCalledWith(box, true));
  });

  it("shows an error when the API reports failure", async () => {
    mockMutateAsync.mockResolvedValue({ success: false });
    render(<ScanBoxStep onBoxReady={jest.fn()} />);
    scan("B001");
    expect(await screen.findByRole("alert")).toBeInTheDocument();
  });
});
