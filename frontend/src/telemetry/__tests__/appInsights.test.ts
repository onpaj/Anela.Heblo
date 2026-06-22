// Mock the SDK to avoid real network calls in tests
jest.mock('@microsoft/applicationinsights-web', () => ({
  ApplicationInsights: jest.fn().mockImplementation(() => ({
    loadAppInsights: jest.fn(),
    addTelemetryInitializer: jest.fn(),
    getTraceCtx: jest.fn(),
  })),
}));

jest.mock('@microsoft/applicationinsights-react-js', () => ({
  ReactPlugin: jest.fn().mockImplementation(() => ({
    identifier: 'ReactPlugin',
  })),
}));

// Deterministic id generator so trace/span assertions are stable.
jest.mock('@microsoft/applicationinsights-core-js', () => ({
  generateW3CId: jest.fn(() => 'abcdef0123456789abcdef0123456789'),
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

describe('appInsights user identity telemetry initializer', () => {
  const CONNECTION_STRING =
    'InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://westeurope-5.in.applicationinsights.azure.com/';

  type Envelope = { data?: Record<string, unknown> };
  type Initializer = (envelope: Envelope) => void;

  let initAppInsights: (cs: string) => unknown;
  let setUserIdentity: (identity: { name?: string; email?: string } | null) => void;
  let ApplicationInsightsMock: jest.Mock;

  // Pull the initializer registered via addTelemetryInitializer during init.
  function getRegisteredInitializer(): Initializer {
    const sdkInstance = ApplicationInsightsMock.mock.results[0].value as {
      addTelemetryInitializer: jest.Mock;
    };
    return sdkInstance.addTelemetryInitializer.mock.calls[0][0] as Initializer;
  }

  beforeEach(async () => {
    jest.resetModules();
    const sdkModule = await import('@microsoft/applicationinsights-web');
    ApplicationInsightsMock = sdkModule.ApplicationInsights as unknown as jest.Mock;
    const module = await import('../appInsights');
    initAppInsights = module.initAppInsights;
    setUserIdentity = module.setUserIdentity;
  });

  it('registers a telemetry initializer on init', () => {
    initAppInsights(CONNECTION_STRING);
    const sdkInstance = ApplicationInsightsMock.mock.results[0].value as {
      addTelemetryInitializer: jest.Mock;
    };
    expect(sdkInstance.addTelemetryInitializer).toHaveBeenCalledTimes(1);
  });

  it('stamps userName and userEmail onto the envelope when identity is set', () => {
    initAppInsights(CONNECTION_STRING);
    setUserIdentity({ name: 'Ada Lovelace', email: 'ada@example.com' });

    const envelope: Envelope = {};
    getRegisteredInitializer()(envelope);

    expect(envelope.data).toEqual({
      userName: 'Ada Lovelace',
      userEmail: 'ada@example.com',
    });
  });

  it('preserves existing envelope data while adding identity', () => {
    initAppInsights(CONNECTION_STRING);
    setUserIdentity({ name: 'Ada Lovelace', email: 'ada@example.com' });

    const envelope: Envelope = { data: { module: 'Catalog' } };
    getRegisteredInitializer()(envelope);

    expect(envelope.data).toEqual({
      module: 'Catalog',
      userName: 'Ada Lovelace',
      userEmail: 'ada@example.com',
    });
  });

  it('adds no identity keys when identity is null', () => {
    initAppInsights(CONNECTION_STRING);
    setUserIdentity(null);

    const envelope: Envelope = {};
    getRegisteredInitializer()(envelope);

    expect(envelope.data).toBeUndefined();
  });

  it('omits keys that are not present on the identity', () => {
    initAppInsights(CONNECTION_STRING);
    setUserIdentity({ name: 'Ada Lovelace' });

    const envelope: Envelope = {};
    getRegisteredInitializer()(envelope);

    expect(envelope.data).toEqual({ userName: 'Ada Lovelace' });
  });
});

describe('startNewTelemetryOperation', () => {
  const CONNECTION_STRING =
    'InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://westeurope-5.in.applicationinsights.azure.com/';

  type TraceCtx = { traceId: string; spanId: string; pageName?: string };

  let initAppInsights: (cs: string) => unknown;
  let startNewTelemetryOperation: (name?: string) => void;
  let ApplicationInsightsMock: jest.Mock;

  function mockTraceCtx(ctx: TraceCtx | null): void {
    const sdkInstance = ApplicationInsightsMock.mock.results[0].value as {
      getTraceCtx: jest.Mock;
    };
    sdkInstance.getTraceCtx.mockReturnValue(ctx);
  }

  beforeEach(async () => {
    jest.resetModules();
    const sdkModule = await import('@microsoft/applicationinsights-web');
    ApplicationInsightsMock = sdkModule.ApplicationInsights as unknown as jest.Mock;
    const module = await import('../appInsights');
    initAppInsights = module.initAppInsights;
    startNewTelemetryOperation = module.startNewTelemetryOperation;
  });

  it('assigns a fresh 32-hex traceId and 16-hex spanId on the current context', () => {
    initAppInsights(CONNECTION_STRING);
    const ctx: TraceCtx = { traceId: 'old', spanId: 'old' };
    mockTraceCtx(ctx);

    startNewTelemetryOperation();

    expect(ctx.traceId).toMatch(/^[0-9a-f]{32}$/);
    expect(ctx.spanId).toMatch(/^[0-9a-f]{16}$/);
    expect(ctx.traceId).not.toBe('old');
    expect(ctx.spanId).not.toBe('old');
  });

  it('sets pageName when a name is provided', () => {
    initAppInsights(CONNECTION_STRING);
    const ctx: TraceCtx = { traceId: 'old', spanId: 'old' };
    mockTraceCtx(ctx);

    startNewTelemetryOperation('packaging-scan');

    expect(ctx.pageName).toBe('packaging-scan');
  });

  it('leaves pageName untouched when no name is provided', () => {
    initAppInsights(CONNECTION_STRING);
    const ctx: TraceCtx = { traceId: 'old', spanId: 'old' };
    mockTraceCtx(ctx);

    startNewTelemetryOperation();

    expect(ctx.pageName).toBeUndefined();
  });

  it('no-ops when the SDK is not initialized', () => {
    expect(() => startNewTelemetryOperation('packaging-scan')).not.toThrow();
  });

  it('no-ops when there is no current trace context', () => {
    initAppInsights(CONNECTION_STRING);
    mockTraceCtx(null);

    expect(() => startNewTelemetryOperation('packaging-scan')).not.toThrow();
  });
});
