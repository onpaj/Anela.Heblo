import React from 'react';
import { Menu } from 'lucide-react';
import UserProfile from '../auth/UserProfile';

interface TopBarProps {
  onMenuClick: () => void;
}

const TopBar: React.FC<TopBarProps> = ({ onMenuClick }) => {

  return (
    <header className="fixed top-0 left-0 right-0 z-50 bg-white border-b border-gray-200 shadow-sm">
      <div className="flex items-center justify-between h-16 px-4">
        {/* Left side - Mobile menu button and App logo */}
        <div className="flex items-center">
          {/* Mobile menu button */}
          <button
            type="button"
            className="md:hidden p-2 rounded-md text-neutral-gray hover:text-primary-blue hover:bg-secondary-blue-pale focus:outline-none focus:ring-2 focus:ring-primary mr-2"
            onClick={onMenuClick}
          >
            <span className="sr-only">Open menu</span>
            <Menu className="h-6 w-6" />
          </button>

          {/* App Logo/Title */}
          <div className="flex items-center">
            <div className="w-8 h-8 bg-primary-blue rounded flex items-center justify-center">
              <span className="text-white font-bold text-sm">AH</span>
            </div>
            <span className="ml-3 text-base font-medium text-gray-900 hidden sm:block">Anela Heblo</span>
          </div>
        </div>

        {/* Right side - User Profile Menu */}
        <div className="flex items-center">
          <UserProfile />
        </div>
      </div>
    </header>
  );
};

export default TopBar;