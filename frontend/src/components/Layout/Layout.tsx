import React, { useState } from "react";
import Sidebar from "./Sidebar";
import TopBar from "./TopBar";

interface LayoutProps {
  children: React.ReactNode;
  statusBar?: React.ReactNode;
}

const Layout: React.FC<LayoutProps> = ({ children, statusBar }) => {
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false);

  return (
    <div className="h-screen bg-gray-50 flex flex-col">
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
        {/* Page content */}
        <main className="flex-1 relative overflow-auto">
          <div className="p-3 md:p-4 bg-gray-50">
            <div className="w-full">{children}</div>
          </div>
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
