using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Domain.Features.MeetingTasks;

namespace Anela.Heblo.Application.Features.MindMaps.Services;

public interface IMindMapUpdater
{
    /// <summary>
    /// Produces the LLM's proposed next document for the map given one new meeting.
    /// Throws <see cref="MindMapUpdateException"/> when no valid document could be obtained.
    /// </summary>
    Task<MindMapDocument> UpdateAsync(MindMapDocument current, MeetingTranscript meeting, CancellationToken ct = default);
}
