import { completePackingOrder } from '../useCompletePackingOrder';
import { getAuthenticatedApiClient } from '../../client';
import { SwaggerException } from '../../generated/api-client';

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

  // PackingCompletionFailed maps to HTTP 503, so the generated client rejects rather than
  // resolving — the envelope has to be read off the exception.
  it('throws a friendly message when the client rejects with a non-200 carrying the envelope', async () => {
    mockPackaging_CompletePacking.mockRejectedValue(
      new SwaggerException(
        'An unexpected server error occurred.',
        503,
        JSON.stringify({ success: false, errorCode: 'PackingCompletionFailed' }),
        {},
        null,
      ),
    );

    await expect(completePackingOrder('250001')).rejects.toThrow(
      'Nepodařilo se dokončit balení objednávky.'
    );
  });

  it('throws a generic message when the rejection carries no error envelope', async () => {
    mockPackaging_CompletePacking.mockRejectedValue(new TypeError('Failed to fetch'));

    await expect(completePackingOrder('250001')).rejects.toThrow('Chyba při dokončení balení.');
  });
});
