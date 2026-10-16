import React from "react";
import { render, screen } from "@testing-library/react";
import MessageAttachments from "../MessageAttachments";
import { MessageAttachmentDto } from "../../../../api/generated/api-client";

const blobImage = { type: "image", fileName: "a.png", url: "blob:http://localhost/1" } as MessageAttachmentDto;

describe("MessageAttachments", () => {
  it("drops blob urls by default", () => {
    render(<MessageAttachments attachments={[blobImage]} tone="agent" />);
    expect(screen.queryByRole("img")).not.toBeInTheDocument();
  });

  it("renders blob urls when allowBlob is set", () => {
    render(<MessageAttachments attachments={[blobImage]} tone="agent" allowBlob />);
    expect(screen.getByRole("img")).toHaveAttribute("src", "blob:http://localhost/1");
  });

  it("falls back to Příloha when fileName is empty and titles the chip with the full name", () => {
    const image = { type: "image", fileName: "", url: "https://files.smartsupp.com/i.png" } as MessageAttachmentDto;
    const file = { type: "file", fileName: "dlouhy-nazev.pdf", url: "https://files.smartsupp.com/d.pdf" } as MessageAttachmentDto;
    const unnamed = { type: "file", fileName: "", url: "https://files.smartsupp.com/u.pdf" } as MessageAttachmentDto;
    render(<MessageAttachments attachments={[image, file, unnamed]} tone="visitor" />);
    expect(screen.getByRole("img")).toHaveAttribute("alt", "Příloha");
    expect(screen.getByText("dlouhy-nazev.pdf")).toHaveAttribute("title", "dlouhy-nazev.pdf");
    expect(screen.getByText("Příloha")).toHaveAttribute("title", "Příloha");
  });

  it("formats sizes", () => {
    const mk = (size: number) =>
      ({ type: "file", fileName: `f${size}.pdf`, url: "https://files.smartsupp.com/f", size } as MessageAttachmentDto);
    render(<MessageAttachments attachments={[mk(500), mk(2 * 1024 * 1024 + 300000)]} tone="visitor" />);
    expect(screen.getByText(/500 B/)).toBeInTheDocument();
    expect(screen.getByText(/2,3 MB/)).toBeInTheDocument();
  });
});
