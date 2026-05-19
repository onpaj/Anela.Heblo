export function sanitizeFilename(subject: string): string {
  return subject
    .trim()
    .replace(/\s+/g, '-')
    .replace(/[/\\:*?"<>|]/g, '')
    .toLowerCase();
}

export function downloadTextFile(content: string, filename: string, mimeType: string): void {
  const blob = new Blob([content], { type: mimeType });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}
