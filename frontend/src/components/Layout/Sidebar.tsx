import React, { useState } from 'react';
import { Link } from 'react-router-dom';
import { 
  LayoutDashboard,
  FileText,
  Package,
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
      id: 'catalog',
      name: 'Katalog',
      href: '/catalog',
      icon: Package,
      type: 'single' as const
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
        fixed top-16 left-0 z-40 bottom-0 bg-white border-r border-gray-200 shadow-sm transform transition-all duration-300 ease-in-out
        ${isOpen ? 'translate-x-0' : '-translate-x-full'} md:translate-x-0
        ${isCollapsed ? 'w-16' : 'w-64'}
      `}>
        <div className="flex flex-col h-full">
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
                        <Link
                          to={section.href!}
                          onClick={() => setActiveItem(section.id)}
                          className={`
                            flex items-center px-3 py-2 text-sm font-medium rounded-md transition-colors duration-300
                            ${isActive 
                              ? 'bg-secondary-blue-pale text-primary-blue border-r-2 border-primary-blue' 
                              : 'text-neutral-slate hover:bg-secondary-blue-pale/50 hover:text-neutral-slate'
                            }
                          `}
                        >
                          <IconComponent className={`mr-3 h-5 w-5 ${isActive ? 'text-primary-blue' : 'text-neutral-gray'}`} />
                          {section.name}
                        </Link>
                      ) : (
                        <Link
                          to={section.href!}
                          onClick={(e) => {
                            setActiveItem(section.id);
                            // Auto-expand sidebar when clicking collapsed menu item
                            onToggleCollapse();
                          }}
                          className={`
                            flex items-center justify-center p-2 rounded-md transition-colors duration-300
                            ${isActive 
                              ? 'bg-secondary-blue-pale text-primary-blue' 
                              : 'text-neutral-slate hover:bg-secondary-blue-pale/50 hover:text-neutral-slate'
                            }
                          `}
                          title={section.name}
                        >
                          <IconComponent className={`h-5 w-5 ${isActive ? 'text-primary-blue' : 'text-neutral-gray'}`} />
                        </Link>
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
                              ? 'bg-secondary-blue-pale text-primary-blue' 
                              : 'text-neutral-slate hover:bg-secondary-blue-pale/50 hover:text-neutral-slate'
                            }
                          `}
                        >
                          <div className="flex items-center">
                            <IconComponent className={`mr-3 h-5 w-5 ${isActive ? 'text-primary-blue' : 'text-neutral-gray'}`} />
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
                                <Link
                                  key={subItem.id}
                                  to={subItem.href}
                                  onClick={() => setActiveItem(subItem.id)}
                                  className={`
                                    block px-3 py-2 text-sm rounded-md transition-colors duration-300
                                    ${isSubActive 
                                      ? 'bg-secondary-blue-pale text-primary-blue font-medium' 
                                      : 'text-neutral-gray hover:bg-secondary-blue-pale/30 hover:text-neutral-slate'
                                    }
                                  `}
                                >
                                  {subItem.name}
                                </Link>
                              );
                            })}
                          </div>
                        )}
                      </>
                    ) : (
                      // Collapsed state - just the icon
                      <button
                        onClick={() => {
                          toggleSection(section.id);
                          // Auto-expand sidebar when clicking collapsed menu item
                          onToggleCollapse();
                        }}
                        className={`
                          flex items-center justify-center p-2 rounded-md transition-colors duration-300
                          ${isActive 
                            ? 'bg-secondary-blue-pale text-primary-blue' 
                            : 'text-neutral-slate hover:bg-secondary-blue-pale/50 hover:text-neutral-slate'
                          }
                        `}
                        title={section.name}
                      >
                        <IconComponent className={`h-5 w-5 ${isActive ? 'text-primary-blue' : 'text-neutral-gray'}`} />
                      </button>
                    )}
                  </div>
                );
              })}
            </div>
          </nav>

          {/* Toggle button at bottom */}
          <div className={`h-12 border-t border-gray-200 ${isCollapsed ? 'px-2' : 'px-3'} flex items-center ${isCollapsed ? 'justify-center' : 'justify-end'}`}>
            <button
              type="button"
              className="hidden md:flex p-1.5 rounded-md text-neutral-gray hover:text-primary-blue hover:bg-secondary-blue-pale focus:outline-none focus:ring-2 focus:ring-primary transition-colors"
              onClick={onToggleCollapse}
              title={isCollapsed ? "Expand sidebar" : "Collapse sidebar"}
            >
              {isCollapsed ? (
                <PanelLeftOpen className="h-4 w-4" />
              ) : (
                <PanelLeftClose className="h-4 w-4" />
              )}
            </button>
          </div>
        </div>
      </div>
    </>
  );
};

export default Sidebar;