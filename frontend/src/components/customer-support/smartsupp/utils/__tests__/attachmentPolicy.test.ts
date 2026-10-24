import { validateNewFiles, MAX_FILE_BYTES } from "../attachmentPolicy";

const file = (name: string, type = "", size = 10) => {
  const f = new File(["x"], name, { type });
  Object.defineProperty(f, "size", { value: size });
  return f;
};

describe("validateNewFiles", () => {
  it("accepts a .docx with empty declared type", () => {
    const r = validateNewFiles([], [file("a.docx")]);
    expect(r.accepted).toHaveLength(1);
    expect(r.error).toBeNull();
  });

  it("accepts an office file declared as octet-stream", () => {
    const r = validateNewFiles([], [file("a.xlsx", "application/octet-stream")]);
    expect(r.accepted).toHaveLength(1);
  });

  it("rejects a declared type that contradicts the extension", () => {
    const r = validateNewFiles([], [file("a.pdf", "image/png")]);
    expect(r.accepted).toHaveLength(0);
    expect(r.error).toBe("Soubor a.pdf má nepodporovaný typ.");
  });

  it("rejects .exe with the type message", () => {
    const r = validateNewFiles([], [file("virus.exe")]);
    expect(r.accepted).toHaveLength(0);
    expect(r.error).toBe("Soubor virus.exe má nepodporovaný typ.");
  });

  it("accepts only the free slots and reports too many", () => {
    const current = [1, 2, 3, 4].map((i) => file(`c${i}.png`));
    const r = validateNewFiles(current, [file("a.png"), file("b.png")]);
    expect(r.accepted).toHaveLength(1);
    expect(r.error).toBe("Najednou lze poslat nejvýše 5 souborů.");
  });

  it("rejects a file over 10 MB", () => {
    const r = validateNewFiles([], [file("big.pdf", "application/pdf", MAX_FILE_BYTES + 1)]);
    expect(r.accepted).toHaveLength(0);
    expect(r.error).toBe("Soubor big.pdf je větší než 10 MB.");
  });
});
