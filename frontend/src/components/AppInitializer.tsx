import React, { useEffect, ReactNode } from 'react';
import { useToast } from '../contexts/ToastContext';
import { setGlobalToastHandler } from '../api/client';

interface AppInitializerProps {
  children: ReactNode;
}

/**
 * Component that initializes global toast handler for API errors
 * Must be rendered inside ToastProvider context
 */
export const AppInitializer: React.FC<AppInitializerProps> = ({ children }) => {
  const { showError } = useToast();

  useEffect(() => {
    // Set global toast handler for API errors
    setGlobalToastHandler(showError);
    
    return () => {
      // Cleanup on unmount (though this shouldn't happen in normal app lifecycle)
      setGlobalToastHandler(() => {});
    };
  }, [showError]);

  return <>{children}</>;
};