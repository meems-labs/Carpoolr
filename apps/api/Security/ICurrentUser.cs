namespace Carpoolr.Api.Security;

public interface ICurrentUser
{
    UserIdentity GetRequiredIdentity();
}
