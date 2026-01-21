import React from 'react';

/**
 * Loading screen displayed while configuration is being loaded from backend
 */
const ConfigLoadingScreen: React.FC = () => {
  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-50">
      <div className="text-center">
        <div className="inline-block animate-spin rounded-full h-16 w-16 border-b-4 border-indigo-600 mb-4"></div>
        <h2 className="text-2xl font-semibold text-gray-900 mb-2">
          Načítání konfigurace...
        </h2>
        <p className="text-gray-600">
          Prosím počkejte, zatímco načítáme nastavení aplikace
        </p>
      </div>
    </div>
  );
};

export default ConfigLoadingScreen;
