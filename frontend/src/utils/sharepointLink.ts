export function getSharePointLink(sourcePath: string | null | undefined): string | null {
  if (!sourcePath) return null;
  if (sourcePath.startsWith('https://')) return sourcePath;
  return null;
}
