import { getTagColor, TAG_PALETTE, OVERLAY_PALETTE } from "../tagColor";

describe("tagColor", () => {
  describe("determinism", () => {
    it("should return the same color for the same name (deterministic)", () => {
      const name = "TestTag";
      const color1 = getTagColor(name);
      const color2 = getTagColor(name);
      const color3 = getTagColor(name);

      expect(color1).toEqual(color2);
      expect(color2).toEqual(color3);
    });

    it("should return exact known colors to detect hash algorithm changes", () => {
      // These assertions pin the exact expected output for specific inputs.
      // If the hash algorithm or palette order changes, these will fail,
      // catching regressions that a determinism-only test would miss.

      // "testtag" (lowercase of "TestTag") hashes to palette index 1 (emerald)
      expect(getTagColor("testtag")).toEqual(TAG_PALETTE[1]);
      expect(getTagColor("TestTag")).toEqual(TAG_PALETTE[1]);
      expect(getTagColor("TESTTAG")).toEqual(TAG_PALETTE[1]);

      // "alpha" hashes to palette index 9 (slate) - different bucket
      expect(getTagColor("alpha")).toEqual(TAG_PALETTE[9]);

      // "example" also hashes to palette index 9 to verify non-trivial collision
      expect(getTagColor("example")).toEqual(TAG_PALETTE[9]);
    });
  });

  describe("palette selection", () => {
    it("should return a valid entry from TAG_PALETTE by default", () => {
      const color = getTagColor("example");
      expect(TAG_PALETTE).toContain(color);
    });

    it("should return a valid entry from OVERLAY_PALETTE when overlay is true", () => {
      const color = getTagColor("example", true);
      expect(OVERLAY_PALETTE).toContain(color);
    });

    it("should return different palettes for overlay true vs false", () => {
      const name = "testname";
      const tagColor = getTagColor(name, false);
      const overlayColor = getTagColor(name, true);

      // They should be different palette entries
      expect(tagColor).not.toEqual(overlayColor);
    });

    it("overlay palette should always have text-white", () => {
      for (let i = 0; i < 100; i++) {
        const color = getTagColor(`tag${i}`, true);
        expect(color.text).toBe("text-white");
      }
    });
  });

  describe("edge cases", () => {
    it("should handle empty string without crashing", () => {
      const color = getTagColor("");
      expect(color).toBeDefined();
      expect(TAG_PALETTE).toContain(color);
    });

    it("should handle very long strings", () => {
      const longName = "a".repeat(1000);
      const color = getTagColor(longName);
      expect(TAG_PALETTE).toContain(color);
    });

    it("should handle special characters", () => {
      const color = getTagColor("tag!@#$%^&*()");
      expect(TAG_PALETTE).toContain(color);
    });
  });

  describe("distribution", () => {
    it("should return indices within palette bounds", () => {
      const names = [
        "alpha",
        "beta",
        "gamma",
        "delta",
        "epsilon",
        "zeta",
        "eta",
        "theta",
      ];

      names.forEach((name) => {
        const color = getTagColor(name);
        const index = TAG_PALETTE.indexOf(color);
        expect(index).toBeGreaterThanOrEqual(0);
        expect(index).toBeLessThan(TAG_PALETTE.length);
      });
    });

    it("should spread tags across different palette colors", () => {
      const colorSet = new Set<string>();

      for (let i = 0; i < 50; i++) {
        const color = getTagColor(`tag${i}`);
        colorSet.add(JSON.stringify(color));
      }

      // Should use multiple colors, not just one
      expect(colorSet.size).toBeGreaterThan(1);
    });
  });

  describe("case insensitivity", () => {
    it("should treat uppercase and lowercase the same", () => {
      const color1 = getTagColor("TestTag");
      const color2 = getTagColor("testtag");
      const color3 = getTagColor("TESTTAG");

      expect(color1).toEqual(color2);
      expect(color2).toEqual(color3);
    });
  });

  describe("palette structure", () => {
    it("TAG_PALETTE should have exactly 10 entries", () => {
      expect(TAG_PALETTE.length).toBe(10);
    });

    it("OVERLAY_PALETTE should have exactly 10 entries", () => {
      expect(OVERLAY_PALETTE.length).toBe(10);
    });

    it("all TAG_PALETTE entries should have bg and text properties", () => {
      TAG_PALETTE.forEach((entry) => {
        expect(entry.bg).toBeDefined();
        expect(entry.text).toBeDefined();
        expect(typeof entry.bg).toBe("string");
        expect(typeof entry.text).toBe("string");
      });
    });

    it("all OVERLAY_PALETTE entries should have bg and text properties", () => {
      OVERLAY_PALETTE.forEach((entry) => {
        expect(entry.bg).toBeDefined();
        expect(entry.text).toBeDefined();
        expect(typeof entry.bg).toBe("string");
        expect(typeof entry.text).toBe("string");
      });
    });
  });
});
