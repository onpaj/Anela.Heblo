import React from 'react';
import { useTranslation } from 'react-i18next';

interface TopBarProps {
  onMenuClick: () => void;
}

const TopBar: React.FC<TopBarProps> = ({ onMenuClick }) => {
  const { t } = useTranslation();

  return (
    <header className="bg-white border-b border-gray-200 sticky top-0 z-30">
      <div className="flex items-center justify-between h-16 px-4 sm:px-6 lg:px-8">
        {/* Mobile menu button */}
        <button
          type="button"
          className="md:hidden p-2 rounded-md text-gray-400 hover:text-gray-500 hover:bg-gray-100 focus:outline-none focus:ring-2 focus:ring-inset focus:ring-accent"
          onClick={onMenuClick}
        >
          <span className="sr-only">{t('common.openMenu')}</span>
          <svg className="h-6 w-6" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 6h16M4 12h16M4 18h16" />
          </svg>
        </button>

        {/* Right side - User menu and actions */}
        <div className="flex items-center space-x-4 ml-auto">
          {/* Search placeholder */}
          <div className="hidden sm:block">
            <div className="relative">
              <input
                type="text"
                placeholder={t('common.search')}
                className="w-64 px-4 py-2 text-sm border border-gray-300 rounded-lg focus:outline-none focus:ring-2 focus:ring-accent focus:border-transparent"
              />
              <div className="absolute inset-y-0 right-0 flex items-center pr-3">
                <svg className="h-4 w-4 text-gray-400" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
                </svg>
              </div>
            </div>
          </div>

          {/* User avatar placeholder */}
          <div className="flex items-center">
            <div className="h-8 w-8 bg-accent rounded-full flex items-center justify-center">
              <span className="text-white text-sm font-medium">U</span>
            </div>
          </div>
        </div>
      </div>
    </header>
  );
};

export default TopBar;