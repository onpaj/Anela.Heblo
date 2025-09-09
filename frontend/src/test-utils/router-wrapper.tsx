import React from "react";
import { BrowserRouter } from "react-router-dom";

interface TestRouterWrapperProps {
  children: React.ReactNode;
}

/**
 * Test router wrapper with future flags enabled to suppress warnings
 * Used for React Router tests to prevent console warnings about upcoming v7 changes
 */
export const TestRouterWrapper: React.FC<TestRouterWrapperProps> = ({
  children,
}) => {
  return (
    <BrowserRouter
      future={{
        v7_startTransition: true,
        v7_relativeSplatPath: true,
      }}
    >
      {children}
    </BrowserRouter>
  );
};
