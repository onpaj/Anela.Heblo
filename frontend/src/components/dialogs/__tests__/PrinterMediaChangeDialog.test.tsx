import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import "@testing-library/jest-dom";
import PrinterMediaChangeDialog from "../PrinterMediaChangeDialog";

describe("PrinterMediaChangeDialog", () => {
  it("renders nothing when closed", () => {
    render(
      <PrinterMediaChangeDialog isOpen={false} onConfirm={jest.fn()} onCancel={jest.fn()} />,
    );
    expect(
      screen.queryByTestId("printer-media-change-dialog"),
    ).not.toBeInTheDocument();
  });

  it("shows the change-and-calibrate warning when open", () => {
    render(
      <PrinterMediaChangeDialog isOpen={true} onConfirm={jest.fn()} onCancel={jest.fn()} />,
    );
    expect(screen.getByText(/Výměna média/i)).toBeInTheDocument();
    expect(screen.getByText(/vyměnili a zkalibrovali médium/i)).toBeInTheDocument();
  });

  it("fires onConfirm and onCancel", () => {
    const onConfirm = jest.fn();
    const onCancel = jest.fn();
    render(
      <PrinterMediaChangeDialog isOpen={true} onConfirm={onConfirm} onCancel={onCancel} />,
    );

    fireEvent.click(screen.getByRole("button", { name: /Pokračovat v tisku/i }));
    expect(onConfirm).toHaveBeenCalledTimes(1);

    fireEvent.click(screen.getByRole("button", { name: /Zrušit/i }));
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it("disables the actions while a confirmed print is pending", () => {
    render(
      <PrinterMediaChangeDialog
        isOpen={true}
        isPending={true}
        onConfirm={jest.fn()}
        onCancel={jest.fn()}
      />,
    );
    expect(screen.getByRole("button", { name: /Tisknu/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /Zrušit/i })).toBeDisabled();
  });
});
