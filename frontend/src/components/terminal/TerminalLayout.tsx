import React, { useEffect } from 'react';
import { Outlet, useLocation, useNavigate } from 'react-router-dom';
import { ArrowLeft } from 'lucide-react';
import UserProfile from '../auth/UserProfile';
import { ScanProvider } from './shell/ScanProvider';
import { FlashOverlay } from './shell/FlashOverlay';

const TERMINAL_ROOT = '/terminal';

const TerminalLayout: React.FC = () => {
  const { pathname } = useLocation();
  const navigate = useNavigate();
  const isHome = pathname === TERMINAL_ROOT || pathname === `${TERMINAL_ROOT}/`;

  useEffect(() => {
    const link = document.querySelector<HTMLLinkElement>('link[rel="manifest"]');
    link?.setAttribute("href", "/manifest.terminal.json");
    return () => {
      link?.setAttribute("href", "/manifest.json");
    };
  }, []);

  return (
    <div className="min-h-screen flex flex-col bg-background-gray dark:bg-graphite-bg">
      <header className="relative h-14 sticky top-0 z-10 bg-white dark:bg-graphite-chrome border-b border-border-light dark:border-graphite-border flex items-center px-4 gap-3">
        {!isHome && (
          <button
            onClick={() => navigate(-1)}
            aria-label="Zpět"
            className="p-2 -ml-2 rounded-md text-neutral-gray dark:text-graphite-muted hover:text-primary-blue dark:hover:text-graphite-accent hover:bg-secondary-blue-pale dark:hover:bg-graphite-surface-2 transition-colors"
          >
            <ArrowLeft className="h-5 w-5" />
          </button>
        )}
        <span className="flex-1 text-base font-semibold text-neutral-slate dark:text-graphite-text select-none">
          Heblo Terminál
        </span>
        <UserProfile compact={true} />
      </header>

      <ScanProvider>
        <main className="flex-1 min-h-0 overflow-hidden">
          <Outlet />
        </main>
        <FlashOverlay />
      </ScanProvider>
    </div>
  );
};

export default TerminalLayout;
