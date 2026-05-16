using Anela.Heblo.Application.Features.MeetingTasks.Contracts;
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetMeetingUsers;

public class GetMeetingUsersHandler : IRequestHandler<GetMeetingUsersRequest, GetMeetingUsersResponse>
{
    private readonly IMeetingUserDirectory _userDirectory;

    public GetMeetingUsersHandler(IMeetingUserDirectory userDirectory)
    {
        _userDirectory = userDirectory;
    }

    public Task<GetMeetingUsersResponse> Handle(
        GetMeetingUsersRequest request,
        CancellationToken cancellationToken)
    {
        var users = _userDirectory.GetAll()
            .Select(u => new MeetingUserDto
            {
                Email = u.Email,
                DisplayName = u.DisplayName,
                Aliases = u.Aliases.ToList()
            })
            .ToList();

        return Task.FromResult(new GetMeetingUsersResponse { Users = users });
    }
}
