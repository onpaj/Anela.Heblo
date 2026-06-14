import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import GroupsPicker from "../GroupsPicker";

jest.mock("../../../api/hooks/useAccessManagement", () => ({
  useGroups: () => ({
    data: {
      groups: [
        { id: "g1", name: "Admins", description: "Admin group" },
        { id: "g2", name: "Editors", description: "Editor group" },
      ],
    },
    isLoading: false,
  }),
}));

describe("GroupsPicker", () => {
  it("renders available and assigned column labels", () => {
    render(<GroupsPicker value={[]} onChange={jest.fn()} />);
    expect(screen.getByText("Available groups")).toBeInTheDocument();
    expect(screen.getByText("Member of")).toBeInTheDocument();
  });

  it("shows all groups in available column when none are assigned", () => {
    render(<GroupsPicker value={[]} onChange={jest.fn()} />);
    expect(screen.getByText("Admins")).toBeInTheDocument();
    expect(screen.getByText("Editors")).toBeInTheDocument();
  });

  it("shows assigned group in right column", () => {
    render(<GroupsPicker value={["g1"]} onChange={jest.fn()} />);
    const assigned = screen.getAllByText("Admins");
    expect(assigned.length).toBeGreaterThan(0);
  });

  it("calls onChange with new id set when + button is clicked", () => {
    const onChange = jest.fn();
    render(<GroupsPicker value={[]} onChange={onChange} />);
    fireEvent.click(screen.getByRole("button", { name: /assign admins/i }));
    expect(onChange).toHaveBeenCalledWith(["g1"]);
  });
});
