import React from "react";
import { createPortal } from "react-dom";
import Toast, { ToastProps } from "./Toast";

interface ToastContainerProps {
  toasts: ToastProps[];
  onClose: (id: string) => void;
}

const ToastContainer: React.FC<ToastContainerProps> = ({ toasts, onClose }) => {
  if (toasts.length === 0) return null;

  return createPortal(
    <div className="fixed top-4 right-4 z-50 space-y-4 min-w-[600px]">
      {toasts.map((toast) => (
        <Toast key={toast.id} {...toast} onClose={onClose} />
      ))}
    </div>,
    document.body,
  );
};

export default ToastContainer;
