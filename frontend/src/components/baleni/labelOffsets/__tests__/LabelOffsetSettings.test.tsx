import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import LabelOffsetSettings from '../LabelOffsetSettings';
import { useLabelSample } from '../useLabelSample';
import { fetchAndPrintLabel } from '../../printLabelPdf';
import { loadLabelOffsets, saveLabelOffset } from '../labelOffsetStorage';

jest.mock('../useLabelSample', () => ({ useLabelSample: jest.fn() }));
jest.mock('../../printLabelPdf', () => ({ fetchAndPrintLabel: jest.fn() }));
jest.mock('../../../../telemetry/useScreenView', () => ({ useScreenView: jest.fn() }));

const mockUseLabelSample = useLabelSample as jest.MockedFunction<typeof useLabelSample>;
const mockFetchAndPrint = fetchAndPrintLabel as jest.MockedFunction<typeof fetchAndPrintLabel>;

const readySample = {
  sample: { orderCode: '250001', packageNumber: 1 },
  previewUrl: 'blob:preview',
  status: 'ready' as const,
};

beforeEach(() => {
  window.localStorage.clear();
  mockUseLabelSample.mockReturnValue(readySample);
  mockFetchAndPrint.mockResolvedValue({ printed: true, status: 200 });
});

const click = (name: string | RegExp) => fireEvent.click(screen.getByRole('button', { name }));

describe('LabelOffsetSettings', () => {
  it('starts on Zásilkovna with its saved offset', () => {
    saveLabelOffset('Zasilkovna', { xMm: 0, yMm: -5 });
    render(<LabelOffsetSettings />);

    expect(screen.getByRole('button', { name: 'Zásilkovna' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByTestId('offset-x')).toHaveTextContent('0 mm');
    expect(screen.getByTestId('offset-y')).toHaveTextContent('-5 mm');
  });

  it('moves the draft 1 mm per arrow click and previews it', () => {
    render(<LabelOffsetSettings />);

    click('Posunout doprava');
    click('Posunout doprava');
    click('Posunout dolů');

    expect(screen.getByTestId('offset-x')).toHaveTextContent('+2 mm');
    expect(screen.getByTestId('offset-y')).toHaveTextContent('-1 mm');
    expect(mockUseLabelSample).toHaveBeenLastCalledWith('Zasilkovna', { xMm: 2, yMm: -1 });
    expect(screen.getByText('(neuložené změny)')).toBeInTheDocument();
  });

  it('stops at the 30 mm limit', () => {
    render(<LabelOffsetSettings />);
    for (let i = 0; i < 35; i++) click('Posunout nahoru');
    expect(screen.getByTestId('offset-y')).toHaveTextContent('+30 mm');
  });

  it('test-prints the sample with the unsaved draft offset', async () => {
    render(<LabelOffsetSettings />);
    click('Posunout doleva');

    click(/Zkušební tisk/);

    await waitFor(() =>
      expect(mockFetchAndPrint).toHaveBeenCalledWith('250001', 1, undefined, false, {
        Zasilkovna: { xMm: -1, yMm: 0 },
      }),
    );
    expect(await screen.findByText('Zkušební štítek odeslán na tisk.')).toBeInTheDocument();
    expect(loadLabelOffsets()).toEqual({});
  });

  it('reports a failed test print', async () => {
    mockFetchAndPrint.mockResolvedValue({ printed: false, status: 503 });
    render(<LabelOffsetSettings />);

    click(/Zkušební tisk/);

    expect(await screen.findByRole('alert')).toHaveTextContent('Zkušební tisk se nezdařil.');
  });

  it('saves the draft for the selected carrier only', () => {
    saveLabelOffset('Zasilkovna', { xMm: 0, yMm: -5 });
    render(<LabelOffsetSettings />);

    click('PPL');
    click('Posunout nahoru');
    click(/Uložit/);

    expect(loadLabelOffsets()).toEqual({
      Zasilkovna: { xMm: 0, yMm: -5 },
      PPL: { xMm: 0, yMm: 1 },
    });
    expect(screen.queryByText('(neuložené změny)')).not.toBeInTheDocument();
  });

  it('disables save until something changes and test print until a sample is loaded', () => {
    mockUseLabelSample.mockReturnValue({ sample: null, previewUrl: null, status: 'none' });
    render(<LabelOffsetSettings />);

    expect(screen.getByRole('button', { name: /Uložit/ })).toBeDisabled();
    expect(screen.getByRole('button', { name: /Zkušební tisk/ })).toBeDisabled();
    expect(screen.getByText(/není k dispozici žádný nedávný štítek/)).toBeInTheDocument();
  });

  it('switching carrier discards the unsaved draft', () => {
    render(<LabelOffsetSettings />);
    click('Posunout doprava');

    click('GLS');
    click('Zásilkovna');

    expect(screen.getByTestId('offset-x')).toHaveTextContent('0 mm');
  });

  it('reset sets the draft back to zero', () => {
    saveLabelOffset('Zasilkovna', { xMm: 3, yMm: -5 });
    render(<LabelOffsetSettings />);

    click(/Vynulovat/);

    expect(screen.getByTestId('offset-x')).toHaveTextContent('0 mm');
    expect(screen.getByTestId('offset-y')).toHaveTextContent('0 mm');
  });
});
