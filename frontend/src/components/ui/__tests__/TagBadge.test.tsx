import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { TagBadge } from "../TagBadge";
import { getTagColor, TAG_PALETTE, OVERLAY_PALETTE } from "../tagColor";

describe("TagBadge", () => {
  describe("rendering", () => {
    it("renders the tag name text", () => {
      render(<TagBadge name="TestTag" />);
      expect(screen.getByText("TestTag")).toBeInTheDocument();
    });

    it("applies the correct bg and text classes from getTagColor", () => {
      const tagName = "example";
      const { bg, text } = getTagColor(tagName, false);
      const { container } = render(<TagBadge name={tagName} />);

      const badgeDiv = container.querySelector("div");
      expect(badgeDiv).toHaveClass(bg);
      expect(badgeDiv).toHaveClass(text);
    });

    it("applies base shape classes: inline-flex items-center rounded-full text-xs px-2 py-0.5 gap-1", () => {
      const { container } = render(<TagBadge name="test" />);
      const badgeDiv = container.querySelector("div");

      expect(badgeDiv).toHaveClass("inline-flex");
      expect(badgeDiv).toHaveClass("items-center");
      expect(badgeDiv).toHaveClass("rounded-full");
      expect(badgeDiv).toHaveClass("text-xs");
      expect(badgeDiv).toHaveClass("px-2");
      expect(badgeDiv).toHaveClass("py-0.5");
      expect(badgeDiv).toHaveClass("gap-1");
    });
  });

  describe("remove button", () => {
    it("renders the remove button when onRemove is provided", () => {
      const handleRemove = jest.fn();
      render(<TagBadge name="test" onRemove={handleRemove} />);

      const button = screen.getByRole("button");
      expect(button).toBeInTheDocument();
      expect(button).toHaveTextContent("×");
    });

    it("does NOT render remove button when onRemove is omitted", () => {
      render(<TagBadge name="test" />);

      const buttons = screen.queryAllByRole("button");
      expect(buttons).toHaveLength(0);
    });

    it("calls onRemove when the remove button is clicked", () => {
      const handleRemove = jest.fn();
      render(<TagBadge name="test" onRemove={handleRemove} />);

      const button = screen.getByRole("button");
      fireEvent.click(button);

      expect(handleRemove).toHaveBeenCalledTimes(1);
    });

    it("remove button has correct aria-label", () => {
      const handleRemove = jest.fn();
      render(<TagBadge name="test" onRemove={handleRemove} />);

      const button = screen.getByRole("button", { name: "Odebrat štítek" });
      expect(button).toBeInTheDocument();
    });

    it("remove button has type='button'", () => {
      const handleRemove = jest.fn();
      render(<TagBadge name="test" onRemove={handleRemove} />);

      const button = screen.getByRole("button");
      expect(button).toHaveAttribute("type", "button");
    });

    it("remove button applies hover:opacity-70 and rounded-full classes", () => {
      const handleRemove = jest.fn();
      render(<TagBadge name="test" onRemove={handleRemove} />);

      const button = screen.getByRole("button");
      expect(button).toHaveClass("hover:opacity-70");
      expect(button).toHaveClass("rounded-full");
      expect(button).toHaveClass("ml-0.5");
    });
  });

  describe("variants", () => {
    it("for variant='default', calls getTagColor with overlay=false", () => {
      const tagName = "defaulttest";
      const { container } = render(<TagBadge name={tagName} variant="default" />);

      const { bg, text } = getTagColor(tagName, false);
      const badgeDiv = container.querySelector("div");
      expect(badgeDiv).toHaveClass(bg);
      expect(badgeDiv).toHaveClass(text);
    });

    it("for variant='compact', renders same shape as default (no visual difference)", () => {
      const { container: containerDefault } = render(
        <TagBadge name="test" variant="default" />
      );
      const { container: containerCompact } = render(
        <TagBadge name="test" variant="compact" />
      );

      const badgeDefault = containerDefault.querySelector("div");
      const badgeCompact = containerCompact.querySelector("div");

      // Both should have the same base classes
      expect(badgeDefault?.className).toBe(badgeCompact?.className);
    });

    it("for variant='overlay', calls getTagColor with overlay=true", () => {
      const tagName = "overlaytest";
      const { container } = render(<TagBadge name={tagName} variant="overlay" />);

      const { bg, text } = getTagColor(tagName, true);
      const badgeDiv = container.querySelector("div");
      expect(badgeDiv).toHaveClass(bg);
      expect(badgeDiv).toHaveClass(text);
    });

    it("overlay variant uses saturated colors (500/90) from OVERLAY_PALETTE", () => {
      const tagName = "example";
      const { bg } = getTagColor(tagName, true);

      const { container } = render(<TagBadge name={tagName} variant="overlay" />);
      const badgeDiv = container.querySelector("div");

      // Check that it has either a 500/90 or text-white from overlay palette
      expect(badgeDiv).toHaveClass(bg);
      expect(OVERLAY_PALETTE).toContainEqual({ bg, text: "text-white" });
    });

    it("default variant uses light colors from TAG_PALETTE", () => {
      const tagName = "example";
      const { bg } = getTagColor(tagName, false);

      const { container } = render(<TagBadge name={tagName} variant="default" />);
      const badgeDiv = container.querySelector("div");

      expect(badgeDiv).toHaveClass(bg);
      expect(TAG_PALETTE).toContainEqual(
        TAG_PALETTE.find((p) => p.bg === bg)!
      );
    });
  });

  describe("default variant", () => {
    it("renders with default variant when variant is not specified", () => {
      const tagName = "defaulttest";
      const { container: containerWithDefault } = render(
        <TagBadge name={tagName} variant="default" />
      );
      const { container: containerWithoutVariant } = render(
        <TagBadge name={tagName} />
      );

      const badgeWithDefault = containerWithDefault.querySelector("div");
      const badgeWithoutVariant = containerWithoutVariant.querySelector("div");

      expect(badgeWithDefault?.className).toBe(badgeWithoutVariant?.className);
    });
  });

  describe("integration scenarios", () => {
    it("renders remove button with onRemove in list context", () => {
      const handleRemove = jest.fn();
      render(<TagBadge name="ProductTag" onRemove={handleRemove} />);

      const button = screen.getByRole("button");
      expect(button).toBeInTheDocument();

      fireEvent.click(button);
      expect(handleRemove).toHaveBeenCalled();
    });

    it("renders without remove button in list view context", () => {
      render(<TagBadge name="ProductTag" />);

      const buttons = screen.queryAllByRole("button");
      expect(buttons).toHaveLength(0);
    });

    it("renders overlay variant for thumbnail context", () => {
      const { container } = render(
        <TagBadge name="ThumbnailTag" variant="overlay" />
      );

      const { bg, text } = getTagColor("ThumbnailTag", true);
      const badgeDiv = container.querySelector("div");

      expect(badgeDiv).toHaveClass(bg);
      expect(badgeDiv).toHaveClass(text);
      expect(badgeDiv).toHaveClass("text-white");
    });
  });
});
