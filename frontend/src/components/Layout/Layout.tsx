import React, { useState } from 'react';
import Sidebar from './Sidebar';
import TopBar from './TopBar';

interface LayoutProps {
  children: React.ReactNode;
  statusBar?: React.ReactNode;
}

const Layout: React.FC<LayoutProps> = ({ children, statusBar }) => {
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false);

  return (
    <div className="h-screen bg-gray-50 flex flex-col">
      {/* Sidebar */}
      <Sidebar 
        isOpen={sidebarOpen} 
        isCollapsed={sidebarCollapsed}
        onClose={() => setSidebarOpen(false)}
        onToggleCollapse={() => setSidebarCollapsed(!sidebarCollapsed)}
      />
      
      {/* Top bar */}
      <TopBar 
        onMenuClick={() => setSidebarOpen(true)}
      />
      
      {/* Main content */}
      <div className={`flex-1 flex flex-col transition-all duration-300 pt-16 ${sidebarCollapsed ? 'md:pl-16' : 'md:pl-64'} min-h-0`}>
        {/* Page content */}
        <main className="flex-1 relative overflow-hidden">
          <div className="h-full p-6 bg-gray-50 flex flex-col">
            <div className="flex-1 bg-white rounded-lg shadow-sm border border-gray-200 p-6 max-w-7xl mx-auto w-full flex flex-col min-h-0">
              {children}
            </div>
          </div>
        </main>
      </div>
      
      {/* Status bar with sidebar state */}
      {statusBar && React.cloneElement(statusBar as React.ReactElement, { sidebarCollapsed })}
    </div>
  );
};

export default Layout;