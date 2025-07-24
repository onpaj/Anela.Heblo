import React, { useState } from 'react';
import { 
  LayoutDashboard,
  FileText,
  DollarSign,
  Truck,
  Factory,
  ShoppingCart,
  Settings,
  ChevronDown,
  ChevronRight,
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
  const [activeItem, setActiveItem] = useState('dashboard');
  const [expandedSections, setExpandedSections] = useState<string[]>(['logistika']);

  // Navigation sections following design system
  const navigationSections = [
    {
      id: 'dashboard',
      name: 'Dashboard',
      href: '/',
      icon: LayoutDashboard,
      type: 'single' as const
    },
    {
      id: 'faktury',
      name: 'Faktury',
      icon: FileText,
      type: 'section' as const,
      items: [
        { id: 'import-shoptet', name: 'Import Shoptet', href: '/faktury/import' }
      ]
    },
    {
      id: 'finance',
      name: 'Finance',
      icon: DollarSign,
      type: 'section' as const,
      items: [
        { id: 'comgate', name: 'Comgate', href: '/finance/comgate' }
      ]
    },
    {
      id: 'logistika',
      name: 'Logistika',
      icon: Truck,
      type: 'section' as const,
      items: [
        { id: 'zavozy', name: 'Závozy', href: '/logistika/zavozy' },
        { id: 'prijem-boxu', name: 'Příjem boxů', href: '/logistika/prijem' },
        { id: 'zasoby', name: 'Zásoby', href: '/logistika/zasoby' }
      ]
    },
    {
      id: 'vyroba',
      name: 'Výroba',
      icon: Factory,
      type: 'section' as const,
      items: [
        { id: 'zasoby-vyrobku', name: 'Zásoby výrobků', href: '/vyroba/zasoby' },
        { id: 'trojclenka', name: 'Trojčlenka', href: '/vyroba/trojclenka' },
        { id: 'inventura', name: 'Inventura', href: '/vyroba/inventura' },
        { id: 'planovani', name: 'Plánování', href: '/vyroba/planovani' }
      ]
    },
    {
      id: 'nakup',
      name: 'Nákup',
      icon: ShoppingCart,
      type: 'section' as const,
      items: [
        { id: 'material-zbozi', name: 'Materiál a zboží', href: '/nakup/material' }
      ]
    },
    {
      id: 'automatizace',
      name: 'Automatizace',
      icon: Settings,
      type: 'section' as const,
      items: [
        { id: 'joby', name: 'Joby', href: '/automatizace/joby' },
        { id: 'hangfire', name: 'Hangfire', href: '/automatizace/hangfire' }
      ]
    }
  ];

  const toggleSection = (sectionId: string) => {
    setExpandedSections(prev => 
      prev.includes(sectionId)
        ? prev.filter(id => id !== sectionId)
        : [...prev, sectionId]
    );
  };

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
                <span className="text-white font-bold text-sm">AH</span>
              </div>
              {!isCollapsed && (
                <span className="ml-3 text-base font-medium text-gray-900">Anela Heblo</span>
              )}
            </div>
          </div>

          {/* Navigation */}
          <nav className={`flex-1 py-4 ${isCollapsed ? 'px-2' : 'px-3'}`}>
            <div className="space-y-1">
              {navigationSections.map((section) => {
                const IconComponent = section.icon;
                const isExpanded = expandedSections.includes(section.id);
                const isActive = activeItem === section.id;
                
                if (section.type === 'single') {
                  // Single item (Dashboard)
                  return (
                    <div key={section.id}>
                      {!isCollapsed ? (
                        <a
                          href={section.href}
                          onClick={() => setActiveItem(section.id)}
                          className={`
                            flex items-center px-3 py-2 text-sm font-medium rounded-md transition-colors duration-300
                            ${isActive 
                              ? 'bg-indigo-50 text-indigo-700 border-r-2 border-indigo-700' 
                              : 'text-gray-700 hover:bg-gray-50 hover:text-gray-900'
                            }
                          `}
                        >
                          <IconComponent className={`mr-3 h-5 w-5 ${isActive ? 'text-indigo-500' : 'text-gray-400'}`} />
                          {section.name}
                        </a>
                      ) : (
                        <a
                          href={section.href}
                          onClick={() => setActiveItem(section.id)}
                          className={`
                            flex items-center justify-center p-2 rounded-md transition-colors duration-300
                            ${isActive 
                              ? 'bg-indigo-50 text-indigo-700' 
                              : 'text-gray-700 hover:bg-gray-50 hover:text-gray-900'
                            }
                          `}
                          title={section.name}
                        >
                          <IconComponent className={`h-5 w-5 ${isActive ? 'text-indigo-500' : 'text-gray-400'}`} />
                        </a>
                      )}
                    </div>
                  );
                }
                
                // Collapsible section
                return (
                  <div key={section.id}>
                    {!isCollapsed ? (
                      <>
                        {/* Section header */}
                        <button
                          onClick={() => toggleSection(section.id)}
                          className={`
                            w-full flex items-center justify-between px-3 py-2 text-sm font-medium rounded-md transition-colors duration-300
                            ${isActive 
                              ? 'bg-indigo-50 text-indigo-700' 
                              : 'text-gray-700 hover:bg-gray-50 hover:text-gray-900'
                            }
                          `}
                        >
                          <div className="flex items-center">
                            <IconComponent className={`mr-3 h-5 w-5 ${isActive ? 'text-indigo-500' : 'text-gray-400'}`} />
                            {section.name}
                          </div>
                          {isExpanded ? (
                            <ChevronDown className="h-4 w-4 text-gray-400 transition-transform duration-300" />
                          ) : (
                            <ChevronRight className="h-4 w-4 text-gray-400 transition-transform duration-300" />
                          )}
                        </button>
                        
                        {/* Sub-items */}
                        {isExpanded && section.items && (
                          <div className="ml-8 mt-1 space-y-1">
                            {section.items.map((subItem) => {
                              const isSubActive = activeItem === subItem.id;
                              return (
                                <a
                                  key={subItem.id}
                                  href={subItem.href}
                                  onClick={() => setActiveItem(subItem.id)}
                                  className={`
                                    block px-3 py-2 text-sm rounded-md transition-colors duration-300
                                    ${isSubActive 
                                      ? 'bg-gray-100 text-gray-900 font-medium' 
                                      : 'text-gray-600 hover:bg-gray-50 hover:text-gray-900'
                                    }
                                  `}
                                >
                                  {subItem.name}
                                </a>
                              );
                            })}
                          </div>
                        )}
                      </>
                    ) : (
                      // Collapsed state - just the icon
                      <button
                        onClick={() => toggleSection(section.id)}
                        className={`
                          flex items-center justify-center p-2 rounded-md transition-colors duration-300
                          ${isActive 
                            ? 'bg-indigo-50 text-indigo-700' 
                            : 'text-gray-700 hover:bg-gray-50 hover:text-gray-900'
                          }
                        `}
                        title={section.name}
                      >
                        <IconComponent className={`h-5 w-5 ${isActive ? 'text-indigo-500' : 'text-gray-400'}`} />
                      </button>
                    )}
                  </div>
                );
              })}
            </div>
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