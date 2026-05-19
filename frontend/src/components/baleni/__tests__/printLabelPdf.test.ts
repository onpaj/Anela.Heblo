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
});

describe('printLabelPdf', () => {
  it('creates a hidden iframe with the correct same-origin PDF URL', () => {
    const createElementSpy = jest.spyOn(document, 'createElement');
    const appendChildSpy = jest.spyOn(document.body, 'appendChild').mockImplementation(() => null as any);

    printLabelPdf('250001', {
      shipmentGuid: 'abc-guid-123',
      packageName: 'Zásilka 1',
    });

    expect(createElementSpy).toHaveBeenCalledWith('iframe');
    expect(appendChildSpy).toHaveBeenCalledTimes(1);

    const iframe = createElementSpy.mock.results[createElementSpy.mock.results.length - 1]
      .value as HTMLIFrameElement;

    expect(iframe.style.display).toBe('none');
    expect(iframe.src).toContain('http://localhost:5001/api/shipment-labels/pdf');
    expect(iframe.src).toContain('orderCode=250001');
    expect(iframe.src).toContain('shipmentGuid=abc-guid-123');
    expect(iframe.src).toContain('packageName=Z%C3%A1silka%201');

    createElementSpy.mockRestore();
    appendChildSpy.mockRestore();
  });

  it('calls contentWindow.print() when iframe loads and removes iframe', () => {
    const createElementSpy = jest.spyOn(document, 'createElement');
    const appendChildSpy = jest.spyOn(document.body, 'appendChild').mockImplementation(() => null as any);
    const removeChildSpy = jest.spyOn(document.body, 'removeChild').mockImplementation(() => null as any);

    printLabelPdf('250001', { shipmentGuid: 'abc-guid-123', packageName: 'Zásilka 1' });

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

    createElementSpy.mockRestore();
    appendChildSpy.mockRestore();
    removeChildSpy.mockRestore();
  });
});
