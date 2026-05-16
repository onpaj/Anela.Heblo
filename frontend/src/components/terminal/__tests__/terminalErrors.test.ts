import { getTerminalErrorMessage } from '../terminalErrors';

describe('getTerminalErrorMessage', () => {
  it('returns error.message when Error has a clean message', () => {
    const error = new Error('Server validation failed');
    const result = getTerminalErrorMessage(error);
    expect(result).toBe('Server validation failed');
  });

  it('returns Czech fallback when Error message is "Failed to fetch"', () => {
    const error = new Error('Failed to fetch');
    const result = getTerminalErrorMessage(error);
    expect(result).toBe('Nepodařilo se spojit se serverem. Zkuste to znovu.');
  });

  it('returns Czech fallback when Error message contains "NetworkError"', () => {
    const error = new Error('NetworkError when attempting to fetch resource');
    const result = getTerminalErrorMessage(error);
    expect(result).toBe('Nepodařilo se spojit se serverem. Zkuste to znovu.');
  });

  it('returns Czech fallback when Error message is "Load failed"', () => {
    const error = new Error('Load failed');
    const result = getTerminalErrorMessage(error);
    expect(result).toBe('Nepodařilo se spojit se serverem. Zkuste to znovu.');
  });

  it('returns Czech fallback when error is not an Error instance', () => {
    const result = getTerminalErrorMessage(null);
    expect(result).toBe('Nepodařilo se spojit se serverem. Zkuste to znovu.');
  });

  it('returns Czech fallback when error is a plain string', () => {
    const result = getTerminalErrorMessage('something went wrong');
    expect(result).toBe('Nepodařilo se spojit se serverem. Zkuste to znovu.');
  });

  it('returns Czech fallback when error is a plain object', () => {
    const result = getTerminalErrorMessage({ message: 'oops' });
    expect(result).toBe('Nepodařilo se spojit se serverem. Zkuste to znovu.');
  });

  it('returns Czech fallback when Error has empty message', () => {
    const error = new Error('');
    const result = getTerminalErrorMessage(error);
    expect(result).toBe('Nepodařilo se spojit se serverem. Zkuste to znovu.');
  });

  it('returns Czech fallback when Error message contains "Failed to fetch" as substring', () => {
    const error = new Error('Connection: Failed to fetch data from API');
    const result = getTerminalErrorMessage(error);
    expect(result).toBe('Nepodařilo se spojit se serverem. Zkuste to znovu.');
  });

  it('returns Czech fallback when Error message contains "Load failed" as substring', () => {
    const error = new Error('Image Load failed: timeout');
    const result = getTerminalErrorMessage(error);
    expect(result).toBe('Nepodařilo se spojit se serverem. Zkuste to znovu.');
  });

  it('returns error.message for case-sensitive network error that does not match', () => {
    const error = new Error('failed to fetch'); // lowercase 'f', should not match
    const result = getTerminalErrorMessage(error);
    expect(result).toBe('failed to fetch');
  });
});
