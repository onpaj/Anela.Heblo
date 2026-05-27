import React from "react";
import { Check, CheckCheck, AlertCircle, Loader2 } from "lucide-react";

interface MessageDeliveryIconProps {
  status?: string | null;
}

interface IconSpec {
  Icon: React.ComponentType<{ className?: string }>;
  className: string;
  label: string;
}

function resolveIcon(status: string): IconSpec | null {
  switch (status.toLowerCase()) {
    case "pending":
      return { Icon: Loader2, className: "text-blue-100 animate-spin", label: "Odesílá se" };
    case "sent":
      return { Icon: Check, className: "text-blue-100", label: "Odesláno" };
    case "delivered":
    case "read":
      return { Icon: CheckCheck, className: "text-blue-100", label: "Doručeno" };
    case "failed":
      return { Icon: AlertCircle, className: "text-red-200", label: "Doručení selhalo" };
    default:
      return { Icon: Check, className: "text-blue-200", label: status };
  }
}

const MessageDeliveryIcon: React.FC<MessageDeliveryIconProps> = ({ status }) => {
  if (!status) return null;
  const spec = resolveIcon(status);
  if (!spec) return null;
  return (
    <span
      data-testid="delivery-icon"
      title={spec.label}
      className="inline-flex items-center"
      aria-label={spec.label}
    >
      <spec.Icon className={`w-3 h-3 ${spec.className}`} />
    </span>
  );
};

export default MessageDeliveryIcon;
