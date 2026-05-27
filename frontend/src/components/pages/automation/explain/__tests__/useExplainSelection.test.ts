import { renderHook, act } from '@testing-library/react';
import { useExplainSelection } from '../useExplainSelection';

function fireMouseup(target: EventTarget) {
  const event = new MouseEvent('mouseup', { bubbles: true });
  Object.defineProperty(event, 'target', { value: target, writable: false });
  document.dispatchEvent(event);
}

describe('useExplainSelection', () => {
  afterEach(() => {
    window.getSelection()?.removeAllRanges();
  });

  it('returns empty selectedText initially', () => {
    const { result } = renderHook(() => useExplainSelection());
    expect(result.current.selectedText).toBe('');
  });

  it('ignores mouseup outside data-explainable element', () => {
    const outside = document.createElement('div');
    document.body.appendChild(outside);
    const { result } = renderHook(() => useExplainSelection());

    const range = document.createRange();
    range.selectNodeContents(outside);
    window.getSelection()?.removeAllRanges();
    window.getSelection()?.addRange(range);

    act(() => { fireMouseup(outside); });

    expect(result.current.selectedText).toBe('');
    document.body.removeChild(outside);
  });

  it('captures selection inside data-explainable element', () => {
    const container = document.createElement('div');
    container.setAttribute('data-explainable', 'true');
    const textNode = document.createTextNode('hello world');
    container.appendChild(textNode);
    document.body.appendChild(container);

    const { result } = renderHook(() => useExplainSelection());

    const range = document.createRange();
    range.setStart(textNode, 0);
    range.setEnd(textNode, 5);
    window.getSelection()?.removeAllRanges();
    window.getSelection()?.addRange(range);

    act(() => { fireMouseup(container); });

    expect(result.current.selectedText).toBe('hello');
    document.body.removeChild(container);
  });

  it('reads selection from textarea selectionStart/End when target is textarea', () => {
    const textarea = document.createElement('textarea');
    textarea.setAttribute('data-explainable', 'true');
    textarea.value = 'abcdef';
    document.body.appendChild(textarea);
    textarea.setSelectionRange(2, 5);

    const { result } = renderHook(() => useExplainSelection());

    act(() => { fireMouseup(textarea); });

    expect(result.current.selectedText).toBe('cde');
    document.body.removeChild(textarea);
  });

  it('clears selection when mouseup yields empty string', () => {
    const container = document.createElement('div');
    container.setAttribute('data-explainable', 'true');
    document.body.appendChild(container);
    const { result } = renderHook(() => useExplainSelection());

    act(() => {
      window.getSelection()?.removeAllRanges();
      fireMouseup(container);
    });

    expect(result.current.selectedText).toBe('');
    document.body.removeChild(container);
  });
});
