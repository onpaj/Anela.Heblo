import {
  calculateLevels,
  getAllParentPositionIds,
  buildTree,
  getChildren,
  OrganizationData,
  Position,
} from '../orgChartUtils';
import { PositionDto } from '../../api/generated/api-client';

// Helper to build a Position quickly without exercising NSwag init/fromJS.
const makePosition = (overrides: Partial<PositionDto>): Position => {
  const p = new PositionDto();
  Object.assign(p, overrides);
  return p;
};

const buildOrganizationData = (positions: Position[]): OrganizationData => ({
  organization: {
    name: 'Test Org',
    positions,
  },
});

describe('calculateLevels', () => {
  it('assigns level 1 to a single root position', () => {
    // Arrange
    const positions = [makePosition({ id: 'root' })];
    const data = buildOrganizationData(positions);

    // Act
    const result = calculateLevels(data);

    // Assert
    expect(result.organization.positions[0].level).toBe(1);
  });

  it('computes correct levels for a three-level hierarchy', () => {
    // Arrange
    const positions = [
      makePosition({ id: 'a' }),
      makePosition({ id: 'b', parentPositionId: 'a' }),
      makePosition({ id: 'c', parentPositionId: 'b' }),
    ];
    const data = buildOrganizationData(positions);

    // Act
    const result = calculateLevels(data);
    const levelById = new Map(
      result.organization.positions.map((p) => [p.id, p.level]),
    );

    // Assert
    expect(levelById.get('a')).toBe(1);
    expect(levelById.get('b')).toBe(2);
    expect(levelById.get('c')).toBe(3);
  });

  it('returns a new OrganizationData object (does not mutate the input)', () => {
    // Arrange
    const positions = [makePosition({ id: 'root' })];
    const data = buildOrganizationData(positions);

    // Act
    const result = calculateLevels(data);

    // Assert
    expect(result).not.toBe(data);
    expect(result.organization).not.toBe(data.organization);
    expect(result.organization.positions).not.toBe(data.organization.positions);
  });

  it('breaks cycles and returns level 1 for cyclic positions', () => {
    // Arrange — a <-> b cycle.
    const positions = [
      makePosition({ id: 'a', parentPositionId: 'b' }),
      makePosition({ id: 'b', parentPositionId: 'a' }),
    ];
    const data = buildOrganizationData(positions);
    const consoleError = jest.spyOn(console, 'error').mockImplementation(() => {});

    // Act
    const result = calculateLevels(data);

    // Assert — cycle guard prevents infinite recursion. Both positions get level 1.
    result.organization.positions.forEach((p) => {
      expect(p.level).toBe(1);
    });

    consoleError.mockRestore();
  });
});

describe('getAllParentPositionIds', () => {
  it('returns an empty set for a root position', () => {
    // Arrange
    const positions = [makePosition({ id: 'root' })];

    // Act
    const result = getAllParentPositionIds('root', positions);

    // Assert
    expect(result.size).toBe(0);
  });

  it('returns every ancestor id for a deeply-nested position', () => {
    // Arrange
    const positions = [
      makePosition({ id: 'a' }),
      makePosition({ id: 'b', parentPositionId: 'a' }),
      makePosition({ id: 'c', parentPositionId: 'b' }),
      makePosition({ id: 'd', parentPositionId: 'c' }),
    ];

    // Act
    const result = getAllParentPositionIds('d', positions);

    // Assert
    expect(Array.from(result).sort()).toEqual(['a', 'b', 'c']);
  });

  it('returns an empty set when the position id is unknown', () => {
    // Arrange
    const positions = [makePosition({ id: 'a' })];

    // Act
    const result = getAllParentPositionIds('missing', positions);

    // Assert
    expect(result.size).toBe(0);
  });

  it('handles cycles gracefully without infinite recursion', () => {
    // Arrange
    const positions = [
      makePosition({ id: 'a', parentPositionId: 'b' }),
      makePosition({ id: 'b', parentPositionId: 'a' }),
    ];

    // Act — must terminate without stack overflow
    const result = getAllParentPositionIds('a', positions);

    // Assert
    expect(result.has('b')).toBe(true);
  });
});

describe('buildTree', () => {
  it('returns an empty array when given no positions', () => {
    // Act
    const roots = buildTree([]);

    // Assert
    expect(roots).toEqual([]);
  });

  it('identifies positions with no parent as roots', () => {
    // Arrange
    const positions = [
      makePosition({ id: 'a' }),
      makePosition({ id: 'b', parentPositionId: 'a' }),
      makePosition({ id: 'c' }),
    ];

    // Act
    const roots = buildTree(positions);

    // Assert
    expect(roots.map((r) => r.id).sort()).toEqual(['a', 'c']);
  });

  it('treats a position whose parent is missing from the list as a root', () => {
    // Arrange — 'orphan' references a parent that isn't in the positions list.
    const positions = [
      makePosition({ id: 'orphan', parentPositionId: 'missing-parent' }),
    ];

    // Act
    const roots = buildTree(positions);

    // Assert
    expect(roots.map((r) => r.id)).toEqual(['orphan']);
  });
});

describe('getChildren', () => {
  it('returns positions whose parentPositionId matches the given id', () => {
    // Arrange
    const positions = [
      makePosition({ id: 'a' }),
      makePosition({ id: 'b', parentPositionId: 'a' }),
      makePosition({ id: 'c', parentPositionId: 'a' }),
      makePosition({ id: 'd', parentPositionId: 'b' }),
    ];

    // Act
    const children = getChildren('a', positions);

    // Assert
    expect(children.map((c) => c.id).sort()).toEqual(['b', 'c']);
  });

  it('returns an empty array when the position has no children', () => {
    // Arrange
    const positions = [
      makePosition({ id: 'a' }),
      makePosition({ id: 'b', parentPositionId: 'a' }),
    ];

    // Act
    const children = getChildren('b', positions);

    // Assert
    expect(children).toEqual([]);
  });
});
