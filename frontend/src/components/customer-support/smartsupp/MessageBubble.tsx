import React from "react";
import { MessageDto } from "../../../api/hooks/useSmartsupp";
import AgentBadge from "./AgentBadge";
import MessageDeliveryIcon from "./MessageDeliveryIcon";

interface MessageBubbleProps {
  message: MessageDto;
  agentNames?: Record<string, string>;
}

function formatTime(dateStr: string): string {
  return new Date(dateStr).toLocaleTimeString("cs-CZ", { hour: "2-digit", minute: "2-digit" });
}

function formatResponseTime(seconds: number): string {
  if (seconds < 60) return `Odpověď za ${seconds} s`;
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `Odpověď za ${minutes} m`;
  const hours = Math.round(minutes / 60);
  return `Odpověď za ${hours} h`;
}

const MessageBubble: React.FC<MessageBubbleProps> = ({ message, agentNames }) => {
  const authorType = message.authorType.toLowerCase();
  const isVisitor = authorType === "visitor" || authorType === "contact";
  const isBot = authorType === "bot";
  const isSystem = authorType === "system" || (message.subType ?? "").toLowerCase() === "system";

  if (isSystem) {
    return (
      <div className="flex justify-center my-2">
        <span
          data-testid="system-event"
          className="inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium bg-slate-100 text-slate-600 ring-1 ring-slate-200"
        >
          {message.content ?? ""}
        </span>
      </div>
    );
  }

  if (isBot) {
    return (
      <div className="flex justify-center my-1">
        <span className="text-xs text-gray-400 italic">{message.content ?? ""}</span>
      </div>
    );
  }

  return (
    <div className={`flex flex-col ${isVisitor ? "items-start" : "items-end"} mb-2`}>
      {!isVisitor && (message.agentId || message.authorName) && (
        <div className="mb-1">
          <AgentBadge agentId={message.agentId} name={message.authorName} agentNames={agentNames} />
        </div>
      )}
      <div
        className={`max-w-[70%] rounded-2xl px-4 py-2 ${
          isVisitor
            ? "bg-gray-100 text-gray-900 rounded-tl-sm"
            : "bg-blue-500 text-white rounded-tr-sm"
        }`}
      >
        <p className="text-sm whitespace-pre-wrap break-words">{message.content ?? ""}</p>
        <div className={`flex items-center gap-1.5 text-xs mt-1 ${isVisitor ? "text-gray-400 justify-end" : "text-blue-200 justify-end"}`}>
          <span>{formatTime(message.createdAt)}</span>
          {!isVisitor && <MessageDeliveryIcon status={message.deliveryStatus} />}
        </div>
      </div>
      {!isVisitor && message.isFirstReply && typeof message.responseTime === "number" && (
        <span className="mt-1 inline-flex items-center rounded-full px-2 py-0.5 text-xs bg-emerald-50 text-emerald-700">
          {formatResponseTime(message.responseTime)}
        </span>
      )}
    </div>
  );
};

export default MessageBubble;
