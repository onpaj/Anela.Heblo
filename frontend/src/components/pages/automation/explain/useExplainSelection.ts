import { useState, useEffect } from 'react';

export interface SelectionState {
  selectedText: string;
  anchorRect: DOMRect | null;
}

function isInsideExplainable(target: EventTarget | null): boolean {
  if (!(target instanceof Element)) return false;
  return target.closest('[data-explainable]') !== null;
}

export function useExplainSelection(): SelectionState {
  const [state, setState] = useState<SelectionState>({ selectedText: '', anchorRect: null });

  useEffect(() => {
    function handleMouseup(e: MouseEvent) {
      if (!isInsideExplainable(e.target)) {
        setState({ selectedText: '', anchorRect: null });
        return;
      }

      const target = e.target as HTMLElement;

      if (target instanceof HTMLTextAreaElement || target instanceof HTMLInputElement) {
        const { selectionStart, selectionEnd, value } = target;
        const text = (selectionStart !== null && selectionEnd !== null)
          ? value.substring(selectionStart, selectionEnd)
          : '';
        const rect = text ? target.getBoundingClientRect() : null;
        setState({ selectedText: text, anchorRect: rect });
        return;
      }

      const selection = window.getSelection();
      const text = selection?.toString() ?? '';
      let rect: DOMRect | null = null;
      if (text && selection?.rangeCount) {
        try {
          rect = selection.getRangeAt(0).getBoundingClientRect();
        } catch {
          rect = null;
        }
      }
      setState({ selectedText: text, anchorRect: rect });
    }

    document.addEventListener('mouseup', handleMouseup);
    return () => document.removeEventListener('mouseup', handleMouseup);
  }, []);

  return state;
}
