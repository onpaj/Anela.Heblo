import React from 'react';
import { Menu, Search, Settings, Plus, ChevronDown } from 'lucide-react';

interface TopBarProps {
  onMenuClick: () => void;
}

const TopBar: React.FC<TopBarProps> = ({ onMenuClick }) => {

  return (
    <header className="bg-white border-b border-gray-200 sticky top-0 z-30 shadow-sm">
      <div className="flex items-center justify-between h-16 px-4 sm:px-6 lg:px-8">
        {/* Mobile menu button */}
        <button
          type="button"
          className="md:hidden p-2 rounded-md text-gray-400 hover:text-gray-500 hover:bg-gray-100 focus:outline-none focus:ring-2 focus:ring-inset focus:ring-indigo-500"
          onClick={onMenuClick}
        >
          <span className="sr-only">Open menu</span>
          <Menu className="h-6 w-6" />
        </button>

        {/* Right side - Search and actions */}
        <div className="flex items-center space-x-4 ml-auto">
          {/* Search */}
          <div className="hidden sm:block">
            <div className="relative">
              <input
                type="text"
                placeholder="Search"
                className="w-80 px-4 py-2 pl-10 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
              />
              <div className="absolute inset-y-0 left-0 flex items-center pl-3">
                <Search className="h-4 w-4 text-gray-400" />
              </div>
              <div className="absolute inset-y-0 right-0 flex items-center pr-3">
                <ChevronDown className="h-4 w-4 text-gray-400" />
              </div>
            </div>
          </div>

          {/* Settings button */}
          <button className="p-2 text-gray-400 hover:text-gray-500 hover:bg-gray-100 rounded-md transition-colors">
            <Settings className="h-5 w-5" />
          </button>

          {/* Add button */}
          <button className="flex items-center px-4 py-2 bg-rose-400 text-white text-sm font-medium rounded-md hover:bg-rose-500 transition-colors focus:outline-none focus:ring-2 focus:ring-rose-500 focus:ring-offset-2">
            <Plus className="h-4 w-4 mr-1" />
            <ChevronDown className="h-4 w-4 ml-1" />
          </button>

          {/* Mobile search button */}
          <button className="sm:hidden p-2 text-gray-400 hover:text-gray-500 hover:bg-gray-100 rounded-md">
            <Search className="h-5 w-5" />
          </button>
        </div>
      </div>
    </header>
  );
};

export default TopBar;