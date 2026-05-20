import { PositionDto } from '../api/generated/api-client';

export type Position = PositionDto;

export interface OrganizationData {
  organization: {
    name: string;
    positions: Position[];
  };
}

export function calculateLevels(data: OrganizationData): OrganizationData {
  const positionMap = new Map(data.organization.positions.map((p) => [p.id!, p]));

  const getLevel = (positionId: string, visited: Set<string>): number => {
    const position = positionMap.get(positionId);
    if (!position) return 1;
    if (!position.parentPositionId) return 1;

    if (visited.has(positionId)) {
      // eslint-disable-next-line no-console
      console.error(`Circular dependency detected for position ${positionId}`);
      return 1;
    }

    visited.add(positionId);
    const parentLevel = getLevel(position.parentPositionId, visited);

    // If parent level is 1 and we're not a root, check if we caused a cycle
    if (parentLevel === 1 && visited.has(position.parentPositionId)) {
      return 1;
    }

    return parentLevel + 1;
  };

  const updatedPositions = data.organization.positions.map((position) => ({
    ...position,
    level: getLevel(position.id!, new Set<string>()),
  })) as Position[];

  return {
    ...data,
    organization: {
      ...data.organization,
      positions: updatedPositions,
    },
  };
}

export function getAllParentPositionIds(
  positionId: string,
  allPositions: Position[],
): Set<string> {
  const parentIds = new Set<string>();
  const positionMap = new Map(allPositions.map((p) => [p.id!, p]));
  const visited = new Set<string>();

  const findParents = (id: string) => {
    if (visited.has(id)) return;
    visited.add(id);
    const position = positionMap.get(id);
    if (position && position.parentPositionId) {
      parentIds.add(position.parentPositionId);
      findParents(position.parentPositionId);
    }
  };

  findParents(positionId);
  return parentIds;
}

export function buildTree(positions: Position[]): Position[] {
  const positionMap = new Map(positions.map((p) => [p.id!, p]));
  const roots: Position[] = [];

  positions.forEach((position) => {
    if (!position.parentPositionId || !positionMap.has(position.parentPositionId)) {
      roots.push(position);
    }
  });

  return roots;
}

export function getChildren(parentId: string, positions: Position[]): Position[] {
  return positions.filter((p) => p.parentPositionId === parentId);
}
