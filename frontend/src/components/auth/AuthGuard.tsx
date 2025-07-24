import React, { useEffect } from 'react';
import { useAuth } from '../../auth/useAuth';

interface AuthGuardProps {
  children: React.ReactNode;
}

const AuthGuard: React.FC<AuthGuardProps> = ({ children }) => {
  const { isAuthenticated, inProgress, login } = useAuth();

  useEffect(() => {
    // If not authenticated and not in progress, trigger login
    if (!isAuthenticated && inProgress === 'none') {
      login().catch(error => {
        console.error('Authentication failed:', error);
      });
    }
  }, [isAuthenticated, inProgress, login]);

  // Show loading while authentication is in progress
  if (inProgress !== 'none') {
    return (
      <div className="min-h-screen flex items-center justify-center bg-gray-50">
        <div className="text-center">
          <div className="inline-block animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600 mb-4"></div>
          <p className="text-gray-600">Přihlašování...</p>
        </div>
      </div>
    );
  }

  // Show loading while waiting for authentication check
  if (!isAuthenticated) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-gray-50">
        <div className="text-center">
          <div className="inline-block animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600 mb-4"></div>
          <p className="text-gray-600">Kontrola přihlášení...</p>
        </div>
      </div>
    );
  }

  // User is authenticated, render the protected content
  return <>{children}</>;
};

export default AuthGuard;