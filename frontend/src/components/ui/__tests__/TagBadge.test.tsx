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
      render(<TagBadge name={tagName} />);

      const badge = screen.getByTestId("tag-badge");
      expect(badge).toHaveClass(bg);
      expect(badge).toHaveClass(text);
    });

    it("applies base shape classes: inline-flex items-center rounded-full text-xs px-2 py-0.5 gap-1", () => {
      render(<TagBadge name="test" />);
      const badge = screen.getByTestId("tag-badge");

      expect(badge).toHaveClass("inline-flex");
      expect(badge).toHaveClass("items-center");
      expect(badge).toHaveClass("rounded-full");
      expect(badge).toHaveClass("text-xs");
      expect(badge).toHaveClass("px-2");
      expect(badge).toHaveClass("py-0.5");
      expect(badge).toHaveClass("gap-1");
    });
  });

  describe("remove button", () => {
    it("renders the remove button when onRemove is provided", () => {
      const handleRemove = jest.fn();
      render(<TagBadge name="test" onRemove={handleRemove} />);

      const button = screen.getByLabelText("Odebrat štítek test");
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

      const button = screen.getByRole("button", { name: "Odebrat štítek test" });
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
      render(<TagBadge name={tagName} variant="default" />);

      const { bg, text } = getTagColor(tagName, false);
      const badge = screen.getByTestId("tag-badge");
      expect(badge).toHaveClass(bg);
      expect(badge).toHaveClass(text);
    });

    it("for variant='overlay', calls getTagColor with overlay=true", () => {
      const tagName = "overlaytest";
      render(<TagBadge name={tagName} variant="overlay" />);

      const { bg, text } = getTagColor(tagName, true);
      const badge = screen.getByTestId("tag-badge");
      expect(badge).toHaveClass(bg);
      expect(badge).toHaveClass(text);
    });

    it("overlay variant uses saturated colors (500/90) from OVERLAY_PALETTE", () => {
      const tagName = "example";
      const { bg } = getTagColor(tagName, true);

      render(<TagBadge name={tagName} variant="overlay" />);
      const badge = screen.getByTestId("tag-badge");

      // Check that it has either a 500/90 or text-white from overlay palette
      expect(badge).toHaveClass(bg);
      expect(OVERLAY_PALETTE).toContainEqual({ bg, text: "text-white" });
    });

    it("default variant uses light colors from TAG_PALETTE", () => {
      const tagName = "example";
      const { bg } = getTagColor(tagName, false);

      render(<TagBadge name={tagName} variant="default" />);
      const badge = screen.getByTestId("tag-badge");

      expect(badge).toHaveClass(bg);
      expect(TAG_PALETTE).toContainEqual(
        TAG_PALETTE.find((p) => p.bg === bg)!
      );
    });
  });

  describe("default variant", () => {
    it("renders with default variant when variant is not specified", () => {
      const tagName = "defaulttest";
      render(<TagBadge name={tagName} variant="default" />);
      const badgeWithDefault = screen.getByTestId("tag-badge");
      const classNameWithDefault = badgeWithDefault.className;

      // Re-render without variant to compare
      const { unmount } = render(<TagBadge name={tagName} />);
      const badgeWithoutVariant = screen.getAllByTestId("tag-badge")[1];

      expect(classNameWithDefault).toBe(badgeWithoutVariant.className);
      unmount();
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
      render(<TagBadge name="ThumbnailTag" variant="overlay" />);

      const { bg, text } = getTagColor("ThumbnailTag", true);
      const badge = screen.getByTestId("tag-badge");

      expect(badge).toHaveClass(bg);
      expect(badge).toHaveClass(text);
      expect(badge).toHaveClass("text-white");
    });
  });
});
