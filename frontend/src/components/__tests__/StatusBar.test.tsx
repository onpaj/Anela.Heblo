import React from "react";
import { render, screen } from "@testing-library/react";
import { StatusBar } from "../StatusBar";
import { useConfigurationQuery } from "../../api/hooks/useConfiguration";
import {
  useLiveHealthCheck,
  useReadyHealthCheck,
} from "../../api/hooks/useHealth";
import { getRuntimeConfig } from "../../config/runtimeConfig";

jest.mock("../../api/hooks/useConfiguration");
jest.mock("../../api/hooks/useHealth");
jest.mock("../../config/runtimeConfig");

const mockUseConfigurationQuery = useConfigurationQuery as jest.Mock;
const mockUseLiveHealthCheck = useLiveHealthCheck as jest.Mock;
const mockUseReadyHealthCheck = useReadyHealthCheck as jest.Mock;
const mockGetRuntimeConfig = getRuntimeConfig as jest.Mock;

describe("StatusBar", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockUseLiveHealthCheck.mockReturnValue({
      data: { status: "Healthy" },
      isLoading: false,
      error: null,
    });
    mockUseReadyHealthCheck.mockReturnValue({
      data: { status: "Healthy" },
      isLoading: false,
      error: null,
    });
    mockGetRuntimeConfig.mockReturnValue({
      apiUrl: "https://api.example.com",
      useMockAuth: false,
      azureClientId: "client-id",
      azureAuthority: "authority",
      aiConnectionString: "",
    });
  });

  it("renders nothing while the configuration query is loading", () => {
    mockUseConfigurationQuery.mockReturnValue({
      data: undefined,
      isLoading: true,
    });

    const { container } = render(<StatusBar />);

    expect(container).toBeEmptyDOMElement();
  });

  it("renders backend-provided version, environment and mock-auth badge on success", () => {
    mockUseConfigurationQuery.mockReturnValue({
      data: {
        version: "1.4.2",
        environment: "Staging",
        useMockAuth: true,
      },
      isLoading: false,
    });

    render(<StatusBar />);

    expect(screen.getByText("v1.4.2")).toBeInTheDocument();
    expect(screen.getByText("Staging")).toBeInTheDocument();
    expect(screen.getByText("Mock Auth")).toBeInTheDocument();
    expect(screen.getByText("API: api.example.com")).toBeInTheDocument();
  });

  it("falls back to local runtime config when the query settles with no data", () => {
    mockUseConfigurationQuery.mockReturnValue({
      data: undefined,
      isLoading: false,
    });

    render(<StatusBar />);

    expect(screen.getByText("Production")).toBeInTheDocument();
    expect(screen.queryByText("Mock Auth")).not.toBeInTheDocument();
  });

  it("respects an explicit useMockAuth: false from the backend instead of the local fallback", () => {
    mockGetRuntimeConfig.mockReturnValue({
      apiUrl: "https://api.example.com",
      useMockAuth: true,
      azureClientId: "",
      azureAuthority: "",
      aiConnectionString: "",
    });
    mockUseConfigurationQuery.mockReturnValue({
      data: {
        version: "1.4.2",
        environment: "Production",
        useMockAuth: false,
      },
      isLoading: false,
    });

    render(<StatusBar />);

    expect(screen.queryByText("Mock Auth")).not.toBeInTheDocument();
  });
});
