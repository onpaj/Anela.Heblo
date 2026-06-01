import React from "react";

export interface CardProps {
  className?: string;
  children?: React.ReactNode;
}

export const Card: React.FC<CardProps> = ({ className = "", children }) => {
  return (
    <div className={`bg-white rounded-lg border border-gray-200 shadow-sm ${className}`}>
      {children}
    </div>
  );
};

export interface CardHeaderProps {
  className?: string;
  children?: React.ReactNode;
}

export const CardHeader: React.FC<CardHeaderProps> = ({ className = "", children }) => {
  return (
    <div className={`flex flex-col space-y-1.5 p-6 ${className}`}>
      {children}
    </div>
  );
};

export interface CardTitleProps {
  className?: string;
  children?: React.ReactNode;
}

export const CardTitle: React.FC<CardTitleProps> = ({ className = "", children }) => {
  return (
    <h3 className={`text-lg font-semibold leading-none tracking-tight ${className}`}>
      {children}
    </h3>
  );
};

export interface CardContentProps {
  className?: string;
  children?: React.ReactNode;
}

export const CardContent: React.FC<CardContentProps> = ({ className = "", children }) => {
  return (
    <div className={`p-6 pt-0 ${className}`}>
      {children}
    </div>
  );
};