using System.Security.Claims;
using Htn.Infrastructure.Identity;

namespace Htn.Api.Common;

public interface ICurrentUser
{
    Guid? Id { get; }
    Guid? PersonId { get; }
}

public sealed class CurrentUser(IHttpContextAccessor http) : ICurrentUser
{
    public Guid? Id => Parse(ClaimTypes.NameIdentifier);
    public Guid? PersonId => Parse(HtnClaimTypes.PersonId);

    private Guid? Parse(string type) =>
        Guid.TryParse(http.HttpContext?.User.FindFirstValue(type), out var id) ? id : null;
}
