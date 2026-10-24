import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import EmojiPicker, { COMPOSER_EMOJI } from "../EmojiPicker";

const open = () => fireEvent.click(screen.getByRole("button", { name: "Vložit emoji" }));

describe("EmojiPicker", () => {
  it("opens a grid of 24 emoji buttons", () => {
    render(<EmojiPicker onPick={jest.fn()} disabled={false} />);
    open();
    expect(screen.getByRole("listbox")).toBeInTheDocument();
    expect(screen.getAllByRole("option")).toHaveLength(COMPOSER_EMOJI.length);
    expect(COMPOSER_EMOJI).toHaveLength(24);
  });

  it("calls onPick and closes when an emoji is clicked", () => {
    const onPick = jest.fn();
    render(<EmojiPicker onPick={onPick} disabled={false} />);
    open();
    fireEvent.click(screen.getByText("🌿"));
    expect(onPick).toHaveBeenCalledWith("🌿");
    expect(screen.queryAllByRole("option")).toHaveLength(0);
  });

  it("closes on Escape", () => {
    render(<EmojiPicker onPick={jest.fn()} disabled={false} />);
    open();
    fireEvent.keyDown(document, { key: "Escape" });
    expect(screen.queryAllByRole("option")).toHaveLength(0);
  });

  it("closes on outside mousedown", () => {
    render(<EmojiPicker onPick={jest.fn()} disabled={false} />);
    open();
    fireEvent.mouseDown(document.body);
    expect(screen.queryAllByRole("option")).toHaveLength(0);
  });

  it("disables the trigger when disabled", () => {
    render(<EmojiPicker onPick={jest.fn()} disabled />);
    expect(screen.getByRole("button", { name: "Vložit emoji" })).toBeDisabled();
  });
});
