import React from 'react';

interface DefaultTileProps {
  data: any;
}

export const DefaultTile: React.FC<DefaultTileProps> = ({ data }) => {
  return (
    <div className="h-full">
      <pre className="text-xs text-gray-600 overflow-auto">
        {JSON.stringify(data, null, 2)}
      </pre>
    </div>
  );
};
