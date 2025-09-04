import React, { createContext, useContext, useState, useCallback, ReactNode } from 'react';
import { ToastProps, ToastAction } from '../components/ui/Toast';
import ToastContainer from '../components/ui/ToastContainer';

interface ToastOptions {
  duration?: number;
  action?: ToastAction;
  onClose?: () => void;
}

interface ToastContextType {
  showToast: (toast: Omit<ToastProps, 'id' | 'onClose'>) => void;
  showSuccess: (title: string, message?: string, options?: ToastOptions) => void;
  showError: (title: string, message?: string, options?: ToastOptions) => void;
  showWarning: (title: string, message?: string, options?: ToastOptions) => void;
  showInfo: (title: string, message?: string, options?: ToastOptions) => void;
}

const ToastContext = createContext<ToastContextType | undefined>(undefined);

interface ToastProviderProps {
  children: ReactNode;
}

export const ToastProvider: React.FC<ToastProviderProps> = ({ children }) => {
  const [toasts, setToasts] = useState<ToastProps[]>([]);

  const removeToast = useCallback((id: string) => {
    setToasts(prev => {
      const toastToRemove = prev.find(t => t.id === id);
      if (toastToRemove && (toastToRemove as any).onCloseCallback) {
        (toastToRemove as any).onCloseCallback();
      }
      return prev.filter(toast => toast.id !== id);
    });
  }, []);

  const showToast = useCallback((toast: Omit<ToastProps, 'id' | 'onClose'> & { onClose?: () => void }) => {
    const id = `toast-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;
    const newToast: ToastProps & { onCloseCallback?: () => void } = {
      ...toast,
      id,
      onClose: removeToast,
      onCloseCallback: toast.onClose
    };
    
    setToasts(prev => [...prev, newToast]);
  }, [removeToast]);

  const showSuccess = useCallback((title: string, message?: string, options?: ToastOptions) => {
    showToast({ 
      type: 'success', 
      title, 
      message, 
      duration: options?.duration,
      action: options?.action,
      onClose: options?.onClose
    });
  }, [showToast]);

  const showError = useCallback((title: string, message?: string, options?: ToastOptions) => {
    showToast({ 
      type: 'error', 
      title, 
      message, 
      duration: options?.duration ?? 8000, // Longer duration for errors
      action: options?.action,
      onClose: options?.onClose
    });
  }, [showToast]);

  const showWarning = useCallback((title: string, message?: string, options?: ToastOptions) => {
    showToast({ 
      type: 'warning', 
      title, 
      message, 
      duration: options?.duration,
      action: options?.action,
      onClose: options?.onClose
    });
  }, [showToast]);

  const showInfo = useCallback((title: string, message?: string, options?: ToastOptions) => {
    showToast({ 
      type: 'info', 
      title, 
      message, 
      duration: options?.duration,
      action: options?.action,
      onClose: options?.onClose
    });
  }, [showToast]);

  const value: ToastContextType = {
    showToast,
    showSuccess,
    showError,
    showWarning,
    showInfo,
  };

  return (
    <ToastContext.Provider value={value}>
      {children}
      <ToastContainer toasts={toasts} onClose={removeToast} />
    </ToastContext.Provider>
  );
};

export const useToast = (): ToastContextType => {
  const context = useContext(ToastContext);
  if (!context) {
    throw new Error('useToast must be used within a ToastProvider');
  }
  return context;
};