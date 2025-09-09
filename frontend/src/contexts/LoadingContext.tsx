import React, { createContext, useContext, useState, ReactNode } from "react";

interface LoadingContextType {
  isLoading: boolean;
  setIsLoading: (loading: boolean) => void;
  loadingCount: number;
  incrementLoading: () => void;
  decrementLoading: () => void;
}

const LoadingContext = createContext<LoadingContextType | undefined>(undefined);

export const useLoading = () => {
  const context = useContext(LoadingContext);
  if (context === undefined) {
    throw new Error("useLoading must be used within a LoadingProvider");
  }
  return context;
};

interface LoadingProviderProps {
  children: ReactNode;
}

export const LoadingProvider: React.FC<LoadingProviderProps> = ({
  children,
}) => {
  const [loadingCount, setLoadingCount] = useState(0);

  const incrementLoading = () => {
    setLoadingCount((prev) => prev + 1);
  };

  const decrementLoading = () => {
    setLoadingCount((prev) => Math.max(0, prev - 1));
  };

  const setIsLoading = (loading: boolean) => {
    if (loading) {
      incrementLoading();
    } else {
      decrementLoading();
    }
  };

  const isLoading = loadingCount > 0;

  return (
    <LoadingContext.Provider
      value={{
        isLoading,
        setIsLoading,
        loadingCount,
        incrementLoading,
        decrementLoading,
      }}
    >
      {children}
    </LoadingContext.Provider>
  );
};
