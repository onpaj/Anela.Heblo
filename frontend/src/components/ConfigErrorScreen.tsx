import React from 'react';
import { ConfigurationError } from '../config/runtimeConfig';

interface ConfigErrorScreenProps {
  error: ConfigurationError | Error;
  onRetry: () => void;
}

/**
 * Error screen displayed when configuration fails to load
 * Provides retry button to attempt loading again
 */
const ConfigErrorScreen: React.FC<ConfigErrorScreenProps> = ({ error, onRetry }) => {
  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-50">
      <div className="max-w-md w-full bg-white shadow-lg rounded-lg p-8">
        <div className="flex items-center justify-center w-16 h-16 mx-auto mb-4 bg-red-100 rounded-full">
          <svg
            className="w-8 h-8 text-red-600"
            fill="none"
            stroke="currentColor"
            viewBox="0 0 24 24"
          >
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              strokeWidth={2}
              d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"
            />
          </svg>
        </div>

        <h2 className="text-2xl font-semibold text-gray-900 text-center mb-2">
          Chyba při načítání konfigurace
        </h2>

        <p className="text-gray-600 text-center mb-6">
          Nepodařilo se načíst konfiguraci aplikace ze serveru.
        </p>

        <div className="bg-red-50 border border-red-200 rounded-md p-4 mb-6">
          <p className="text-sm text-red-800 font-mono break-words">
            {error.message}
          </p>
          {error instanceof ConfigurationError && error.originalError && (
            <p className="text-xs text-red-600 font-mono mt-2">
              {error.originalError.message}
            </p>
          )}
        </div>

        <button
          onClick={onRetry}
          className="w-full bg-indigo-600 text-white py-3 px-4 rounded-md font-medium hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 transition-colors"
        >
          Zkusit znovu
        </button>

        <p className="text-xs text-gray-500 text-center mt-4">
          Pokud problém přetrvává, kontaktujte administrátora systému.
        </p>
      </div>
    </div>
  );
};

export default ConfigErrorScreen;
