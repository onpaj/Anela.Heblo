import { SwaggerException } from '../generated/api-client';
import { callApi, readApiErrorEnvelope } from '../apiErrorEnvelope';

const swaggerException = (status: number, body: unknown) =>
  new SwaggerException(
    'An unexpected server error occurred.',
    status,
    typeof body === 'string' ? body : JSON.stringify(body),
    {},
    null,
  );

describe('readApiErrorEnvelope', () => {
  it('reads errorCode and params from a SwaggerException carrying a BaseResponse body', () => {
    // Arrange
    const error = swaggerException(404, {
      success: false,
      errorCode: 'ShoptetOrderNotFound',
      params: { orderCode: '00000000' },
    });

    // Act
    const envelope = readApiErrorEnvelope(error);

    // Assert
    expect(envelope).toEqual({
      errorCode: 'ShoptetOrderNotFound',
      params: { orderCode: '00000000' },
    });
  });

  it('reads errorCode from an error object that carries it directly', () => {
    // Arrange — endpoints with typed non-200 branches throw the parsed result, not a
    // SwaggerException, so the fields sit on the thrown object itself.
    const error = { errorCode: 'PackingUserNotEligible', params: { userId: '7' } };

    // Act
    const envelope = readApiErrorEnvelope(error);

    // Assert
    expect(envelope).toEqual({
      errorCode: 'PackingUserNotEligible',
      params: { userId: '7' },
    });
  });

  // Captured verbatim from a staging 404 on POST /api/packaging/orders/00000000/scan —
  // the envelope spells absent params as an explicit null, which callers never guard for.
  it('normalizes a null params field to undefined', () => {
    // Arrange
    const error = swaggerException(
      404,
      '{"order":null,"shipment":null,"success":false,"errorCode":"ShoptetOrderNotFound","params":null}',
    );

    // Act
    const envelope = readApiErrorEnvelope(error);

    // Assert
    expect(envelope).toEqual({ errorCode: 'ShoptetOrderNotFound', params: undefined });
  });

  it('returns null for a network failure that carries no envelope', () => {
    expect(readApiErrorEnvelope(new TypeError('Failed to fetch'))).toBeNull();
  });

  it('returns null when the response body is not JSON', () => {
    expect(readApiErrorEnvelope(swaggerException(502, '<html>Bad Gateway</html>'))).toBeNull();
  });

  it('returns null when the body is JSON but reports no error code', () => {
    expect(readApiErrorEnvelope(swaggerException(500, { success: false }))).toBeNull();
  });
});

describe('callApi', () => {
  const toMessage = jest.fn<string, [{ errorCode?: string }]>();

  beforeEach(() => {
    // CRA's jest config sets `resetMocks: true`, which drops any implementation passed to
    // `jest.fn()` at construction — it has to be re-applied per test.
    toMessage.mockImplementation((envelope) => envelope.errorCode ?? 'generic');
  });

  it('returns the response untouched on success', async () => {
    // Arrange
    const response = { success: true, value: 42 };

    // Act
    const result = await callApi(async () => response, toMessage);

    // Assert
    expect(result).toBe(response);
    expect(toMessage).not.toHaveBeenCalled();
  });

  it('maps a thrown SwaggerException to the caller message via its errorCode', async () => {
    // Arrange
    const call = async (): Promise<{ success?: boolean }> => {
      throw swaggerException(404, { success: false, errorCode: 'ShoptetOrderNotFound' });
    };

    // Act & Assert
    await expect(callApi(call, toMessage)).rejects.toThrow('ShoptetOrderNotFound');
    expect(toMessage).toHaveBeenCalledWith({ errorCode: 'ShoptetOrderNotFound', params: undefined });
  });

  it('maps an unparseable failure to the caller message with no error code', async () => {
    // Arrange
    const call = async (): Promise<{ success?: boolean }> => {
      throw new TypeError('Failed to fetch');
    };

    // Act & Assert
    await expect(callApi(call, toMessage)).rejects.toThrow('generic');
    expect(toMessage).toHaveBeenCalledWith({});
  });

  it('still maps a resolved success:false envelope, for endpoints that return 200 on failure', async () => {
    // Arrange
    const call = async () => ({ success: false, errorCode: 'PackingCompletionFailed' });

    // Act & Assert
    await expect(callApi(call, toMessage)).rejects.toThrow('PackingCompletionFailed');
  });
});
