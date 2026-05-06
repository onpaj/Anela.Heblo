import React from "react";
import { render, screen, waitFor } from "@testing-library/react";
import PhotoThumbnail from "../PhotoThumbnail";
import { authenticatedFetch } from "../../../../api/client";

jest.mock("../../../../config/runtimeConfig", () => ({
  getConfig: () => ({ apiUrl: "http://localhost:5001" }),
}));

jest.mock("../../../../api/client", () => ({
  authenticatedFetch: jest.fn(),
}));

const mockAuthenticatedFetch = authenticatedFetch as jest.Mock;

const FAKE_BLOB_URL = "blob:fake-url";

function makeFetchSuccess() {
  const blob = new Blob(["img-data"], { type: "image/jpeg" });
  const response = {
    ok: true,
    blob: () => Promise.resolve(blob),
  };
  mockAuthenticatedFetch.mockResolvedValue(response);
}

function makeFetchFailure() {
  mockAuthenticatedFetch.mockRejectedValue(new Error("Network error"));
}

describe("PhotoThumbnail", () => {
  beforeEach(() => {
    URL.createObjectURL = jest.fn().mockReturnValue(FAKE_BLOB_URL);
    URL.revokeObjectURL = jest.fn();
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  test("shows loading skeleton initially before fetch resolves", () => {
    // Arrange — fetch never resolves during this test
    mockAuthenticatedFetch.mockReturnValue(new Promise(() => undefined));
    const alt = "Test photo";

    // Act
    render(
      <PhotoThumbnail
        photoId={42}
        modifiedAt="2026-01-15T10:00:00Z"
        alt={alt}
        size="medium"
      />,
    );

    // Assert — skeleton div is present, no img yet
    expect(screen.getByLabelText(alt)).toBeInTheDocument();
    expect(screen.queryByRole("img")).not.toBeInTheDocument();
  });

  test("shows img with blob URL after authenticatedFetch resolves", async () => {
    // Arrange
    makeFetchSuccess();

    // Act
    render(
      <PhotoThumbnail
        photoId={42}
        modifiedAt="2026-01-15T10:00:00Z"
        alt="Test photo"
        size="medium"
      />,
    );

    // Assert
    const img = await screen.findByRole("img", { name: "Test photo" });
    expect(img).toBeInTheDocument();
    expect(img).toHaveAttribute("src", expect.stringMatching(/^blob:/));
  });

  test("shows error placeholder when authenticatedFetch rejects", async () => {
    // Arrange
    makeFetchFailure();

    // Act
    render(
      <PhotoThumbnail
        photoId={7}
        modifiedAt="2026-01-15T10:00:00Z"
        alt="Error photo"
        size="medium"
      />,
    );

    // Assert
    await waitFor(() => {
      expect(
        screen.getByLabelText("Náhled není k dispozici"),
      ).toBeInTheDocument();
    });
    expect(screen.queryByRole("img")).not.toBeInTheDocument();
  });

  test("calls authenticatedFetch with URL containing correct photoId, size, and v= param", () => {
    // Arrange
    const modifiedAt = "2026-01-15T10:00:00Z";
    const expectedVersion = new Date(modifiedAt).getTime();
    mockAuthenticatedFetch.mockReturnValue(new Promise(() => undefined));

    // Act
    render(
      <PhotoThumbnail
        photoId={42}
        modifiedAt={modifiedAt}
        alt="URL test photo"
        size="large"
      />,
    );

    // Assert
    expect(mockAuthenticatedFetch).toHaveBeenCalledTimes(1);
    const calledUrl = mockAuthenticatedFetch.mock.calls[0][0] as string;
    expect(calledUrl).toContain("http://localhost:5001");
    expect(calledUrl).toContain("/api/photobank/photos/42/thumbnail/large");
    expect(calledUrl).toContain(`?v=${expectedVersion}`);
  });

  test("revokes object URL when component unmounts", async () => {
    // Arrange
    makeFetchSuccess();

    // Act
    const { unmount } = render(
      <PhotoThumbnail
        photoId={42}
        modifiedAt="2026-01-15T10:00:00Z"
        alt="Cleanup photo"
        size="medium"
      />,
    );
    await screen.findByRole("img", { name: "Cleanup photo" });

    // Assert — revokeObjectURL called on unmount
    unmount();
    expect(URL.revokeObjectURL).toHaveBeenCalledWith(FAKE_BLOB_URL);
  });
});
