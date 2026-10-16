import React from "react";
import { FileText, Paperclip } from "lucide-react";
import { MessageAttachmentDto } from "../../../api/generated/api-client";
import { isRenderableUrl } from "./utils/linkify";

interface MessageAttachmentsProps {
  attachments: MessageAttachmentDto[];
  tone: "visitor" | "agent";
  allowBlob?: boolean;
}

const KIB = 1024;
const MIB = KIB * KIB;

export function formatFileSize(size: number): string {
  if (size < KIB) return `${size} B`;
  if (size < MIB) return `${Math.floor(size / KIB)} kB`;
  return `${(size / MIB).toFixed(1).replace(".", ",")} MB`;
}

function isImage(attachment: MessageAttachmentDto): boolean {
  return attachment.type === "image" || (attachment.fileType ?? "").toLowerCase().startsWith("image/");
}

const MessageAttachments: React.FC<MessageAttachmentsProps> = ({ attachments, tone, allowBlob = false }) => {
  const visible = attachments.filter((a) => isRenderableUrl(a.url, allowBlob));
  if (visible.length === 0) return null;

  const chipClass =
    tone === "visitor"
      ? "bg-white/70 dark:bg-graphite-surface text-gray-800 dark:text-graphite-text"
      : "bg-white/20 dark:bg-blue-950/40 text-white dark:text-blue-50";

  return (
    <div className="flex flex-col gap-1.5 mt-1" data-testid="message-attachments">
      {visible.map((attachment, index) => {
        const name = attachment.fileName ?? "Příloha";
        const url = attachment.url as string;
        if (isImage(attachment)) {
          const thumb = isRenderableUrl(attachment.thumbUrl, allowBlob) ? (attachment.thumbUrl as string) : url;
          return (
            <a key={index} href={url} target="_blank" rel="noopener noreferrer">
              <img src={thumb} alt={name} loading="lazy" className="max-w-[240px] rounded-lg" />
            </a>
          );
        }
        const Icon = (attachment.fileType ?? "").toLowerCase() === "application/pdf" ? FileText : Paperclip;
        return (
          <a
            key={index}
            href={url}
            target="_blank"
            rel="noopener noreferrer"
            className={`inline-flex items-center gap-2 rounded-lg px-2.5 py-1.5 text-sm ${chipClass}`}
          >
            <Icon className="h-4 w-4 shrink-0" aria-hidden="true" />
            <span className="truncate max-w-[180px] underline">{name}</span>
            {typeof attachment.size === "number" && (
              <span className="text-xs opacity-80 shrink-0">{formatFileSize(attachment.size)}</span>
            )}
          </a>
        );
      })}
    </div>
  );
};

export default MessageAttachments;
