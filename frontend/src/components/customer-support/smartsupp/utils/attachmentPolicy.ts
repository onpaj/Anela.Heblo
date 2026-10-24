const MAX_FILES = 5;
export const MAX_FILE_BYTES = 10 * 1024 * 1024;

const MIME_BY_EXTENSION: Readonly<Record<string, string>> = {
  jpg: "image/jpeg",
  jpeg: "image/jpeg",
  png: "image/png",
  gif: "image/gif",
  webp: "image/webp",
  pdf: "application/pdf",
  doc: "application/msword",
  docx: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
  xls: "application/vnd.ms-excel",
  xlsx: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
  txt: "text/plain",
};

export const ATTACHMENT_ACCEPT = Object.keys(MIME_BY_EXTENSION)
  .map((ext) => `.${ext}`)
  .join(",");

const GENERIC_TYPE = "application/octet-stream";

function extensionOf(fileName: string): string {
  const dot = fileName.lastIndexOf(".");
  return dot < 0 ? "" : fileName.slice(dot + 1).toLowerCase();
}

/** Mapped MIME type for an allowed file name, or null when the extension is not allowed. */
export function mimeTypeForFileName(fileName: string): string | null {
  return MIME_BY_EXTENSION[extensionOf(fileName)] ?? null;
}

function isAllowedType(file: File): boolean {
  const mapped = mimeTypeForFileName(file.name);
  if (!mapped) return false;
  const declared = file.type.toLowerCase();
  return declared === "" || declared === GENERIC_TYPE || declared === mapped;
}

export function validateNewFiles(
  current: File[],
  incoming: File[],
): { accepted: File[]; error: string | null } {
  const accepted: File[] = [];
  let error: string | null = null;

  for (const file of incoming) {
    if (!isAllowedType(file)) {
      error = error ?? `Soubor ${file.name} má nepodporovaný typ.`;
    } else if (file.size > MAX_FILE_BYTES) {
      error = error ?? `Soubor ${file.name} je větší než 10 MB.`;
    } else if (current.length + accepted.length >= MAX_FILES) {
      error = error ?? `Najednou lze poslat nejvýše ${MAX_FILES} souborů.`;
    } else {
      accepted.push(file);
    }
  }

  return { accepted, error };
}
