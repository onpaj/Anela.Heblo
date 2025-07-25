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
    <div className="min-h-screen bg-gray-50">
      {/* Sidebar */}
      <Sidebar 
        isOpen={sidebarOpen} 
        isCollapsed={sidebarCollapsed}
        onClose={() => setSidebarOpen(false)}
        onToggleCollapse={() => setSidebarCollapsed(!sidebarCollapsed)}
      />
      
      {/* Main content */}
      <div className={`transition-all duration-300 ${sidebarCollapsed ? 'md:pl-16' : 'md:pl-64'}`}>
        {/* Top bar */}
        <TopBar 
          onMenuClick={() => setSidebarOpen(true)}
        />
        
        {/* Page content */}
        <main className="flex-1 relative overflow-hidden">
          <div className="h-full px-4 sm:px-6 lg:px-8 py-8 bg-gray-50">
            <div className="h-full bg-white rounded-lg shadow-sm border border-gray-200 p-6">
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