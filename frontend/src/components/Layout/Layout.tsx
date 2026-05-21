import React, { useState } from "react";
import { useLocation } from "react-router-dom";
import Sidebar from "./Sidebar";
import TopBar from "./TopBar";
import { MobileNotice } from "../common/MobileNotice";

interface LayoutProps {
  children: React.ReactNode;
  statusBar?: React.ReactNode;
}

const Layout: React.FC<LayoutProps> = ({ children, statusBar }) => {
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false);
  const location = useLocation();

  const hideMobileNotice =
    location.pathname === "/" ||
    location.pathname === "/dashboard" ||
    location.pathname === "/customer/smartsupp";

  // Pages that manage their own internal scroll — no padding wrapper, main is overflow-hidden
  const isFullHeightPage = location.pathname === "/customer/smartsupp";

  return (
    <div className="h-screen bg-gray-50 flex flex-col overflow-hidden">
      {/* TopBar for mobile menu */}
      <TopBar onMenuClick={() => setSidebarOpen(true)} />

      {/* Sidebar */}
      <Sidebar
        isOpen={sidebarOpen}
        isCollapsed={sidebarCollapsed}
        onClose={() => setSidebarOpen(false)}
        onToggleCollapse={() => setSidebarCollapsed(!sidebarCollapsed)}
        onMenuClick={() => setSidebarOpen(true)}
      />

      {/* Main content */}
      <div
        className={`flex-1 flex flex-col transition-all duration-300 ${sidebarCollapsed ? "md:pl-16" : "md:pl-64"} pt-16 md:pt-0`}
      >
        {!hideMobileNotice && <MobileNotice />}

        {/* Page content */}
        <main className={`flex-1 relative ${isFullHeightPage ? "overflow-hidden flex flex-col" : "overflow-auto"}`}>
          {isFullHeightPage ? (
            children
          ) : (
            <div className="p-3 md:p-4 bg-gray-50 min-h-full flex flex-col">
              <div className="w-full flex-1 flex flex-col min-h-0">{children}</div>
            </div>
          )}
        </main>
      </div>

      {/* Status bar with sidebar state */}
      {statusBar &&
        React.cloneElement(statusBar as React.ReactElement, {
          sidebarCollapsed,
        })}
    </div>
  );
};

export default Layout;
