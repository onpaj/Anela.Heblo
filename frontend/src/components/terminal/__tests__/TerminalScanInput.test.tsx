import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import TerminalScanInput from "../TerminalScanInput";

describe("TerminalScanInput", () => {
  it("submits a trimmed, uppercased value and clears the field", () => {
    const onScan = jest.fn();
    render(<TerminalScanInput label="Kód boxu" onScan={onScan} />);

    const input = screen.getByTestId("terminal-scan-input") as HTMLInputElement;
    fireEvent.change(input, { target: { value: "  b001  " } });
    fireEvent.click(screen.getByTestId("terminal-scan-submit"));

    expect(onScan).toHaveBeenCalledWith("B001");
    expect(input.value).toBe("");
  });

  it("does not submit an empty value", () => {
    const onScan = jest.fn();
    render(<TerminalScanInput label="Kód boxu" onScan={onScan} />);

    fireEvent.click(screen.getByTestId("terminal-scan-submit"));

    expect(onScan).not.toHaveBeenCalled();
  });
});
