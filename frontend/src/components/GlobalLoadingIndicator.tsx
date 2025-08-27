import React from 'react';
import { useIsFetching, useIsMutating } from '@tanstack/react-query';
import { LoadingIndicator } from './ui/LoadingIndicator';

export const GlobalLoadingIndicator: React.FC = () => {
  const isFetching = useIsFetching();
  const isMutating = useIsMutating();
  
  const isLoading = isFetching > 0 || isMutating > 0;
  
  return <LoadingIndicator isVisible={isLoading} />;
};