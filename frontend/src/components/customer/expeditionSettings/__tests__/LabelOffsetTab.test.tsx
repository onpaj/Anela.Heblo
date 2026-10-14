import { render, screen, fireEvent } from '@testing-library/react';
import LabelOffsetTab from '../LabelOffsetTab';
import {
  Carriers,
  useCarrierLabelOffsets,
  useSetCarrierLabelOffset,
} from '../../../../api/hooks/useCarrierLabelOffsets';

jest.mock('../../../../api/hooks/useCarrierLabelOffsets', () => ({
  ...jest.requireActual('../../../../api/hooks/useCarrierLabelOffsets'),
  useCarrierLabelOffsets: jest.fn(),
  useSetCarrierLabelOffset: jest.fn(),
}));

const mockUseOffsets = useCarrierLabelOffsets as jest.MockedFunction<typeof useCarrierLabelOffsets>;
const mockUseSetOffset = useSetCarrierLabelOffset as jest.MockedFunction<typeof useSetCarrierLabelOffset>;

const mutate = jest.fn();

const OFFSETS = [
  { carrier: Carriers.Zasilkovna, offsetXMm: 0, offsetYMm: -5, modifiedAt: new Date('2026-10-14T10:00:00Z') },
  { carrier: Carriers.PPL, offsetXMm: 0, offsetYMm: 0 },
  { carrier: Carriers.GLS, offsetXMm: 0, offsetYMm: 0 },
];

beforeEach(() => {
  mutate.mockReset();
  mockUseSetOffset.mockReturnValue({ mutate, isPending: false, isError: false } as any);
  mockUseOffsets.mockReturnValue({ data: OFFSETS, isLoading: false, error: null } as any);
});

describe('LabelOffsetTab', () => {
  it('renders loading state', () => {
    mockUseOffsets.mockReturnValue({ data: undefined, isLoading: true, error: null } as any);
    render(<LabelOffsetTab />);
    expect(screen.getByRole('status')).toBeInTheDocument();
  });

  it('renders error state', () => {
    mockUseOffsets.mockReturnValue({ data: undefined, isLoading: false, error: new Error('fail') } as any);
    render(<LabelOffsetTab />);
    expect(screen.getByText(/Nepodařilo se načíst posun štítků/i)).toBeInTheDocument();
  });

  it('shows one row per carrier with its saved offsets', () => {
    render(<LabelOffsetTab />);

    expect(screen.getByText('Zásilkovna')).toBeInTheDocument();
    expect(screen.getByText('PPL')).toBeInTheDocument();
    expect(screen.getByText('GLS')).toBeInTheDocument();
    expect(screen.getByLabelText('Zásilkovna Y (mm)')).toHaveValue(-5);
    expect(screen.getByLabelText('PPL X (mm)')).toHaveValue(0);
  });

  it('saves the edited carrier offset including negative values', () => {
    render(<LabelOffsetTab />);

    fireEvent.change(screen.getByLabelText('PPL X (mm)'), { target: { value: '-4' } });
    fireEvent.change(screen.getByLabelText('PPL Y (mm)'), { target: { value: '7' } });
    fireEvent.click(screen.getByRole('button', { name: 'Uložit PPL' }));

    expect(mutate).toHaveBeenCalledWith({ carrier: Carriers.PPL, offsetXMm: -4, offsetYMm: 7 });
  });

  it('disables save until the row is changed', () => {
    render(<LabelOffsetTab />);
    expect(screen.getByRole('button', { name: 'Uložit GLS' })).toBeDisabled();
  });

  it('blocks saving an out-of-range or fractional value', () => {
    render(<LabelOffsetTab />);

    fireEvent.change(screen.getByLabelText('GLS X (mm)'), { target: { value: '31' } });
    expect(screen.getByRole('button', { name: 'Uložit GLS' })).toBeDisabled();
    expect(screen.getByText(/celé číslo mezi -30 a 30/)).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('GLS X (mm)'), { target: { value: '2.5' } });
    expect(screen.getByRole('button', { name: 'Uložit GLS' })).toBeDisabled();
  });

  it('shows an error when saving failed', () => {
    mockUseSetOffset.mockReturnValue({ mutate, isPending: false, isError: true } as any);
    render(<LabelOffsetTab />);
    expect(screen.getAllByText('Uložení se nezdařilo.')).toHaveLength(3);
  });
});
