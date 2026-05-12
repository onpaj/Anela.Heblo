import { getSharePointLink } from '../sharepointLink';

describe('getSharePointLink', () => {
  test('returns null for null', () => {
    expect(getSharePointLink(null)).toBeNull();
  });

  test('returns null for undefined', () => {
    expect(getSharePointLink(undefined)).toBeNull();
  });

  test('returns null for empty string', () => {
    expect(getSharePointLink('')).toBeNull();
  });

  test('returns null for synthetic upload path', () => {
    expect(getSharePointLink('upload/abc-123/file.pdf')).toBeNull();
  });

  test('returns the URL verbatim when it starts with https://', () => {
    const url = 'https://anelacz.sharepoint.com/sites/x/doc.docx';
    expect(getSharePointLink(url)).toBe(url);
  });
});
