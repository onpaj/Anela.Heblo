import React from 'react';
import { TransportBoxStateBadgeProps, stateLabels, stateColors } from '../TransportBoxTypes';

const TransportBoxStateBadge: React.FC<TransportBoxStateBadgeProps> = ({ state, size = 'md' }) => {
  const sizeClasses = {
    sm: 'px-2 py-0.5 text-xs',
    md: 'px-2.5 py-0.5 text-xs',
    lg: 'px-3 py-1 text-sm',
  };

  return (
    <span className={`inline-flex items-center rounded-full font-medium ${
      stateColors[state] || 'bg-gray-100 text-gray-800'
    } ${sizeClasses[size]}`}>
      {stateLabels[state] || state}
    </span>
  );
};

export default TransportBoxStateBadge;