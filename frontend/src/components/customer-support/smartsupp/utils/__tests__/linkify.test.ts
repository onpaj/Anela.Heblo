import { splitLinks, isRenderableUrl } from "../linkify";

describe("splitLinks", () => {
  it("keeps trailing period outside the link", () => {
    expect(splitLinks("Viz https://anela.cz/produkt.")).toEqual([
      { kind: "text", text: "Viz " },
      { kind: "link", text: "https://anela.cz/produkt", href: "https://anela.cz/produkt" },
      { kind: "text", text: "." },
    ]);
  });

  it("links www hosts with https href and keeps brackets as text", () => {
    expect(splitLinks("(www.anela.cz)")).toEqual([
      { kind: "text", text: "(" },
      { kind: "link", text: "www.anela.cz", href: "https://www.anela.cz" },
      { kind: "text", text: ")" },
    ]);
  });

  it("never links javascript: urls", () => {
    expect(splitLinks("javascript:alert(1)")).toEqual([{ kind: "text", text: "javascript:alert(1)" }]);
  });

  it("leaves markdown-like text untouched", () => {
    expect(splitLinks("*tucne*")).toEqual([{ kind: "text", text: "*tucne*" }]);
  });

  it("finds two urls in one line", () => {
    const links = splitLinks("https://a.cz a https://b.cz").filter((s) => s.kind === "link");
    expect(links).toHaveLength(2);
  });

  it("returns empty array for empty text", () => {
    expect(splitLinks("")).toEqual([]);
  });
});

describe("isRenderableUrl", () => {
  it("accepts https only by default", () => {
    expect(isRenderableUrl("https://x.cz/a.png")).toBe(true);
    expect(isRenderableUrl("http://x.cz/a.png")).toBe(false);
    expect(isRenderableUrl("javascript:alert(1)")).toBe(false);
    expect(isRenderableUrl("blob:https://x/1")).toBe(false);
    expect(isRenderableUrl(undefined)).toBe(false);
  });

  it("accepts blob when allowed", () => {
    expect(isRenderableUrl("blob:https://x/1", true)).toBe(true);
  });
});
