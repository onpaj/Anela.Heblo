import {
  mapAgentRun,
  mapAuditEvent,
  mapChangeEvent,
  mapPaged,
  mapProposalDetail,
  mapProposalSummary,
  mapSettings,
  toApiId,
  toDate,
} from '../mappers';

describe('toDate', () => {
  it('accepts an ISO string (raw JSON) and a Date (generated client) alike', () => {
    // Arrange
    const iso = '2026-10-07T08:00:00Z';

    // Act
    const fromString = toDate(iso);
    const fromDate = toDate(new Date(iso));

    // Assert
    expect(fromString?.toISOString()).toBe('2026-10-07T08:00:00.000Z');
    expect(fromDate?.toISOString()).toBe('2026-10-07T08:00:00.000Z');
  });

  it('returns null for missing or unparseable values instead of an Invalid Date', () => {
    expect(toDate(undefined)).toBeNull();
    expect(toDate(null)).toBeNull();
    expect(toDate('')).toBeNull();
    expect(toDate('not-a-date')).toBeNull();
  });
});

describe('toApiId', () => {
  it('converts the opaque view-model id back to the API id type', () => {
    expect(toApiId('42')).toBe(42);
  });
});

describe('mapProposalSummary', () => {
  it('stringifies the id, keeps diff lines and defaults missing fields', () => {
    // Arrange
    const wire = {
      id: 7,
      currentVersion: 2,
      status: 'Pending',
      createdAt: '2026-10-07T08:00:00Z',
      diffLines: ['Přidat vylučující slovo'],
    };

    // Act
    const summary = mapProposalSummary(wire);

    // Assert
    expect(summary.id).toBe('7');
    expect(summary.currentVersion).toBe(2);
    expect(summary.status).toBe('Pending');
    expect(summary.actionCount).toBe(0);
    expect(summary.createdAt).toBeInstanceOf(Date);
    expect(summary.expiresAt).toBeNull();
    expect(summary.diffLines).toEqual(['Přidat vylučující slovo']);
    expect(summary).not.toHaveProperty('accountName');
  });

  it('defaults diffLines to an empty list', () => {
    expect(mapProposalSummary({ id: 1 }).diffLines).toEqual([]);
  });
});

describe('mapProposalDetail', () => {
  const buildWire = () => ({
    summary: { id: 7, currentVersion: 2, status: 'Pending', diffLines: ['summary line'] },
    versions: [
      { version: 2, actionCount: 1, reasoning: 'Current reasoning', createdBy: 'agent', createdAt: '2026-10-07T08:00:00Z', diffLines: ['v2 line'] },
      { version: 1, actionCount: 2, reasoning: 'Old reasoning', createdBy: 'agent', createdAt: '2026-10-07T07:00:00Z', diffLines: ['v1 line'] },
    ],
    approvals: [{ version: 1, principal: 'ondra@anela.cz', channel: 'Web', decision: 'Approved', at: '2026-10-07T09:00:00Z' }],
    executions: [
      { version: 1, actionIndex: 0, state: 'Failed', error: 'old failure', executedAt: '2026-10-07T07:30:00Z' },
      {
        version: 2, actionIndex: 0, state: 'Succeeded', beforeValue: 'Absent', afterValue: 'Present',
        executedAt: new Date('2026-10-07T10:00:00Z'), validateOnly: true, revertState: 'NotReverted',
      },
    ],
    currentEvaluation: { decision: 'NeedsSecondApprover', reason: 'Více než 20 slov' },
  });

  it('flattens the nested summary and picks reasoning and diffLines from the current version', () => {
    // Act
    const detail = mapProposalDetail(buildWire());

    // Assert
    expect(detail.id).toBe('7');
    expect(detail.reasoning).toBe('Current reasoning');
    expect(detail.diffLines).toEqual(['v2 line']);
  });

  it('falls back to the summary diff lines and empty reasoning when the current version is missing', () => {
    const wire = { ...buildWire(), versions: [] };

    const detail = mapProposalDetail(wire);

    expect(detail.reasoning).toBe('');
    expect(detail.diffLines).toEqual(['summary line']);
  });

  it('filters executions to the current version and maps state to outcome', () => {
    const detail = mapProposalDetail(buildWire());

    expect(detail.results).toHaveLength(1);
    expect(detail.results[0]).toEqual({
      actionIndex: 0,
      outcome: 'Succeeded',
      beforeValue: 'Absent',
      afterValue: 'Present',
      error: null,
      executedAt: expect.any(Date),
      validateOnly: true,
      revertState: 'NotReverted',
      revertError: null,
    });
  });

  it('maps currentEvaluation to limits, approvals and version infos', () => {
    const detail = mapProposalDetail(buildWire());

    expect(detail.limits).toEqual({ outcome: 'NeedsSecondApprover', reason: 'Více než 20 slov' });
    expect(detail.approvals[0].reason).toBeNull();
    expect(detail.approvals[0].at).toBeInstanceOf(Date);
    expect(detail.versions).toEqual([
      { version: 2, createdBy: 'agent', createdAt: expect.any(Date), actionCount: 1 },
      { version: 1, createdBy: 'agent', createdAt: expect.any(Date), actionCount: 2 },
    ]);
  });

  it('returns empty collections and null limits when the DTO omits them', () => {
    const detail = mapProposalDetail({});

    expect(detail.reasoning).toBe('');
    expect(detail.diffLines).toEqual([]);
    expect(detail.limits).toBeNull();
    expect(detail.approvals).toEqual([]);
    expect(detail.results).toEqual([]);
    expect(detail.versions).toEqual([]);
  });
});

describe('mapPaged', () => {
  it('maps items and defaults totalCount to 0', () => {
    const paged = mapPaged({ items: [{ id: 1 }, { id: 2 }] }, mapProposalSummary);

    expect(paged.items.map((p) => p.id)).toEqual(['1', '2']);
    expect(paged.totalCount).toBe(0);
  });

  it('keeps the given totalCount and tolerates a missing list', () => {
    expect(mapPaged({ totalCount: 5 }, mapProposalSummary)).toEqual({ items: [], totalCount: 5 });
  });
});

describe('mapAuditEvent', () => {
  it('stringifies ids and keeps a missing proposal id as null', () => {
    const event = mapAuditEvent({ id: 3, proposalId: 7, version: 2, eventType: 'Approved', payloadJson: '{}' });
    const orphan = mapAuditEvent({ id: 4 });

    expect(event.id).toBe('3');
    expect(event.proposalId).toBe('7');
    expect(orphan.proposalId).toBeNull();
    expect(orphan.version).toBeNull();
  });
});

describe('mapChangeEvent', () => {
  it('reads entityExternalId and the *Json values as raw text', () => {
    const event = mapChangeEvent({
      id: 9, entityExternalId: '111', oldValueJson: '{"a":1}', newValueJson: '<b>x</b>', origin: 'OutOfBand', actorKind: 'User',
    });

    expect(event.entityExternalId).toBe('111');
    expect(event.oldValue).toBe('{"a":1}');
    expect(event.newValue).toBe('<b>x</b>');
    expect(event.origin).toBe('OutOfBand');
  });
});

describe('mapAgentRun', () => {
  it('maps costCzk and sums input and output tokens', () => {
    const run = mapAgentRun({ id: 'g1', agentName: 'paid-search', costCzk: 4.2, inputTokens: 100000, outputTokens: 20000 });

    expect(run.approxCostCzk).toBe(4.2);
    expect(run.approxTokens).toBe(120000);
  });

  it('counts a single present token value alone and returns null when both are missing', () => {
    expect(mapAgentRun({ inputTokens: 500 }).approxTokens).toBe(500);
    expect(mapAgentRun({ outputTokens: 70 }).approxTokens).toBe(70);
    expect(mapAgentRun({}).approxTokens).toBeNull();
    expect(mapAgentRun({}).approxCostCzk).toBeNull();
  });
});

describe('mapSettings', () => {
  it('treats a missing global flag as execution disabled (fails closed)', () => {
    const settings = mapSettings({});

    expect(settings.killSwitch).toEqual({ executionEnabled: false, platformOverrides: [] });
    expect(settings.autonomy).toEqual([]);
    expect(settings.limits).toEqual([]);
  });

  it('derives the kill switch from isExecutionEnabledGlobally and the non-Global scopes', () => {
    const settings = mapSettings({
      isExecutionEnabledGlobally: true,
      killSwitches: [
        { scope: 'Global', executionEnabled: true },
        { scope: 'Sklik', executionEnabled: false },
      ],
    });

    expect(settings.killSwitch).toEqual({
      executionEnabled: true,
      platformOverrides: [{ platform: 'Sklik', executionEnabled: false }],
    });
  });

  it('joins approval rates into autonomy rows by platform and action type', () => {
    const settings = mapSettings({
      autonomy: [
        { platform: 'GoogleAds', actionType: 'PauseAd', mode: 'ProposeOnly' },
        { platform: 'GoogleAds', actionType: 'AddNegativeKeyword', mode: 'Auto' },
        { platform: 'Sklik', actionType: 'PauseAd', mode: 'ProposeOnly' },
      ],
      approvalRates: [
        { platform: 'GoogleAds', actionType: 'PauseAd', decided: 4, approvedWithoutEdit: 2, rate: 0.5 },
        { platform: 'GoogleAds', actionType: 'AddNegativeKeyword', decided: 3, approvedWithoutEdit: 0 },
      ],
    });

    expect(settings.autonomy[0]).toEqual(expect.objectContaining({ approvedWithoutEditRate: 0.5, decidedCount: 4 }));
    expect(settings.autonomy[1]).toEqual(expect.objectContaining({ approvedWithoutEditRate: null, decidedCount: 3 }));
    expect(settings.autonomy[2]).toEqual(expect.objectContaining({ approvedWithoutEditRate: null, decidedCount: 0 }));
  });

  it('maps limits with always-present platform and action type', () => {
    const settings = mapSettings({
      limits: [{ platform: 'GoogleAds', actionType: 'PauseAd', key: 'MaxActionsPerProposal', value: 10 }],
    });

    expect(settings.limits).toEqual([{ platform: 'GoogleAds', actionType: 'PauseAd', key: 'MaxActionsPerProposal', value: 10 }]);
  });
});
