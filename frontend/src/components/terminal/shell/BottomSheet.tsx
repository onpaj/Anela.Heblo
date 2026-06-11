// shell/BottomSheet.tsx
import React, { useContext, useEffect, type ReactNode } from 'react';
import { ScanActionsContext } from './ScanProvider';

interface BottomSheetProps {
  open: boolean;
  onClose: () => void;
  /** when the sheet contains an input the wedge must yield focus */
  hasInput?: boolean;
  children: ReactNode;
  testId?: string;
  ariaLabel?: string;
}

export const BottomSheet: React.FC<BottomSheetProps> = ({
  open, onClose, hasInput = false, children, testId, ariaLabel,
}) => {
  const actions = useContext(ScanActionsContext);

  useEffect(() => {
    if (!actions || !hasInput) return;
    actions.setYieldFocus(open);
    return () => actions.setYieldFocus(false);
  }, [actions, hasInput, open]);

  if (!open) return null;
  return (
    <div className="fixed inset-0 z-40 flex flex-col justify-end" role="dialog" aria-modal="true" aria-label={ariaLabel} data-testid={testId}>
      <div className="absolute inset-0 bg-black/30" onClick={onClose} />
      <div className="relative bg-white rounded-t-2xl max-w-md mx-auto w-full p-4 shadow-hover">
        {children}
      </div>
    </div>
  );
};
