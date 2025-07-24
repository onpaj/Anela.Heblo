import React, { useState } from 'react';
import { 
  BarChart3,
  Building2,
  Folder,
  Plus,
  PanelLeftClose,
  PanelLeftOpen
} from 'lucide-react';
import UserProfile from '../auth/UserProfile';

interface SidebarProps {
  isOpen: boolean;
  isCollapsed: boolean;
  onClose: () => void;
  onToggleCollapse: () => void;
}

const Sidebar: React.FC<SidebarProps> = ({ isOpen, isCollapsed, onClose, onToggleCollapse }) => {
  const [activeItem, setActiveItem] = useState('home');

  // Navigation items matching the design template
  const directoryItems = [
    { id: 'journal', name: 'Journal', href: '/journal', icon: BarChart3 },
    { id: 'local-business', name: 'Local Business', href: '/', icon: Building2, active: true },
  ];

  const pageItems = [
    { id: 'home', name: 'Home', href: '/' },
    { id: 'about', name: 'About Us', href: '/about' },
    { id: 'advertise', name: 'Advertise with Us', href: '/advertise' },
    { id: 'faq', name: 'FAQ', href: '/faq' },
    { id: 'privacy', name: 'Privacy', href: '/privacy' },
    { id: 'terms', name: 'Terms of Use', href: '/terms' },
    { id: 'contact', name: 'Contact', href: '/contact' },
    { id: 'add-page', name: 'Add Page', href: '/add-page' },
  ];

  return (
    <>
      {/* Mobile overlay */}
      {isOpen && (
        <div 
          className="fixed inset-0 flex z-40 md:hidden"
          onClick={onClose}
        >
          <div className="fixed inset-0 bg-gray-600 bg-opacity-75" />
        </div>
      )}

      {/* Sidebar */}
      <div className={`
        fixed top-0 left-0 z-40 h-full bg-white border-r border-gray-200 shadow-sm transform transition-all duration-300 ease-in-out
        ${isOpen ? 'translate-x-0' : '-translate-x-full'} md:translate-x-0
        ${isCollapsed ? 'w-16' : 'w-64'}
      `}>
        <div className="flex flex-col h-full">
          {/* Logo */}
          <div className={`flex items-center h-16 border-b border-gray-200 ${isCollapsed ? 'px-3 justify-center' : 'px-6'}`}>
            <div className="flex items-center">
              <div className="w-8 h-8 bg-indigo-600 rounded flex items-center justify-center">
                <span className="text-white font-bold text-sm">S</span>
              </div>
              {!isCollapsed && (
                <span className="ml-3 text-base font-medium text-gray-900">Site Manager</span>
              )}
            </div>
          </div>

          {/* Navigation */}
          <nav className={`flex-1 py-4 ${isCollapsed ? 'px-2' : 'px-3'}`}>
            {/* Directories Section */}
            {!isCollapsed && (
              <div className="mb-8">
                <div className="flex items-center px-3 mb-3">
                  <Folder className="h-4 w-4 text-gray-400 mr-2" />
                  <h3 className="text-xs font-semibold text-gray-500 uppercase tracking-wide">
                    Directories
                  </h3>
                </div>
                <div className="space-y-1">
                  {directoryItems.map((item) => {
                    const IconComponent = item.icon;
                    const isActive = item.active || activeItem === item.id;
                    return (
                      <a
                        key={item.id}
                        href={item.href}
                        onClick={() => setActiveItem(item.id)}
                        className={`
                          flex items-center px-3 py-2 text-sm font-medium rounded-md transition-colors duration-200
                          ${isActive 
                            ? 'bg-indigo-50 text-indigo-700 border-r-2 border-indigo-700' 
                            : 'text-gray-700 hover:bg-gray-50 hover:text-gray-900'
                          }
                        `}
                      >
                        <IconComponent className={`mr-3 h-5 w-5 ${isActive ? 'text-indigo-500' : 'text-gray-400'}`} />
                        {item.name}
                      </a>
                    );
                  })}
                </div>
              </div>
            )}

            {/* Collapsed Directory Icons */}
            {isCollapsed && (
              <div className="mb-8 space-y-2">
                {directoryItems.map((item) => {
                  const IconComponent = item.icon;
                  const isActive = item.active || activeItem === item.id;
                  return (
                    <a
                      key={item.id}
                      href={item.href}
                      onClick={() => setActiveItem(item.id)}
                      className={`
                        flex items-center justify-center p-2 rounded-md transition-colors duration-200 group relative
                        ${isActive 
                          ? 'bg-indigo-50 text-indigo-700' 
                          : 'text-gray-700 hover:bg-gray-50 hover:text-gray-900'
                        }
                      `}
                      title={item.name}
                    >
                      <IconComponent className={`h-5 w-5 ${isActive ? 'text-indigo-500' : 'text-gray-400'}`} />
                    </a>
                  );
                })}
              </div>
            )}

            {/* Pages Section */}
            {!isCollapsed && (
              <div>
                <div className="flex items-center px-3 mb-3">
                  <Folder className="h-4 w-4 text-gray-400 mr-2" />
                  <h3 className="text-xs font-semibold text-gray-500 uppercase tracking-wide">
                    Pages
                  </h3>
                </div>
                <div className="space-y-1">
                  {pageItems.map((item) => {
                    const isActive = activeItem === item.id;
                    return (
                      <a
                        key={item.id}
                        href={item.href}
                        onClick={() => setActiveItem(item.id)}
                        className={`
                          flex items-center px-3 py-2 text-sm rounded-md transition-colors duration-200
                          ${isActive 
                            ? 'bg-gray-100 text-gray-900' 
                            : 'text-gray-600 hover:bg-gray-50 hover:text-gray-900'
                          }
                        `}
                      >
                        <div className="w-8 h-5 flex items-center">
                          {item.id === 'add-page' ? (
                            <Plus className="h-4 w-4 text-gray-400" />
                          ) : (
                            <div className="w-2 h-2 bg-gray-300 rounded-full"></div>
                          )}
                        </div>
                        {item.name}
                      </a>
                    );
                  })}
                </div>
              </div>
            )}
          </nav>

          {/* User Profile */}
          <div className={`border-t border-gray-200 ${isCollapsed ? 'p-2' : 'p-3'}`}>
            {isCollapsed ? (
              <div className="flex flex-col items-center space-y-2">
                <UserProfile compact={true} />
              </div>
            ) : (
              <div className="flex items-center justify-between">
                <div className="flex-1 mr-2">
                  <UserProfile />
                </div>
                
                {/* Collapse/Expand Button - only show on desktop */}
                <button
                  type="button"
                  className="hidden md:flex p-1.5 rounded-md text-gray-400 hover:text-gray-600 hover:bg-gray-100 focus:outline-none focus:ring-2 focus:ring-inset focus:ring-indigo-500 transition-colors flex-shrink-0"
                  onClick={onToggleCollapse}
                  title="Collapse sidebar"
                >
                  <PanelLeftClose className="h-4 w-4" />
                </button>
              </div>
            )}
          </div>

          {/* Collapsed state fold button */}
          {isCollapsed && (
            <div className="border-t border-gray-200 p-2">
              <button
                type="button"
                className="hidden md:flex w-full justify-center p-1.5 rounded-md text-gray-400 hover:text-gray-600 hover:bg-gray-100 focus:outline-none focus:ring-2 focus:ring-inset focus:ring-indigo-500 transition-colors"
                onClick={onToggleCollapse}
                title="Expand sidebar"
              >
                <PanelLeftOpen className="h-4 w-4" />
              </button>
            </div>
          )}
        </div>
      </div>
    </>
  );
};

export default Sidebar;