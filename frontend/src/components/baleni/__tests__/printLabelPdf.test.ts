import { printLabelPdf } from '../printLabelPdf';
import * as clientModule from '../../../api/client';

jest.mock('../../../api/client', () => ({
  getAuthenticatedApiClient: jest.fn(),
}));

const mockGetAuthenticatedApiClient =
  clientModule.getAuthenticatedApiClient as jest.MockedFunction<
    typeof clientModule.getAuthenticatedApiClient
  >;

beforeEach(() => {
  jest.clearAllMocks();
  mockGetAuthenticatedApiClient.mockReturnValue({
    baseUrl: 'http://localhost:5001',
  } as any);
  URL.createObjectURL = jest.fn().mockReturnValue('blob:test-url');
  URL.revokeObjectURL = jest.fn();
});

const successFetch = () => {
  global.fetch = jest.fn().mockResolvedValue({
    ok: true,
    blob: async () => new Blob(['%PDF'], { type: 'application/pdf' }),
  });
};

describe('printLabelPdf', () => {
  it('fetches the PDF from the correct same-origin URL', async () => {
    successFetch();
    jest.spyOn(document.body, 'appendChild').mockImplementation(() => null as any);

    printLabelPdf('250001', { shipmentGuid: 'abc-guid-123', packageName: 'Zásilka 1' });
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(global.fetch).toHaveBeenCalledWith(
      expect.stringMatching(/http:\/\/localhost:5001\/api\/shipment-labels\/pdf/)
    );
    const url = (global.fetch as jest.Mock).mock.calls[0][0] as string;
    expect(url).toContain('orderCode=250001');
    expect(url).toContain('shipmentGuid=abc-guid-123');
    expect(url).toContain('packageName=Z%C3%A1silka%201');

    jest.restoreAllMocks();
  });

  it('creates a hidden iframe with a blob URL and appends it', async () => {
    successFetch();
    const createElementSpy = jest.spyOn(document, 'createElement');
    const appendChildSpy = jest.spyOn(document.body, 'appendChild').mockImplementation(() => null as any);

    printLabelPdf('250001', { shipmentGuid: 'abc-guid-123', packageName: 'Zásilka 1' });
    await new Promise(resolve => setTimeout(resolve, 0));

    const iframe = createElementSpy.mock.results[createElementSpy.mock.results.length - 1]
      .value as HTMLIFrameElement;
    expect(iframe.style.display).toBe('none');
    expect(iframe.src).toBe('blob:test-url');
    expect(appendChildSpy).toHaveBeenCalledWith(iframe);

    createElementSpy.mockRestore();
    appendChildSpy.mockRestore();
  });

  it('calls contentWindow.print(), removes iframe, and revokes blob URL on load', async () => {
    successFetch();
    const createElementSpy = jest.spyOn(document, 'createElement');
    const appendChildSpy = jest.spyOn(document.body, 'appendChild').mockImplementation(() => null as any);
    const removeChildSpy = jest.spyOn(document.body, 'removeChild').mockImplementation(() => null as any);

    printLabelPdf('250001', { shipmentGuid: 'abc-guid-123', packageName: 'Zásilka 1' });
    await new Promise(resolve => setTimeout(resolve, 0));

    const iframe = createElementSpy.mock.results[createElementSpy.mock.results.length - 1]
      .value as HTMLIFrameElement;
    const printMock = jest.fn();
    Object.defineProperty(iframe, 'contentWindow', {
      value: { print: printMock },
      configurable: true,
    });

    iframe.onload!(new Event('load'));

    expect(printMock).toHaveBeenCalledTimes(1);
    expect(removeChildSpy).toHaveBeenCalledWith(iframe);
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:test-url');

    createElementSpy.mockRestore();
    appendChildSpy.mockRestore();
    removeChildSpy.mockRestore();
  });

  it('does not create an iframe when the PDF fetch returns a non-ok status', async () => {
    global.fetch = jest.fn().mockResolvedValue({ ok: false, status: 404 });
    const appendChildSpy = jest.spyOn(document.body, 'appendChild');

    printLabelPdf('250001', { shipmentGuid: 'abc-guid-123', packageName: 'Zásilka 1' });
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(appendChildSpy).not.toHaveBeenCalled();

    appendChildSpy.mockRestore();
  });

  it('throws synchronously when shipmentGuid is missing', () => {
    expect(() =>
      printLabelPdf('250001', { packageName: 'Zásilka 1' })
    ).toThrow('Invalid label: missing shipmentGuid or packageName');
  });
});
