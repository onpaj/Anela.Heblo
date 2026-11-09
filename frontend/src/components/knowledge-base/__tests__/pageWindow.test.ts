import { getPageWindow } from '../pageWindow';

describe('getPageWindow', () => {
  it.each([
    [0, 1, []],
    [1, 1, [1]],
    [3, 2, [1, 2, 3]],
    [5, 5, [1, 2, 3, 4, 5]],
    [10, 1, [1, 2, 3, 4, 5]],
    [10, 3, [1, 2, 3, 4, 5]],
    [10, 4, [2, 3, 4, 5, 6]],
    [10, 7, [5, 6, 7, 8, 9]],
    [10, 8, [6, 7, 8, 9, 10]],
    [10, 10, [6, 7, 8, 9, 10]],
    [6, 3, [1, 2, 3, 4, 5]],
    [6, 4, [2, 3, 4, 5, 6]],
  ])('totalPages=%i currentPage=%i -> %j', (totalPages, currentPage, expected) => {
    expect(getPageWindow(totalPages, currentPage)).toEqual(expected);
  });
});
