import React, { useState } from 'react';
import Sidebar from './Sidebar';
import TopBar from './TopBar';

interface LayoutProps {
  children: React.ReactNode;
}

const Layout: React.FC<LayoutProps> = ({ children }) => {
  const [sidebarOpen, setSidebarOpen] = useState(false);

  return (
    <div className="min-h-screen bg-base">
      {/* Sidebar */}
      <Sidebar isOpen={sidebarOpen} onClose={() => setSidebarOpen(false)} />
      
      {/* Main content */}
      <div className="md:pl-64">
        {/* Top bar */}
        <TopBar onMenuClick={() => setSidebarOpen(true)} />
        
        {/* Page content */}
        <main className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
          {children}
        </main>
      </div>
    </div>
  );
};

export default Layout;