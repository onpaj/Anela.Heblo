import React, { useEffect, useState, useCallback } from 'react';
import { X, CheckCircle, AlertCircle, XCircle, Info } from 'lucide-react';

export interface ToastAction {
  label: string;
  onClick: () => void;
}

export interface ToastProps {
  id: string;
  type: 'success' | 'error' | 'warning' | 'info';
  title: string;
  message?: string;
  duration?: number;
  action?: ToastAction;
  onClose: (id: string) => void;
}

interface ToastIconProps {
  type: ToastProps['type'];
  className?: string;
}

const ToastIcon: React.FC<ToastIconProps> = ({ type, className = "h-5 w-5" }) => {
  switch (type) {
    case 'success':
      return <CheckCircle className={`${className} text-green-500`} />;
    case 'error':
      return <XCircle className={`${className} text-red-500`} />;
    case 'warning':
      return <AlertCircle className={`${className} text-yellow-500`} />;
    case 'info':
      return <Info className={`${className} text-blue-500`} />;
  }
};

const Toast: React.FC<ToastProps> = ({ 
  id, 
  type, 
  title, 
  message, 
  duration = 5000, 
  action,
  onClose 
}) => {
  const [isVisible, setIsVisible] = useState(false);
  const [isLeaving, setIsLeaving] = useState(false);

  const getToastStyles = () => {
    const baseStyles = "max-w-2xl w-full bg-white border rounded-lg shadow-lg pointer-events-auto ring-1 ring-black ring-opacity-5 transform transition-all duration-300 ease-in-out";
    
    switch (type) {
      case 'success':
        return `${baseStyles} border-green-200`;
      case 'error':
        return `${baseStyles} border-red-200`;
      case 'warning':
        return `${baseStyles} border-yellow-200`;
      case 'info':
        return `${baseStyles} border-blue-200`;
      default:
        return `${baseStyles} border-gray-200`;
    }
  };

  const handleClose = useCallback(() => {
    setIsLeaving(true);
    setTimeout(() => onClose(id), 150);
  }, [id, onClose]);

  // Show animation
  useEffect(() => {
    const timer = setTimeout(() => setIsVisible(true), 100);
    return () => clearTimeout(timer);
  }, []);

  // Auto-close timer
  useEffect(() => {
    if (duration > 0) {
      const timer = setTimeout(handleClose, duration);
      return () => clearTimeout(timer);
    }
  }, [duration, id, onClose, handleClose]);

  const transformClass = isLeaving 
    ? 'translate-x-full opacity-0' 
    : isVisible 
    ? 'translate-x-0 opacity-100' 
    : 'translate-x-full opacity-0';

  return (
    <div className={`${getToastStyles()} ${transformClass}`}>
      <div className="p-4">
        <div className="flex items-start">
          <div className="flex-shrink-0">
            <ToastIcon type={type} />
          </div>
          <div className="ml-3 w-0 flex-1">
            <p className="text-sm font-medium text-gray-900">
              {title}
            </p>
            {message && (
              <p className="mt-1 text-sm text-gray-500">
                {message}
              </p>
            )}
            {action && (
              <div className="mt-3 flex space-x-2">
                <button
                  type="button"
                  className="inline-flex items-center px-3 py-1.5 text-xs font-medium text-white bg-indigo-600 hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 rounded-md transition-colors"
                  onClick={() => {
                    action.onClick();
                    handleClose();
                  }}
                >
                  {action.label}
                </button>
              </div>
            )}
          </div>
          <div className="ml-4 flex-shrink-0 flex">
            <button
              type="button"
              className="inline-flex text-gray-400 hover:text-gray-600 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-gray-500 transition-colors"
              onClick={handleClose}
            >
              <span className="sr-only">Zavřít</span>
              <X className="h-5 w-5" />
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};

export default Toast;