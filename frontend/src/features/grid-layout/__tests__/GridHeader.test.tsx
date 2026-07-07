import { render, fireEvent, screen } from '@testing-library/react';
import { GridHeader } from '../GridHeader';
import { GridColumn, GridColumnState } from '../types';

const columns: GridColumn<{ id: string }>[] = [
  { id: 'stock', header: 'Stock', defaultWidth: 100, minWidth: 60, renderCell: (r) => r.id },
];

const columnState: GridColumnState[] = [{ id: 'stock', order: 0, width: 100, hidden: false }];

function renderHeader(
  onResizeChange: (id: string, w: number) => void,
  onResizeEnd: (id: string, w: number) => void,
) {
  return render(
    <table>
      <GridHeader
        columns={columns}
        columnState={columnState}
        onResizeChange={onResizeChange}
        onResizeEnd={onResizeEnd}
      />
    </table>,
  );
}

it('fires onResizeChange per mousemove and onResizeEnd exactly once on mouseup', () => {
  const onResizeChange = jest.fn();
  const onResizeEnd = jest.fn();
  renderHeader(onResizeChange, onResizeEnd);

  const handle = screen.getByTestId('column-resize-handle-stock');

  fireEvent.mouseDown(handle, { clientX: 200 });
  fireEvent.mouseMove(window, { clientX: 210 });
  fireEvent.mouseMove(window, { clientX: 230 });
  fireEvent.mouseMove(window, { clientX: 260 });

  expect(onResizeChange).toHaveBeenCalledTimes(3);
  expect(onResizeChange).toHaveBeenLastCalledWith('stock', 160); // 100 + (260 - 200)
  expect(onResizeEnd).not.toHaveBeenCalled();

  fireEvent.mouseUp(window, { clientX: 260 });

  expect(onResizeEnd).toHaveBeenCalledTimes(1);
  expect(onResizeEnd).toHaveBeenCalledWith('stock', 160);
  expect(onResizeChange).toHaveBeenCalledTimes(3); // no extra change on mouseup
});

it('clamps the final width to minWidth when dragged below it', () => {
  const onResizeChange = jest.fn();
  const onResizeEnd = jest.fn();
  renderHeader(onResizeChange, onResizeEnd);

  const handle = screen.getByTestId('column-resize-handle-stock');
  fireEvent.mouseDown(handle, { clientX: 200 });
  fireEvent.mouseUp(window, { clientX: 50 }); // dx = -150 → 100 - 150 = -50, clamp to 60

  expect(onResizeEnd).toHaveBeenCalledWith('stock', 60);
});

it('does not throw when both resize callbacks are omitted', () => {
  render(
    <table>
      <GridHeader columns={columns} columnState={columnState} />
    </table>,
  );
  const handle = screen.getByTestId('column-resize-handle-stock');
  expect(() => {
    fireEvent.mouseDown(handle, { clientX: 200 });
    fireEvent.mouseMove(window, { clientX: 230 });
    fireEvent.mouseUp(window, { clientX: 230 });
  }).not.toThrow();
});
