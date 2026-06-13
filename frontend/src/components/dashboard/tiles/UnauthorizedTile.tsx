import React from 'react';
import { Lock } from 'lucide-react';

export const UnauthorizedTile: React.FC = () => {
  return (
    <div
      className="flex flex-col items-center justify-center h-full text-gray-400"
      data-testid="unauthorized-tile"
    >
      <Lock className="h-8 w-8 mb-2" />
      <span className="text-sm">Přístup zakázán</span>
    </div>
  );
};
