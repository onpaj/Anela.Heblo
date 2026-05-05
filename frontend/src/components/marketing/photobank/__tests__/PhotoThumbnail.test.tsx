import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import PhotoThumbnail from "../PhotoThumbnail";

jest.mock("../../../../config/runtimeConfig", () => ({
  getConfig: () => ({ apiUrl: "http://localhost:5001" }),
}));

describe("PhotoThumbnail", () => {
  afterEach(() => {
    jest.clearAllMocks();
  });

  test("renders img with correct URL including photoId, size, and cache-bust param", () => {
    // Arrange
    const modifiedAt = "2026-01-15T10:00:00Z";
    const expectedVersion = new Date(modifiedAt).getTime();

    // Act
    render(
      <PhotoThumbnail
        photoId={42}
        modifiedAt={modifiedAt}
        alt="Test photo"
        size="medium"
      />,
    );

    // Assert
    const img = screen.getByRole("img", { name: "Test photo" });
    expect(img).toBeInTheDocument();
    expect(img).toHaveAttribute(
      "src",
      expect.stringContaining("/api/photobank/photos/42/thumbnail/medium"),
    );
    expect(img).toHaveAttribute(
      "src",
      expect.stringContaining(`?v=${expectedVersion}`),
    );
    expect(img).toHaveAttribute("loading", "lazy");
  });

  test("renders large size URL when size is large", () => {
    // Arrange & Act
    render(
      <PhotoThumbnail
        photoId={10}
        modifiedAt="2026-01-15T10:00:00Z"
        alt="Large photo"
        size="large"
      />,
    );

    // Assert
    const img = screen.getByRole("img", { name: "Large photo" });
    expect(img).toHaveAttribute(
      "src",
      expect.stringContaining("/thumbnail/large"),
    );
  });

  test("shows placeholder when img fires onError", () => {
    // Arrange
    render(
      <PhotoThumbnail
        photoId={7}
        modifiedAt="2026-01-15T10:00:00Z"
        alt="Error photo"
        size="medium"
      />,
    );

    const img = screen.getByRole("img", { name: "Error photo" });

    // Act
    fireEvent.error(img);

    // Assert
    expect(screen.queryByRole("img")).not.toBeInTheDocument();
    expect(
      screen.getByLabelText("Náhled není k dispozici"),
    ).toBeInTheDocument();
  });

  test("uses modifiedAt as cache-bust version parameter", () => {
    // Arrange
    const modifiedAt1 = "2026-01-15T10:00:00Z";
    const modifiedAt2 = "2026-03-20T12:30:00Z";
    const version1 = new Date(modifiedAt1).getTime();
    const version2 = new Date(modifiedAt2).getTime();

    // Act
    const { rerender } = render(
      <PhotoThumbnail photoId={1} modifiedAt={modifiedAt1} alt="photo" />,
    );
    const img1 = screen.getByRole("img", { name: "photo" });
    const src1 = img1.getAttribute("src") ?? "";

    rerender(
      <PhotoThumbnail photoId={1} modifiedAt={modifiedAt2} alt="photo" />,
    );
    const img2 = screen.getByRole("img", { name: "photo" });
    const src2 = img2.getAttribute("src") ?? "";

    // Assert
    expect(src1).toContain(`?v=${version1}`);
    expect(src2).toContain(`?v=${version2}`);
    expect(src1).not.toBe(src2);
  });
});
