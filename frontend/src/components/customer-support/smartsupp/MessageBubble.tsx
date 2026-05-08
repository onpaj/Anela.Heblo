import React from "react";
import { MessageDto } from "../../../api/hooks/useSmartsupp";

interface MessageBubbleProps {
  message: MessageDto;
}

function formatTime(dateStr: string): string {
  return new Date(dateStr).toLocaleTimeString("cs-CZ", { hour: "2-digit", minute: "2-digit" });
}

const MessageBubble: React.FC<MessageBubbleProps> = ({ message }) => {
  const isVisitor = message.authorType.toLowerCase() === "visitor";
  const isBot = message.authorType.toLowerCase() === "bot";

  if (isBot) {
    return (
      <div className="flex justify-center my-1">
        <span className="text-xs text-gray-400 italic">{message.content ?? ""}</span>
      </div>
    );
  }

  return (
    <div className={`flex ${isVisitor ? "justify-start" : "justify-end"} mb-2`}>
      <div
        className={`max-w-[70%] rounded-2xl px-4 py-2 ${
          isVisitor
            ? "bg-gray-100 text-gray-900 rounded-tl-sm"
            : "bg-blue-500 text-white rounded-tr-sm"
        }`}
      >
        {!isVisitor && message.authorName && (
          <div className="text-xs text-blue-200 mb-1">{message.authorName}</div>
        )}
        <p className="text-sm whitespace-pre-wrap break-words">{message.content ?? ""}</p>
        <div className={`text-xs mt-1 ${isVisitor ? "text-gray-400" : "text-blue-200"} text-right`}>
          {formatTime(message.createdAt)}
        </div>
      </div>
    </div>
  );
};

export default MessageBubble;
