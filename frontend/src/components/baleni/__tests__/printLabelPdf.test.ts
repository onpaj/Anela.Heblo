import { printLabelPdf } from '../printLabelPdf';
import type { ShipmentLabelDto } from '../../../api/generated/api-client';

const flushAsync = () => new Promise(resolve => setTimeout(resolve, 0));

const labelWithUrl: ShipmentLabelDto = {
  shipmentGuid: 'abc-guid-123',
  packageName: 'Zásilka 1',
  labelUrl: 'https://carrier.example.com/label.pdf',
} as ShipmentLabelDto;

const labelWithoutUrl: ShipmentLabelDto = {
  shipmentGuid: 'abc-guid-123',
  packageName: 'Zásilka 1',
} as ShipmentLabelDto;

let originalOpen: typeof window.open;

beforeEach(() => {
  jest.clearAllMocks();
  URL.createObjectURL = jest.fn().mockReturnValue('blob:test-url');
  URL.revokeObjectURL = jest.fn();
  originalOpen = window.open;
  window.open = jest.fn() as unknown as typeof window.open;
});

afterEach(() => {
  window.open = originalOpen;
});

describe('printLabelPdf', () => {
  it('fetches the carrier CDN URL directly (no /api/... proxy)', async () => {
    global.fetch = jest.fn().mockResolvedValue({
      ok: true,
      blob: async () => new Blob(['%PDF'], { type: 'application/pdf' }),
    });
    jest.spyOn(document.body, 'appendChild').mockImplementation(() => null as any);

    printLabelPdf('250001', labelWithUrl);
    await flushAsync();

    expect(global.fetch).toHaveBeenCalledWith('https://carrier.example.com/label.pdf');
    expect(window.open).not.toHaveBeenCalled();

    jest.restoreAllMocks();
  });

  it('mounts a hidden iframe with the blob URL, calls print, removes iframe, revokes blob URL', async () => {
    global.fetch = jest.fn().mockResolvedValue({
      ok: true,
      blob: async () => new Blob(['%PDF'], { type: 'application/pdf' }),
    });
    const createElementSpy = jest.spyOn(document, 'createElement');
    const appendChildSpy = jest.spyOn(document.body, 'appendChild').mockImplementation(() => null as any);
    const removeChildSpy = jest.spyOn(document.body, 'removeChild').mockImplementation(() => null as any);

    printLabelPdf('250001', labelWithUrl);
    await flushAsync();

    const iframe = createElementSpy.mock.results[createElementSpy.mock.results.length - 1]
      .value as HTMLIFrameElement;
    expect(iframe.style.display).toBe('none');
    expect(iframe.src).toBe('blob:test-url');
    expect(appendChildSpy).toHaveBeenCalledWith(iframe);

    const printMock = jest.fn();
    Object.defineProperty(iframe, 'contentWindow', {
      value: { print: printMock },
      configurable: true,
    });
    iframe.onload!(new Event('load'));

    expect(printMock).toHaveBeenCalledTimes(1);
    expect(removeChildSpy).toHaveBeenCalledWith(iframe);
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:test-url');

    jest.restoreAllMocks();
  });

  it('falls back to window.open when fetch throws (CORS)', async () => {
    global.fetch = jest.fn().mockRejectedValue(new TypeError('Failed to fetch'));
    const appendChildSpy = jest.spyOn(document.body, 'appendChild');

    printLabelPdf('250001', labelWithUrl);
    await flushAsync();

    expect(appendChildSpy).not.toHaveBeenCalled();
    expect(window.open).toHaveBeenCalledWith(
      'https://carrier.example.com/label.pdf',
      '_blank',
      'noopener,noreferrer',
    );

    appendChildSpy.mockRestore();
  });

  it('falls back to window.open when fetch returns non-ok', async () => {
    global.fetch = jest.fn().mockResolvedValue({ ok: false, status: 404 });
    const appendChildSpy = jest.spyOn(document.body, 'appendChild');

    printLabelPdf('250001', labelWithUrl);
    await flushAsync();

    expect(appendChildSpy).not.toHaveBeenCalled();
    expect(window.open).toHaveBeenCalledWith(
      'https://carrier.example.com/label.pdf',
      '_blank',
      'noopener,noreferrer',
    );

    appendChildSpy.mockRestore();
  });

  it('is a no-op when labelUrl is missing', async () => {
    global.fetch = jest.fn();
    const appendChildSpy = jest.spyOn(document.body, 'appendChild');

    printLabelPdf('250001', labelWithoutUrl);
    await flushAsync();

    expect(global.fetch).not.toHaveBeenCalled();
    expect(appendChildSpy).not.toHaveBeenCalled();
    expect(window.open).not.toHaveBeenCalled();

    appendChildSpy.mockRestore();
  });
});
