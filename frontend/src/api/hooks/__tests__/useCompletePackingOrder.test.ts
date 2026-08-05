import { completePackingOrder } from '../useCompletePackingOrder';
import { getAuthenticatedApiClient } from '../../client';

jest.mock('../../client', () => ({
  getAuthenticatedApiClient: jest.fn(),
}));

const mockPackaging_CompletePacking = jest.fn();

beforeEach(() => {
  jest.clearAllMocks();
  (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
    packaging_CompletePacking: mockPackaging_CompletePacking,
  });
});

describe('completePackingOrder', () => {
  it('calls packaging_CompletePacking with the order code, suppressing global toasts', async () => {
    mockPackaging_CompletePacking.mockResolvedValue({ success: true });

    await completePackingOrder('25/0001');

    expect(getAuthenticatedApiClient).toHaveBeenCalledWith(false);
    expect(mockPackaging_CompletePacking).toHaveBeenCalledWith('25/0001');
  });

  it('throws a friendly message when the server reports failure', async () => {
    mockPackaging_CompletePacking.mockResolvedValue({
      success: false,
      errorCode: 'PackingCompletionFailed',
    });

    await expect(completePackingOrder('250001')).rejects.toThrow(
      'Nepodařilo se dokončit balení objednávky.'
    );
  });
});
