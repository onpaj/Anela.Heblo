import React, { useState, useEffect } from 'react';
import { AlertCircle, X } from 'lucide-react';
import { useIsMobile } from '../../hooks/useMediaQuery';

const STORAGE_KEY = 'mobile-notice-dismissed';

export const MobileNotice: React.FC = () => {
  const isMobile = useIsMobile();
  const [isDismissed, setIsDismissed] = useState(false);

  useEffect(() => {
    // Check if notice was previously dismissed in this session
    const dismissed = sessionStorage.getItem(STORAGE_KEY);
    if (dismissed === 'true') {
      setIsDismissed(true);
    }
  }, []);

  const handleDismiss = () => {
    sessionStorage.setItem(STORAGE_KEY, 'true');
    setIsDismissed(true);
  };

  // Don't show notice if:
  // - Not on mobile device
  // - User has dismissed it
  if (!isMobile || isDismissed) {
    return null;
  }

  return (
    <div className="bg-yellow-50 dark:bg-amber-900/30 border-l-4 border-yellow-400 dark:border-amber-500 p-4">
      <div className="flex items-start">
        <div className="flex-shrink-0">
          <AlertCircle className="h-5 w-5 text-yellow-400 dark:text-amber-400" />
        </div>
        <div className="ml-3 flex-1">
          <p className="text-sm text-yellow-800 dark:text-amber-300">
            Tato stránka je optimalizována pro desktop. Některé funkce nemusí na mobilním zařízení fungovat správně.
          </p>
        </div>
        <div className="ml-auto pl-3">
          <button
            type="button"
            onClick={handleDismiss}
            className="inline-flex rounded-md bg-yellow-50 dark:bg-amber-900/30 p-1.5 text-yellow-500 dark:text-amber-400 hover:bg-yellow-100 dark:hover:bg-amber-900/50 focus:outline-none focus:ring-2 focus:ring-yellow-600 focus:ring-offset-2 focus:ring-offset-yellow-50"
            aria-label="Zavřít upozornění"
          >
            <X className="h-5 w-5" />
          </button>
        </div>
      </div>
    </div>
  );
};
