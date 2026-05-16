import React from 'react';

interface ExplainTooltipProps {
  anchorRect: DOMRect;
  onExplain: () => void;
}

export function ExplainTooltip({ anchorRect, onExplain }: ExplainTooltipProps) {
  const style: React.CSSProperties = {
    position: 'fixed',
    top: anchorRect.bottom + 4,
    left: anchorRect.left,
    zIndex: 55,
  };

  return (
    <div style={style}>
      <button
        type="button"
        onMouseDown={(e) => {
          e.preventDefault();
          onExplain();
        }}
        className="px-2 py-1 rounded-md text-xs font-medium bg-indigo-600 text-white shadow hover:bg-indigo-700"
      >
        Vysvětlit
      </button>
    </div>
  );
}
