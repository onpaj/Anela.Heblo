import React from "react";
import { render, screen } from "@testing-library/react";
import JournalEntryModal from "../JournalEntryModal";
import { JournalEntryDto } from "../../api/generated/api-client";

// The form reads its data from a GetJournalEntryResponse wrapper (`entry.entry`).
// Plain components (not jest.fn) — CRA's resetMocks would strip a jest.fn implementation.
jest.mock("../JournalEntryForm", () => ({
  __esModule: true,
  default: ({ entry }: { entry?: { entry?: { id?: number; title?: string } } }) => (
    <div data-testid="form-entry">
      {entry?.entry ? `${entry.entry.id}:${entry.entry.title}` : "none"}
    </div>
  ),
}));

jest.mock("../../api/hooks/useJournal", () => ({
  useDeleteJournalEntry: () => ({ mutateAsync: () => Promise.resolve(), isPending: false }),
}));

describe("JournalEntryModal", () => {
  it("passes the edited entry to the form inside a response wrapper", () => {
    const entry = new JournalEntryDto({ id: 42, title: "Inventura etiket" });

    render(<JournalEntryModal isOpen onClose={() => {}} entry={entry} isEdit />);

    expect(screen.getByTestId("form-entry")).toHaveTextContent("42:Inventura etiket");
  });

  it("passes no entry to the form when creating", () => {
    render(<JournalEntryModal isOpen onClose={() => {}} />);

    expect(screen.getByTestId("form-entry")).toHaveTextContent("none");
  });
});
