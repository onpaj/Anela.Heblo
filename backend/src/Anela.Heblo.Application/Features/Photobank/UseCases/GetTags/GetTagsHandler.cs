using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Domain.Features.Photobank;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.GetTags
{
    public class GetTagsHandler : IRequestHandler<GetTagsRequest, GetTagsResponse>
    {
        private readonly IPhotobankRepository _repository;
        private readonly IPhotobankTagsCache _cache;
        private readonly ILogger<GetTagsHandler> _logger;

        public GetTagsHandler(
            IPhotobankRepository repository,
            IPhotobankTagsCache cache,
            ILogger<GetTagsHandler> logger)
        {
            _repository = repository;
            _cache = cache;
            _logger = logger;
        }

        public async Task<GetTagsResponse> Handle(GetTagsRequest request, CancellationToken cancellationToken)
        {
            if (_cache.TryGet(out var cached))
            {
                _logger.LogDebug("Photobank tags cache hit ({TagCount} tags)", cached.Count);
                return new GetTagsResponse { Tags = cached.ToList() };
            }

            var stopwatch = Stopwatch.StartNew();
            var rows = await _repository.GetTagsWithCountsAsync(cancellationToken);
            stopwatch.Stop();

            var dtos = rows
                .Select(r => new TagWithCountDto { Id = r.Id, Name = r.Name, Count = r.Count })
                .ToList();

            _cache.Set(dtos);

            _logger.LogInformation(
                "Fetched {TagCount} photobank tags in {ElapsedMs} ms",
                dtos.Count,
                stopwatch.ElapsedMilliseconds);

            return new GetTagsResponse { Tags = dtos };
        }
    }
}
