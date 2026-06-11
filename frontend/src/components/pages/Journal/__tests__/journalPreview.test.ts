import { truncateContent, MAX_PREVIEW_LENGTH } from "../journalPreview";

describe("truncateContent", () => {
  describe("empty/falsy content", () => {
    it("returns empty string for empty content", () => {
      expect(truncateContent("")).toBe("");
    });
  });

  describe("no searchQuery (head-truncate mode)", () => {
    it("returns content as-is when shorter than maxLength", () => {
      const content = "short text";
      expect(truncateContent(content)).toBe(content);
    });

    it("truncates at maxLength and appends ellipsis when content exceeds limit", () => {
      const content = "A".repeat(250);
      const result = truncateContent(content);
      expect(result).toBe("A".repeat(MAX_PREVIEW_LENGTH) + "...");
    });

    it("returns content exactly at maxLength without ellipsis", () => {
      const content = "A".repeat(MAX_PREVIEW_LENGTH);
      expect(truncateContent(content)).toBe(content);
    });

    it("respects custom maxLength option", () => {
      const content = "A".repeat(50);
      const result = truncateContent(content, { maxLength: 30 });
      expect(result).toBe("A".repeat(30) + "...");
    });

    it("treats whitespace-only searchQuery as no query (head-truncate fallback)", () => {
      const content = "A".repeat(250);
      const result = truncateContent(content, { searchQuery: "   " });
      expect(result).toBe("A".repeat(MAX_PREVIEW_LENGTH) + "...");
    });
  });

  describe("searchQuery with match in content", () => {
    it("centers window on first match occurrence", () => {
      const prefix = "p".repeat(300);
      const suffix = "s".repeat(300);
      const content = prefix + "needle" + suffix;
      const result = truncateContent(content, { searchQuery: "needle" });
      expect(result).toContain("needle");
      expect(result.startsWith("...")).toBe(true);
      expect(result.endsWith("...")).toBe(true);
    });

    it("does not prepend ellipsis when match is at start of content", () => {
      const content = "needle" + "s".repeat(300);
      const result = truncateContent(content, { searchQuery: "needle" });
      expect(result).toContain("needle");
      expect(result.startsWith("...")).toBe(false);
      expect(result.endsWith("...")).toBe(true);
    });

    it("matches case-insensitively", () => {
      const content = "This contains NEEDLE in it and more text padding padding padding padding";
      const result = truncateContent(content, { searchQuery: "needle" });
      expect(result.toLowerCase()).toContain("needle");
    });

    it("window length does not exceed maxLength plus ellipsis overhead", () => {
      const prefix = "p".repeat(300);
      const suffix = "s".repeat(300);
      const content = prefix + "needle" + suffix;
      const result = truncateContent(content, { searchQuery: "needle" });
      // Max is maxLength chars + up to 6 chars for leading/trailing "..."
      expect(result.length).toBeLessThanOrEqual(MAX_PREVIEW_LENGTH + 6);
    });
  });

  describe("searchQuery with no match in content (fallback)", () => {
    it("falls back to head-truncate when query does not match", () => {
      const content = "A".repeat(250);
      const result = truncateContent(content, { searchQuery: "zzz" });
      expect(result).toBe("A".repeat(MAX_PREVIEW_LENGTH) + "...");
    });

    it("returns full content when shorter than maxLength and query does not match", () => {
      const content = "short text here";
      const result = truncateContent(content, { searchQuery: "zzz" });
      expect(result).toBe(content);
    });
  });
});
