import React, { useEffect } from 'react';
import { Outlet, useLocation, useNavigate } from 'react-router-dom';
import { ArrowLeft } from 'lucide-react';
import UserProfile from '../auth/UserProfile';

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
    <div className="min-h-screen flex flex-col bg-background-gray">
      <header className="h-14 sticky top-0 z-10 bg-white border-b border-border-light flex items-center px-4 gap-3">
        {!isHome && (
          <button
            onClick={() => navigate(TERMINAL_ROOT)}
            aria-label="Zpět"
            className="p-2 -ml-2 rounded-md text-neutral-gray hover:text-primary-blue hover:bg-secondary-blue-pale transition-colors"
          >
            <ArrowLeft className="h-5 w-5" />
          </button>
        )}
        <span className="flex-1 text-base font-semibold text-neutral-slate select-none">
          Heblo Terminál
        </span>
        <UserProfile compact={true} />
      </header>

      <main className="flex-1 overflow-y-auto p-4">
        <div className="max-w-md mx-auto w-full">
          <Outlet />
        </div>
      </main>
    </div>
  );
};

export default TerminalLayout;
