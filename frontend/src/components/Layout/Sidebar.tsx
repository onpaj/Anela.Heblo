import React from 'react';
import { useTranslation } from 'react-i18next';

interface SidebarProps {
  isOpen: boolean;
  onClose: () => void;
}

const Sidebar: React.FC<SidebarProps> = ({ isOpen, onClose }) => {
  const { t } = useTranslation();

  // Navigation items based on the architecture document modules
  const navigationItems = [
    { name: t('navigation.catalog'), href: '/catalog', icon: '📚' },
    { name: t('navigation.manufacture'), href: '/manufacture', icon: '🏭' },
    { name: t('navigation.purchase'), href: '/purchase', icon: '🛒' },
    { name: t('navigation.transport'), href: '/transport', icon: '📦' },
    { name: t('navigation.invoices'), href: '/invoices', icon: '📄' },
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
        fixed top-0 left-0 z-40 w-64 h-full bg-sidebar border-r border-gray-200 transform transition-transform duration-300 ease-in-out
        ${isOpen ? 'translate-x-0' : '-translate-x-full'} md:translate-x-0
      `}>
        <div className="flex flex-col h-full">
          {/* Logo */}
          <div className="flex items-center justify-center h-16 border-b border-gray-200">
            <h1 className="text-xl font-bold text-gray-900">Anela Heblo</h1>
          </div>

          {/* Navigation */}
          <nav className="flex-1 px-4 py-6 space-y-2">
            {navigationItems.map((item) => (
              <a
                key={item.name}
                href={item.href}
                className="flex items-center px-4 py-3 text-sm font-medium text-gray-700 rounded-lg hover:bg-hover transition-colors duration-200"
              >
                <span className="mr-3 text-lg">{item.icon}</span>
                {item.name}
              </a>
            ))}
          </nav>

          {/* Footer */}
          <div className="p-4 border-t border-gray-200">
            <p className="text-xs text-gray-400">
              © 2024 Anela Heblo
            </p>
          </div>
        </div>
      </div>
    </>
  );
};

export default Sidebar;