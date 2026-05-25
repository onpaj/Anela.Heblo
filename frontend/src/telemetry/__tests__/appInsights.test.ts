// Mock the SDK to avoid real network calls in tests
jest.mock('@microsoft/applicationinsights-web', () => ({
  ApplicationInsights: jest.fn().mockImplementation(() => ({
    loadAppInsights: jest.fn(),
  })),
}));

jest.mock('@microsoft/applicationinsights-react-js', () => ({
  ReactPlugin: jest.fn().mockImplementation(() => ({
    identifier: 'ReactPlugin',
  })),
}));

describe('appInsights singleton', () => {
  let initAppInsights: (cs: string) => unknown;
  let getAppInsights: () => unknown;
  let ApplicationInsightsMock: jest.Mock;

  beforeEach(async () => {
    jest.resetModules();
    // Re-import after reset to get a fresh singleton and fresh mocks
    const sdkModule = await import('@microsoft/applicationinsights-web');
    ApplicationInsightsMock = sdkModule.ApplicationInsights as unknown as jest.Mock;
    const module = await import('../appInsights');
    initAppInsights = module.initAppInsights;
    getAppInsights = module.getAppInsights;
  });

  it('returns null when connection string is empty', () => {
    const result = initAppInsights('');
    expect(result).toBeNull();
    expect(getAppInsights()).toBeNull();
  });

  it('returns null when connection string is whitespace only', () => {
    const result = initAppInsights('   ');
    expect(result).toBeNull();
    expect(getAppInsights()).toBeNull();
  });

  it('creates and returns an ApplicationInsights instance when connection string is set', () => {
    const result = initAppInsights(
      'InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://westeurope-5.in.applicationinsights.azure.com/'
    );
    expect(result).not.toBeNull();
    expect(getAppInsights()).not.toBeNull();
    expect(ApplicationInsightsMock).toHaveBeenCalledTimes(1);
  });

  it('returns the existing instance on second call (singleton)', () => {
    const cs = 'InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://westeurope-5.in.applicationinsights.azure.com/';
    initAppInsights(cs);
    initAppInsights(cs);
    expect(ApplicationInsightsMock).toHaveBeenCalledTimes(1);
  });
});
