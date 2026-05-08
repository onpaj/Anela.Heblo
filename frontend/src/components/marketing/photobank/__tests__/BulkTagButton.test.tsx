import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import BulkTagButton from "../BulkTagButton";

// ---- Helpers ----------------------------------------------------------------

const DEFAULT_PROPS = {
  search: "",
  selectedTagNames: [] as string[],
  totalMatching: 0,
  onOpenDialog: jest.fn(),
};

function renderButton(props: Partial<typeof DEFAULT_PROPS> = {}) {
  return render(<BulkTagButton {...DEFAULT_PROPS} {...props} />);
}

// ---- Tests ------------------------------------------------------------------

beforeEach(() => {
  (DEFAULT_PROPS.onOpenDialog as jest.Mock).mockClear();
});

test("renders Otagovat label", () => {
  renderButton();

  expect(screen.getByRole("button", { name: /Otagovat/i })).toBeInTheDocument();
});

test("is disabled with 'Nejprve použijte filtr' tooltip when no filters are active", () => {
  renderButton({ search: "", selectedTagNames: [], totalMatching: 0 });

  const btn = screen.getByRole("button", { name: /Otagovat/i });
  expect(btn).toBeDisabled();
  expect(btn).toHaveAttribute("title", "Nejprve použijte filtr");
});

test("is disabled with 'Žádné fotky neodpovídají filtru' tooltip when filters active but totalMatching is 0", () => {
  renderButton({ search: "test", selectedTagNames: [], totalMatching: 0 });

  const btn = screen.getByRole("button", { name: /Otagovat/i });
  expect(btn).toBeDisabled();
  expect(btn).toHaveAttribute("title", "Žádné fotky neodpovídají filtru");
});

test("is enabled and calls onOpenDialog when filters active and totalMatching > 0", () => {
  renderButton({ search: "test", selectedTagNames: [], totalMatching: 5 });

  const btn = screen.getByRole("button", { name: /Otagovat/i });
  expect(btn).not.toBeDisabled();
  expect(btn).not.toHaveAttribute("title");

  fireEvent.click(btn);

  expect(DEFAULT_PROPS.onOpenDialog).toHaveBeenCalledTimes(1);
});
