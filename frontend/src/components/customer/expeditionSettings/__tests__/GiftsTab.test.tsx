import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import GiftsTab from '../GiftsTab';
import { useGiftSetting, useSetGiftSetting } from '../../../../api/hooks/useGiftSetting';

jest.mock('../../../../api/hooks/useGiftSetting');

const mockUseGiftSetting = useGiftSetting as jest.MockedFunction<typeof useGiftSetting>;
const mockUseSetGiftSetting = useSetGiftSetting as jest.MockedFunction<typeof useSetGiftSetting>;

const createWrapper = () => {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
};

const defaultMutate = jest.fn();

beforeEach(() => {
  mockUseSetGiftSetting.mockReturnValue({
    mutate: defaultMutate,
    isPending: false,
    isError: false,
    error: null,
  } as any);
});

describe('GiftsTab', () => {
  it('renders loading state', () => {
    mockUseGiftSetting.mockReturnValue({ data: undefined, isLoading: true, error: null } as any);
    render(<GiftsTab />, { wrapper: createWrapper() });
    expect(screen.getByRole('status')).toBeInTheDocument();
  });

  it('renders error state', () => {
    mockUseGiftSetting.mockReturnValue({ data: undefined, isLoading: false, error: new Error('fail') } as any);
    render(<GiftsTab />, { wrapper: createWrapper() });
    expect(screen.getByText(/Nepodařilo se načíst/i)).toBeInTheDocument();
  });

  it('renders form with initial values from data', () => {
    mockUseGiftSetting.mockReturnValue({
      data: { isEnabled: true, thresholdCzk: 1500, text: 'DÁREK ZDARMA', modifiedAt: null, modifiedBy: null },
      isLoading: false,
      error: null,
    } as any);

    render(<GiftsTab />, { wrapper: createWrapper() });

    expect((screen.getByRole('spinbutton') as HTMLInputElement).value).toBe('1500');
    expect((screen.getByRole('textbox') as HTMLInputElement).value).toBe('DÁREK ZDARMA');
  });

  it('disables threshold and text inputs when toggle is off', () => {
    mockUseGiftSetting.mockReturnValue({
      data: { isEnabled: false, thresholdCzk: 0, text: '', modifiedAt: null, modifiedBy: null },
      isLoading: false,
      error: null,
    } as any);

    render(<GiftsTab />, { wrapper: createWrapper() });

    expect(screen.getByRole('spinbutton')).toBeDisabled();
    expect(screen.getByRole('textbox')).toBeDisabled();
  });

  it('calls mutate with form values on save', async () => {
    mockUseGiftSetting.mockReturnValue({
      data: { isEnabled: true, thresholdCzk: 1500, text: 'DÁREK ZDARMA', modifiedAt: null, modifiedBy: null },
      isLoading: false,
      error: null,
    } as any);

    render(<GiftsTab />, { wrapper: createWrapper() });

    fireEvent.click(screen.getByRole('button', { name: /Uložit/i }));

    await waitFor(() => {
      expect(defaultMutate).toHaveBeenCalledWith({
        isEnabled: true,
        thresholdCzk: 1500,
        text: 'DÁREK ZDARMA',
      });
    });
  });
});
