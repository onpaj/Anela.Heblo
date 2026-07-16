import {
  init,
  receiveReducer,
  savedCount,
  ReceiveState,
} from '../receiveMachine';

describe('receiveMachine.init', () => {
  test('freeform starts on the material step, blank', () => {
    const state = init({ mode: 'freeform' });
    expect(state).toEqual({
      mode: 'freeform',
      activeStep: 'material',
      material: null,
      materialLocked: false,
      lotCode: null,
      boxes: [],
    });
  });

  test('po starts on the lot step with the material pre-selected and locked', () => {
    const state = init({ mode: 'po', material: { code: 'MAT001', name: 'Olive Oil' } });
    expect(state.activeStep).toBe('lot');
    expect(state.material).toEqual({ code: 'MAT001', name: 'Olive Oil' });
    expect(state.materialLocked).toBe(true);
  });
});

describe('receiveMachine.receiveReducer', () => {
  test('SELECT_MATERIAL (freeform) advances to the lot step and stores the material', () => {
    const state = init({ mode: 'freeform' });
    const next = receiveReducer(state, { type: 'SELECT_MATERIAL', code: 'MAT001', name: 'Olive Oil' });
    expect(next.material).toEqual({ code: 'MAT001', name: 'Olive Oil' });
    expect(next.activeStep).toBe('lot');
    // immutability: the original state object is untouched.
    expect(state.material).toBeNull();
    expect(state.activeStep).toBe('material');
    expect(next).not.toBe(state);
  });

  test('SELECT_MATERIAL is a no-op when the material is locked (po)', () => {
    const state = init({ mode: 'po', material: { code: 'MAT001', name: 'Olive Oil' } });
    const next = receiveReducer(state, { type: 'SELECT_MATERIAL', code: 'MAT999', name: 'Other' });
    expect(next).toBe(state);
  });

  test('CONFIRM_LOT advances to the scan step and stores the lot', () => {
    const state = receiveReducer(init({ mode: 'freeform' }), {
      type: 'SELECT_MATERIAL',
      code: 'MAT001',
      name: 'Olive Oil',
    });
    const next = receiveReducer(state, { type: 'CONFIRM_LOT', lot: 'L1' });
    expect(next.lotCode).toBe('L1');
    expect(next.activeStep).toBe('scan');
  });

  test('CONFIRM_LOT with an empty lot is a no-op', () => {
    const state = init({ mode: 'po', material: { code: 'MAT001', name: 'Olive Oil' } });
    const next = receiveReducer(state, { type: 'CONFIRM_LOT', lot: '' });
    expect(next).toBe(state);
  });

  test('EDIT_MATERIAL (freeform) returns to the material step and clears lot + boxes', () => {
    let state = init({ mode: 'freeform' });
    state = receiveReducer(state, { type: 'SELECT_MATERIAL', code: 'MAT001', name: 'Olive Oil' });
    state = receiveReducer(state, { type: 'CONFIRM_LOT', lot: 'L1' });
    state = receiveReducer(state, { type: 'ADD_SCAN', id: 1, code: 'M00000001' });
    const next = receiveReducer(state, { type: 'EDIT_MATERIAL' });
    expect(next.activeStep).toBe('material');
    expect(next.lotCode).toBeNull();
    expect(next.boxes).toEqual([]);
  });

  test('EDIT_MATERIAL is a no-op when the material is locked (po)', () => {
    const state = receiveReducer(
      init({ mode: 'po', material: { code: 'MAT001', name: 'Olive Oil' } }),
      { type: 'CONFIRM_LOT', lot: 'L1' },
    );
    const next = receiveReducer(state, { type: 'EDIT_MATERIAL' });
    expect(next).toBe(state);
  });

  test('EDIT_LOT returns to the lot step and clears boxes', () => {
    let state = receiveReducer(
      init({ mode: 'po', material: { code: 'MAT001', name: 'Olive Oil' } }),
      { type: 'CONFIRM_LOT', lot: 'L1' },
    );
    state = receiveReducer(state, { type: 'ADD_SCAN', id: 1, code: 'M00000001' });
    const next = receiveReducer(state, { type: 'EDIT_LOT' });
    expect(next.activeStep).toBe('lot');
    expect(next.boxes).toEqual([]);
    expect(next.lotCode).toBe('L1'); // lot value stays as the field default
  });

  test('ADD_SCAN appends a saving box without mutating the previous list', () => {
    const state = receiveReducer(
      init({ mode: 'po', material: { code: 'MAT001', name: 'Olive Oil' } }),
      { type: 'CONFIRM_LOT', lot: 'L1' },
    );
    const next = receiveReducer(state, { type: 'ADD_SCAN', id: 7, code: 'M00000001' });
    expect(next.boxes).toEqual([{ id: 7, code: 'M00000001', status: 'saving' }]);
    expect(state.boxes).toEqual([]);
    expect(next.boxes).not.toBe(state.boxes);
  });

  test('RESOLVE_SCAN updates only the matching box', () => {
    let state = receiveReducer(
      init({ mode: 'po', material: { code: 'MAT001', name: 'Olive Oil' } }),
      { type: 'CONFIRM_LOT', lot: 'L1' },
    );
    state = receiveReducer(state, { type: 'ADD_SCAN', id: 1, code: 'M00000001' });
    state = receiveReducer(state, { type: 'ADD_SCAN', id: 2, code: 'M00000002' });
    const next = receiveReducer(state, { type: 'RESOLVE_SCAN', id: 2, status: 'saved' });
    expect(next.boxes[0].status).toBe('saving');
    expect(next.boxes[1].status).toBe('saved');
  });

  test('REJECT_SCAN appends an error box (client-side rejection)', () => {
    const state = receiveReducer(
      init({ mode: 'po', material: { code: 'MAT001', name: 'Olive Oil' } }),
      { type: 'CONFIRM_LOT', lot: 'L1' },
    );
    const next = receiveReducer(state, { type: 'REJECT_SCAN', id: 3, code: 'BADCODE', message: 'Neplatný formát' });
    expect(next.boxes).toEqual([
      { id: 3, code: 'BADCODE', status: 'error', message: 'Neplatný formát' },
    ]);
  });

  test('RESET returns a fresh freeform state (empty combobox)', () => {
    let state = receiveReducer(init({ mode: 'freeform' }), {
      type: 'SELECT_MATERIAL',
      code: 'MAT001',
      name: 'Olive Oil',
    });
    state = receiveReducer(state, { type: 'CONFIRM_LOT', lot: 'L1' });
    const next = receiveReducer(state, { type: 'RESET' });
    expect(next).toEqual(init({ mode: 'freeform' }));
  });
});

describe('receiveMachine.savedCount', () => {
  test('counts only saved boxes', () => {
    const state: ReceiveState = {
      mode: 'po',
      activeStep: 'scan',
      material: { code: 'MAT001', name: 'Olive Oil' },
      materialLocked: true,
      lotCode: 'L1',
      boxes: [
        { id: 1, code: 'M00000001', status: 'saved' },
        { id: 2, code: 'M00000002', status: 'duplicate' },
        { id: 3, code: 'M00000003', status: 'saved' },
        { id: 4, code: 'M00000004', status: 'saving' },
      ],
    };
    expect(savedCount(state)).toBe(2);
  });
});
