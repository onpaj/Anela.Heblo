export const MAX_PREVIEW_LENGTH = 200;

export interface TruncateOptions {
  searchQuery?: string;
  maxLength?: number;
}

export function truncateContent(content: string, options?: TruncateOptions): string {
  if (!content) return "";

  const maxLength = options?.maxLength ?? MAX_PREVIEW_LENGTH;
  const searchQuery = options?.searchQuery?.trim();

  if (!searchQuery) {
    return content.length <= maxLength ? content : content.substring(0, maxLength) + "...";
  }

  const index = content.toLowerCase().indexOf(searchQuery.toLowerCase());
  if (index === -1) {
    return content.length <= maxLength ? content : content.substring(0, maxLength) + "...";
  }

  const start = Math.max(0, index - Math.floor(maxLength / 2));
  const length = Math.min(maxLength, content.length - start);

  let preview = content.substring(start, start + length);
  if (start > 0) preview = "..." + preview;
  if (start + length < content.length) preview = preview + "...";

  return preview;
}
