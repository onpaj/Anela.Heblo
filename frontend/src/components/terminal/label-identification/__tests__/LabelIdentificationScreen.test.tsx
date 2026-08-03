import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import LabelIdentificationScreen from "../LabelIdentificationScreen";
import { useIdentifyLabelMutation } from "../../../../api/hooks/useLabelIdentification";
import { LabelMatchDecision } from "../../../../api/generated/api-client";

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

    await waitFor(() => expect(screen.getByText("PEE002015")).toBeInTheDocument());
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

    await waitFor(() => expect(screen.getByTestId("label-size-step")).toBeInTheDocument());
    fireEvent.click(screen.getByTestId("label-variant-KRE005030"));

    await waitFor(() => expect(screen.getByTestId("label-final-code")).toHaveTextContent("KRE005030"));
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

    await waitFor(() => expect(screen.getByTestId("label-candidate-KRE005")).toBeInTheDocument());
    expect(screen.getByTestId("label-candidate-MAS007")).toBeInTheDocument();
  });

  it("shows the unreadable message with a retry on a Low decision", async () => {
    mockMutate.mockResolvedValue({
      success: true, decision: LabelMatchDecision.Low, rawText: "…", candidates: [],
    });
    render(<LabelIdentificationScreen />);
    uploadPhoto();

    await waitFor(() =>
      expect(screen.getByText("Nepodařilo se přečíst štítek")).toBeInTheDocument());
    expect(screen.getByText("Zkusit znovu")).toBeInTheDocument();
  });

  it("shows a Czech error message when the request fails", async () => {
    mockMutate.mockRejectedValue(new Error("boom"));
    render(<LabelIdentificationScreen />);
    uploadPhoto();

    await waitFor(() =>
      expect(
        screen.getByText("Služba rozpoznávání není dostupná, zkuste to znovu."),
      ).toBeInTheDocument());
  });

  it("shows a reading indicator while the request is in flight", () => {
    setupMutation({ isPending: true });
    render(<LabelIdentificationScreen />);
    expect(screen.getByText("Čtu štítek…")).toBeInTheDocument();
  });
});
