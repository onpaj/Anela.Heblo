import React, { useState } from "react";
import { Link } from "react-router-dom";
import {
  LayoutDashboard,
  Package,
  ShoppingCart,
  ChevronDown,
  ChevronRight,
  PanelLeftClose,
  PanelLeftOpen,
  Menu,
  DollarSign,
  Cog,
  Truck,
  Bot,
} from "lucide-react";
import UserProfile from "../auth/UserProfile";
import { useAuth } from "../../auth/useAuth";
import { useMockAuth, shouldUseMockAuth } from "../../auth/mockAuth";

interface SidebarProps {
  isOpen: boolean;
  isCollapsed: boolean;
  onClose: () => void;
  onToggleCollapse: () => void;
  onMenuClick: () => void;
}

const Sidebar: React.FC<SidebarProps> = ({
  isOpen,
  isCollapsed,
  onClose,
  onToggleCollapse,
  onMenuClick,
}) => {
  const [activeItem, setActiveItem] = useState("dashboard");
  const [expandedSections, setExpandedSections] = useState<string[]>([]);

  // Use mock auth if enabled, otherwise use real auth
  const realAuth = useAuth();
  const mockAuth = useMockAuth();
  const auth = shouldUseMockAuth() ? mockAuth : realAuth;
  const { getUserInfo } = auth;
  const userInfo = getUserInfo();

  // Helper function to check if user has a specific role
  const hasRole = (role: string): boolean => {
    return userInfo?.roles?.includes(role) || false;
  };

  // Function to open Hangfire dashboard in new window
  const openHangfireDashboard = () => {
    const baseUrl = window.location.origin
      .replace(":3000", ":5000")
      .replace(":3001", ":5001");
    window.open(`${baseUrl}/hangfire`, "_blank", "noopener,noreferrer");
  };

  // Navigation sections - only implemented pages
  const navigationSections = [
    {
      id: "dashboard",
      name: "Dashboard",
      href: "/",
      icon: LayoutDashboard,
      type: "single" as const,
    },
    // Finance section - only visible for finance_reader role
    ...(hasRole("finance_reader")
      ? [
          {
            id: "finance",
            name: "Finance",
            icon: DollarSign,
            type: "section" as const,
            items: [
              {
                id: "financni-prehled",
                name: "Finanční přehled",
                href: "/finance/overview",
              },
              {
                id: "analyza-marzovosti",
                name: "Analýza marže",
                href: "/analytics/product-margin-summary",
              },
            ],
          },
        ]
      : []),
    {
      id: "produkty",
      name: "Produkty",
      icon: Package,
      type: "section" as const,
      items: [
        { id: "catalog", name: "Katalog", href: "/catalog" },
        { id: "marze-produktu", name: "Marže", href: "/products/margins" },
        { id: "journal", name: "Deník", href: "/journal" },
      ],
    },
    {
      id: "nakup",
      name: "Nákup",
      icon: ShoppingCart,
      type: "section" as const,
      items: [
        {
          id: "nakupni-objednavky",
          name: "Nákupní objednávky",
          href: "/purchase/orders",
        },
        {
          id: "analyza-skladu",
          name: "Analýza skladů",
          href: "/purchase/stock-analysis",
        },
      ],
    },
    {
      id: "vyroba",
      name: "Výroba",
      icon: Cog,
      type: "section" as const,
      items: [
        {
          id: "rizeni-zasob-vyroba",
          name: "Řízení zásob",
          href: "/manufacturing/stock-analysis",
        },
        {
          id: "prehled-vyroby",
          name: "Přehled výroby",
          href: "/manufacturing/output",
        },
        {
          id: "kalkulator-davek",
          name: "Kalkulačka dávek",
          href: "/manufacturing/batch-calculator",
        },
      ],
    },
    {
      id: "logistika",
      name: "Logistika",
      icon: Truck,
      type: "section" as const,
      items: [
        {
          id: "transportni-boxy",
          name: "Transportní boxy",
          href: "/logistics/transport-boxes",
        },
        {
          id: "vypackovani-balicku",
          name: "Výroba dárkových balíčků",
          href: "/logistics/gift-package-manufacturing",
        },
      ],
    },
    {
      id: "automatizace",
      name: "Automatizace",
      icon: Bot,
      type: "section" as const,
      items: [
        {
          id: "hangfire",
          name: "Hangfire",
          href: "#",
          onClick: openHangfireDashboard,
        },
      ],
    },
  ];

  const toggleSection = (sectionId: string) => {
    setExpandedSections(
      (prev) =>
        prev.includes(sectionId)
          ? [] // Close the currently open section
          : [sectionId], // Open only the clicked section, close all others
    );
  };

  return (
    <>
      {/* Mobile overlay */}
      {isOpen && (
        <div className="fixed inset-0 flex z-40 md:hidden" onClick={onClose}>
          <div className="fixed inset-0 bg-gray-600 bg-opacity-75" />
        </div>
      )}

      {/* Sidebar */}
      <div
        className={`
        fixed top-0 left-0 z-40 bottom-0 bg-white border-r border-gray-200 shadow-sm transform transition-all duration-300 ease-in-out
        ${isOpen ? "translate-x-0" : "-translate-x-full"} md:translate-x-0
        ${isCollapsed ? "w-16" : "w-64"}
      `}
      >
        <div className="flex flex-col h-full">
          {/* App Title / Mobile Menu */}
          <div
            className={`${isCollapsed ? "h-16 px-2" : "h-16 px-4"} flex items-center border-b border-gray-200`}
          >
            {!isCollapsed ? (
              <div className="flex items-center justify-between w-full">
                {/* Mobile menu button - only visible on mobile */}
                <button
                  type="button"
                  className="md:hidden p-2 rounded-md text-neutral-gray hover:text-primary-blue hover:bg-secondary-blue-pale focus:outline-none focus:ring-2 focus:ring-primary"
                  onClick={onMenuClick}
                >
                  <Menu className="h-5 w-5" />
                </button>

                {/* App Title */}
                <div className="flex items-center md:justify-start justify-center flex-1">
                  <div className="w-8 h-8 bg-primary-blue rounded flex items-center justify-center">
                    <span className="text-white font-bold text-sm">AH</span>
                  </div>
                  <span className="ml-3 text-lg font-semibold text-gray-900">
                    Anela Heblo
                  </span>
                </div>
              </div>
            ) : (
              <div className="flex items-center justify-center w-full">
                <div className="w-8 h-8 bg-primary-blue rounded flex items-center justify-center">
                  <span className="text-white font-bold text-sm">A</span>
                </div>
              </div>
            )}
          </div>

          {/* Navigation */}
          <nav className={`flex-1 py-4 ${isCollapsed ? "px-2" : "px-3"}`}>
            <div className="space-y-1">
              {navigationSections.map((section) => {
                const IconComponent = section.icon;
                const isExpanded = expandedSections.includes(section.id);
                const isActive = activeItem === section.id;

                if (section.type === "single") {
                  // Single item (Dashboard)
                  return (
                    <div key={section.id}>
                      {!isCollapsed ? (
                        <Link
                          to={section.href!}
                          onClick={() => setActiveItem(section.id)}
                          className={`
                            flex items-center px-3 py-2 text-sm font-medium rounded-md transition-colors duration-300
                            ${
                              isActive
                                ? "bg-secondary-blue-pale text-primary-blue border-r-2 border-primary-blue"
                                : "text-neutral-slate hover:bg-secondary-blue-pale/50 hover:text-neutral-slate"
                            }
                          `}
                        >
                          <IconComponent
                            className={`mr-3 h-5 w-5 ${isActive ? "text-primary-blue" : "text-neutral-gray"}`}
                          />
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
                            ${
                              isActive
                                ? "bg-secondary-blue-pale text-primary-blue"
                                : "text-neutral-slate hover:bg-secondary-blue-pale/50 hover:text-neutral-slate"
                            }
                          `}
                          title={section.name}
                        >
                          <IconComponent
                            className={`h-5 w-5 ${isActive ? "text-primary-blue" : "text-neutral-gray"}`}
                          />
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
                            ${
                              isActive
                                ? "bg-secondary-blue-pale text-primary-blue"
                                : "text-neutral-slate hover:bg-secondary-blue-pale/50 hover:text-neutral-slate"
                            }
                          `}
                        >
                          <div className="flex items-center">
                            <IconComponent
                              className={`mr-3 h-5 w-5 ${isActive ? "text-primary-blue" : "text-neutral-gray"}`}
                            />
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

                              // If subItem has onClick, render as button
                              if ((subItem as any).onClick) {
                                return (
                                  <button
                                    key={subItem.id}
                                    onClick={() => {
                                      setActiveItem(subItem.id);
                                      (subItem as any).onClick();
                                    }}
                                    className={`
                                      block w-full text-left px-3 py-2 text-sm rounded-md transition-colors duration-300
                                      ${
                                        isSubActive
                                          ? "bg-secondary-blue-pale text-primary-blue font-medium"
                                          : "text-neutral-gray hover:bg-secondary-blue-pale/30 hover:text-neutral-slate"
                                      }
                                    `}
                                  >
                                    {subItem.name}
                                  </button>
                                );
                              }

                              // Regular Link for navigation items
                              return (
                                <Link
                                  key={subItem.id}
                                  to={subItem.href}
                                  onClick={() => setActiveItem(subItem.id)}
                                  className={`
                                    block px-3 py-2 text-sm rounded-md transition-colors duration-300
                                    ${
                                      isSubActive
                                        ? "bg-secondary-blue-pale text-primary-blue font-medium"
                                        : "text-neutral-gray hover:bg-secondary-blue-pale/30 hover:text-neutral-slate"
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
                          ${
                            isActive
                              ? "bg-secondary-blue-pale text-primary-blue"
                              : "text-neutral-slate hover:bg-secondary-blue-pale/50 hover:text-neutral-slate"
                          }
                        `}
                        title={section.name}
                      >
                        <IconComponent
                          className={`h-5 w-5 ${isActive ? "text-primary-blue" : "text-neutral-gray"}`}
                        />
                      </button>
                    )}
                  </div>
                );
              })}
            </div>
          </nav>

          {/* User Profile and Toggle button at bottom */}
          <div
            className={`border-t border-gray-200 ${isCollapsed ? "px-2" : "px-3"} flex flex-col`}
          >
            {!isCollapsed ? (
              <div className="flex items-center justify-between h-16 py-2">
                <div className="flex-1 min-w-0">
                  <UserProfile />
                </div>
                <button
                  type="button"
                  className="hidden md:flex p-1.5 ml-2 rounded-md text-neutral-gray hover:text-primary-blue hover:bg-secondary-blue-pale focus:outline-none focus:ring-2 focus:ring-primary transition-colors flex-shrink-0"
                  onClick={onToggleCollapse}
                  title="Collapse sidebar"
                >
                  <PanelLeftClose className="h-4 w-4" />
                </button>
              </div>
            ) : (
              <div className="flex flex-col items-center py-2 space-y-2">
                <div className="w-full flex justify-center">
                  <UserProfile compact />
                </div>
                <button
                  type="button"
                  className="hidden md:flex p-1.5 rounded-md text-neutral-gray hover:text-primary-blue hover:bg-secondary-blue-pale focus:outline-none focus:ring-2 focus:ring-primary transition-colors"
                  onClick={onToggleCollapse}
                  title="Expand sidebar"
                >
                  <PanelLeftOpen className="h-4 w-4" />
                </button>
              </div>
            )}
          </div>
        </div>
      </div>
    </>
  );
};

export default Sidebar;
