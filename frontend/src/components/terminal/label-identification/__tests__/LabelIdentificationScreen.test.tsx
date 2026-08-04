import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import LabelIdentificationScreen from "../LabelIdentificationScreen";
import { useIdentifyLabelMutation } from "../../../../api/hooks/useLabelIdentification";
import { LabelMatchDecision, SwaggerException } from "../../../../api/generated/api-client";

jest.mock("../../../../api/hooks/useLabelIdentification");
jest.mock("../../../../telemetry/useScreenView", () => ({ useScreenView: jest.fn() }));

const mockMutate = jest.fn();
const setupMutation = (overrides = {}) => {
  (useIdentifyLabelMutation as jest.Mock).mockReturnValue({
    mutateAsync: mockMutate,
    isPending: false,
    ...overrides,
  });
};

const uploadPhoto = () => {
  const input = screen.getByTestId("label-photo-input");
  fireEvent.change(input, {
    target: { files: [new File(["x"], "label.jpg", { type: "image/jpeg" })] },
  });
};

beforeEach(() => {
  jest.clearAllMocks();
  setupMutation();
});

describe("LabelIdentificationScreen", () => {
  it("shows the capture button initially", () => {
    render(<LabelIdentificationScreen />);
    expect(screen.getByText("Vyfotit štítek")).toBeInTheDocument();
  });

  it("shows a single product code and name when one variant auto-confirms", async () => {
    mockMutate.mockResolvedValue({
      success: true,
      decision: LabelMatchDecision.Auto,
      rawText: "…",
      candidates: [{
        family: "PEE002", score: 97.2,
        variants: [{ productCode: "PEE002015", productName: "Ochráním chodidla" }],
      }],
    });
    render(<LabelIdentificationScreen />);
    uploadPhoto();

    expect(await screen.findByText("PEE002015")).toBeInTheDocument();
    expect(screen.getByText("Ochráním chodidla")).toBeInTheDocument();
    expect(screen.queryByTestId("label-size-step")).not.toBeInTheDocument();
  });

  it("asks for the size when the family has two variants", async () => {
    mockMutate.mockResolvedValue({
      success: true,
      decision: LabelMatchDecision.Auto,
      rawText: "…",
      candidates: [{
        family: "KRE005", score: 100,
        variants: [
          { productCode: "KRE005015", productName: "Masážní olej 15 ml" },
          { productCode: "KRE005030", productName: "Masážní olej 30 ml" },
        ],
      }],
    });
    render(<LabelIdentificationScreen />);
    uploadPhoto();

    expect(await screen.findByTestId("label-size-step")).toBeInTheDocument();
    fireEvent.click(screen.getByTestId("label-variant-KRE005030"));

    expect(await screen.findByTestId("label-final-code")).toHaveTextContent("KRE005030");
  });

  it("lists candidates to choose from on a Choose decision", async () => {
    mockMutate.mockResolvedValue({
      success: true,
      decision: LabelMatchDecision.Choose,
      rawText: "…",
      candidates: [
        { family: "KRE005", score: 74.1, variants: [{ productCode: "KRE005015", productName: "A" }] },
        { family: "MAS007", score: 71.0, variants: [{ productCode: "MAS007015", productName: "B" }] },
      ],
    });
    render(<LabelIdentificationScreen />);
    uploadPhoto();

    expect(await screen.findByTestId("label-candidate-KRE005")).toBeInTheDocument();
    expect(screen.getByTestId("label-candidate-MAS007")).toBeInTheDocument();
  });

  it("skips the size step when a chosen Choose candidate has exactly one variant", async () => {
    mockMutate.mockResolvedValue({
      success: true,
      decision: LabelMatchDecision.Choose,
      rawText: "…",
      candidates: [
        { family: "KRE005", score: 74.1, variants: [{ productCode: "KRE005015", productName: "A" }] },
        { family: "MAS007", score: 71.0, variants: [{ productCode: "MAS007015", productName: "B" }] },
      ],
    });
    render(<LabelIdentificationScreen />);
    uploadPhoto();

    fireEvent.click(await screen.findByTestId("label-candidate-KRE005"));

    expect(await screen.findByTestId("label-final-code")).toHaveTextContent("KRE005015");
    expect(screen.queryByTestId("label-size-step")).not.toBeInTheDocument();
  });

  it("shows the unreadable message with a retry on a Low decision", async () => {
    mockMutate.mockResolvedValue({
      success: true, decision: LabelMatchDecision.Low, rawText: "…", candidates: [],
    });
    render(<LabelIdentificationScreen />);
    uploadPhoto();

    expect(await screen.findByText("Nepodařilo se přečíst štítek")).toBeInTheDocument();
    expect(screen.getByText("Zkusit znovu")).toBeInTheDocument();
  });

  it("shows the unreadable message when a non-Low decision has no candidates", async () => {
    mockMutate.mockResolvedValue({
      success: true, decision: LabelMatchDecision.Choose, rawText: "…", candidates: [],
    });
    render(<LabelIdentificationScreen />);
    uploadPhoto();

    expect(await screen.findByText("Nepodařilo se přečíst štítek")).toBeInTheDocument();
    expect(screen.getByText("Zkusit znovu")).toBeInTheDocument();
  });

  it("shows the unreadable message when a selected family has no variants", async () => {
    mockMutate.mockResolvedValue({
      success: true,
      decision: LabelMatchDecision.Choose,
      rawText: "…",
      candidates: [{ family: "KRE005", score: 74.1, variants: [] }],
    });
    render(<LabelIdentificationScreen />);
    uploadPhoto();

    fireEvent.click(await screen.findByTestId("label-candidate-KRE005"));

    expect(await screen.findByText("Nepodařilo se přečíst štítek")).toBeInTheDocument();
    expect(screen.getByText("Zkusit znovu")).toBeInTheDocument();
  });

  it("shows a Czech error message when the request fails with a genuine non-HTTP error", async () => {
    mockMutate.mockRejectedValue(new Error("boom"));
    render(<LabelIdentificationScreen />);
    uploadPhoto();

    expect(
      await screen.findByText("Služba rozpoznávání není dostupná, zkuste to znovu."),
    ).toBeInTheDocument();
  });

  it("shows the unreadable-ingredients message for a 422 LabelTextUnreadable SwaggerException", async () => {
    const body = JSON.stringify({
      success: false,
      errorCode: "LabelTextUnreadable",
      params: null,
    });
    mockMutate.mockRejectedValue(
      new SwaggerException("Unprocessable", 422, body, {}, null),
    );
    render(<LabelIdentificationScreen />);
    uploadPhoto();

    expect(
      await screen.findByText(
        "Na fotce nejsou čitelné ingredience — jděte blíž a držte telefon v klidu.",
      ),
    ).toBeInTheDocument();
  });

  it("shows the undecodable-photo message for a 400 LabelPhotoUndecodable SwaggerException", async () => {
    const body = JSON.stringify({
      success: false,
      errorCode: "LabelPhotoUndecodable",
      params: null,
    });
    mockMutate.mockRejectedValue(
      new SwaggerException("Bad Request", 400, body, {}, null),
    );
    render(<LabelIdentificationScreen />);
    uploadPhoto();

    expect(await screen.findByText("Nepodařilo se načíst fotku.")).toBeInTheDocument();
  });

  it("shows the missing-photo message for a 413 payload-too-large SwaggerException with no body", async () => {
    mockMutate.mockRejectedValue(new SwaggerException("Payload Too Large", 413, "", {}, null));
    render(<LabelIdentificationScreen />);
    uploadPhoto();

    expect(await screen.findByText("Nahrajte prosím fotku štítku.")).toBeInTheDocument();
  });

  it("shows a reading indicator while the request is in flight", () => {
    setupMutation({ isPending: true });
    render(<LabelIdentificationScreen />);
    expect(screen.getByText("Čtu štítek…")).toBeInTheDocument();
  });

  it("shows the reference label and opens the zoom viewer on the final screen", async () => {
    mockMutate.mockResolvedValue({
      success: true,
      decision: LabelMatchDecision.Auto,
      rawText: "…",
      candidates: [{
        family: "PEE002", score: 97.2,
        variants: [{ productCode: "PEE002015", productName: "Ochráním chodidla" }],
      }],
    });
    render(<LabelIdentificationScreen />);
    uploadPhoto();

    const labelButton = await screen.findByTestId("label-reference-PEE002");
    expect(screen.getByAltText("Referenční štítek PEE002")).toBeInTheDocument();
    expect(screen.queryByTestId("label-reference-viewer")).not.toBeInTheDocument();

    fireEvent.click(labelButton);
    expect(screen.getByTestId("label-reference-viewer")).toBeInTheDocument();
  });

  it("shows a reference label per candidate and zooms without selecting", async () => {
    mockMutate.mockResolvedValue({
      success: true,
      decision: LabelMatchDecision.Choose,
      rawText: "…",
      candidates: [
        { family: "KRE005", score: 74.1, variants: [{ productCode: "KRE005015", productName: "A" }] },
        { family: "MAS007", score: 71.0, variants: [{ productCode: "MAS007015", productName: "B" }] },
      ],
    });
    render(<LabelIdentificationScreen />);
    uploadPhoto();

    expect(await screen.findByAltText("Referenční štítek KRE005")).toBeInTheDocument();
    expect(screen.getByAltText("Referenční štítek MAS007")).toBeInTheDocument();

    fireEvent.click(screen.getByTestId("label-candidate-zoom-KRE005"));

    // The zoom control is a sibling of the select button, so zooming must not confirm.
    expect(screen.getByTestId("label-reference-viewer")).toBeInTheDocument();
    expect(screen.queryByTestId("label-final-code")).not.toBeInTheDocument();
  });

  it("shows the family reference label on the size step", async () => {
    mockMutate.mockResolvedValue({
      success: true,
      decision: LabelMatchDecision.Auto,
      rawText: "…",
      candidates: [{
        family: "KRE005", score: 100,
        variants: [
          { productCode: "KRE005015", productName: "Masážní olej 15 ml" },
          { productCode: "KRE005030", productName: "Masážní olej 30 ml" },
        ],
      }],
    });
    render(<LabelIdentificationScreen />);
    uploadPhoto();

    expect(await screen.findByTestId("label-size-step")).toBeInTheDocument();
    expect(screen.getByTestId("label-reference-KRE005")).toBeInTheDocument();
    expect(screen.getByAltText("Referenční štítek KRE005")).toBeInTheDocument();
  });

  it("hides the reference label when its artwork fails to load", async () => {
    mockMutate.mockResolvedValue({
      success: true,
      decision: LabelMatchDecision.Auto,
      rawText: "…",
      candidates: [{
        family: "PEE002", score: 97.2,
        variants: [{ productCode: "PEE002015", productName: "Ochráním chodidla" }],
      }],
    });
    render(<LabelIdentificationScreen />);
    uploadPhoto();

    const image = await screen.findByAltText("Referenční štítek PEE002");
    fireEvent.error(image);

    expect(screen.queryByTestId("label-reference-PEE002")).not.toBeInTheDocument();
    // The product code itself still stands — a missing image never blocks the answer.
    expect(screen.getByTestId("label-final-code")).toHaveTextContent("PEE002015");
  });
});
