import { SwaggerException } from '../../generated/api-client';
import { resolveReprintErrorMessage } from '../reprintErrors';

const swaggerError = (body: string, status = 400) =>
  new SwaggerException('error', status, body, {}, null);

describe('resolveReprintErrorMessage', () => {
  test('returns discarded message for MaterialContainerDiscarded', () => {
    const error = swaggerError(
      JSON.stringify({ success: false, errorCode: 'MaterialContainerDiscarded' }),
    );

    expect(resolveReprintErrorMessage(error)).toBe(
      'Kontejner je vyřazený – štítek nelze dotisknout.',
    );
  });

  test('returns unknown-code message for UnknownMaterialContainerCode', () => {
    const error = swaggerError(
      JSON.stringify({ success: false, errorCode: 'UnknownMaterialContainerCode' }),
      404,
    );

    expect(resolveReprintErrorMessage(error)).toBe(
      'Neznámý štítek – nejprve jej vygenerujte v aplikaci.',
    );
  });

  test('returns non-empty generic message for non-JSON body', () => {
    const message = resolveReprintErrorMessage(swaggerError('<html>', 502));

    expect(message).toEqual(expect.any(String));
    expect(message.length).toBeGreaterThan(0);
  });

  test('returns non-empty generic message for plain Error', () => {
    const message = resolveReprintErrorMessage(new Error('network down'));

    expect(message.length).toBeGreaterThan(0);
  });
});
