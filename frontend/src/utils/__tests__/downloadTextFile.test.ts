import { sanitizeFilename, downloadTextFile } from '../downloadTextFile';

describe('sanitizeFilename', () => {
  it('lowercases the subject', () => {
    expect(sanitizeFilename('Hello World')).toBe('hello-world');
  });

  it('collapses whitespace runs to a single hyphen', () => {
    expect(sanitizeFilename('foo  bar   baz')).toBe('foo-bar-baz');
  });

  it('trims leading and trailing whitespace', () => {
    expect(sanitizeFilename('  hello  ')).toBe('hello');
  });

  it('strips dangerous filesystem characters', () => {
    expect(sanitizeFilename('foo/bar\\baz:qux*?<>|"')).toBe('foobarbazqux');
  });

  it('preserves Czech diacritics', () => {
    expect(sanitizeFilename('Schůzka s týmem Q2')).toBe('schůzka-s-týmem-q2');
  });

  it('preserves hyphens already in the subject', () => {
    expect(sanitizeFilename('Stand-up meeting')).toBe('stand-up-meeting');
  });

  it('returns "download" when subject contains only special characters', () => {
    expect(sanitizeFilename('???')).toBe('download');
  });

  it('removes trailing hyphens after stripping dangerous characters', () => {
    expect(sanitizeFilename('hello /')).toBe('hello');
  });

  it('returns "download" when subject becomes empty after sanitization', () => {
    expect(sanitizeFilename('   /\\:*?"<>|   ')).toBe('download');
  });
});

describe('downloadTextFile', () => {
  let mockCreateObjectURL: jest.Mock;
  let mockRevokeObjectURL: jest.Mock;
  let mockClick: jest.Mock;
  let mockAnchor: HTMLAnchorElement;
  let appendSpy: jest.SpyInstance;
  let removeSpy: jest.SpyInstance;

  beforeEach(() => {
    mockClick = jest.fn();
    mockCreateObjectURL = jest.fn(() => 'blob:mock-url');
    mockRevokeObjectURL = jest.fn();
    mockAnchor = {
      href: '',
      download: '',
      click: mockClick,
    } as unknown as HTMLAnchorElement;

    global.URL.createObjectURL = mockCreateObjectURL;
    global.URL.revokeObjectURL = mockRevokeObjectURL;
    jest.spyOn(document, 'createElement').mockReturnValue(mockAnchor);

    // Mock appendChild and removeChild to avoid jsdom validation
    appendSpy = jest.spyOn(document.body, 'appendChild').mockImplementation(() => mockAnchor as any);
    removeSpy = jest.spyOn(document.body, 'removeChild').mockImplementation(() => mockAnchor as any);
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('creates a Blob with the given content and MIME type', () => {
    downloadTextFile('hello content', 'file.txt', 'text/plain');
    expect(mockCreateObjectURL).toHaveBeenCalled();
    // The Blob is created internally; we verify the URL was created (which means Blob was created)
    expect(mockCreateObjectURL.mock.calls[0][0]).toBeInstanceOf(Blob);
    expect((mockCreateObjectURL.mock.calls[0][0] as Blob).type).toBe('text/plain');
  });

  it('appends anchor to body, clicks, removes, then revokes URL', () => {
    downloadTextFile('# Summary\nContent', 'meeting-summary.md', 'text/markdown');

    expect(mockAnchor.href).toBe('blob:mock-url');
    expect(mockAnchor.download).toBe('meeting-summary.md');
    expect(appendSpy).toHaveBeenCalledWith(mockAnchor);
    expect(mockClick).toHaveBeenCalledTimes(1);
    expect(removeSpy).toHaveBeenCalledWith(mockAnchor);
    expect(mockRevokeObjectURL).toHaveBeenCalledWith('blob:mock-url');
  });

  it('revokes the object URL after click', () => {
    downloadTextFile('content', 'file.txt', 'text/plain');
    expect(mockRevokeObjectURL).toHaveBeenCalledWith('blob:mock-url');
  });
});
