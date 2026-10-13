using System.Net.Http;
using Baccarat.Core.Authentication;

namespace Baccarat.Infrastructure.Authentication;

public interface IAuthenticationHttpProtocol
{
    HttpRequestMessage CreateSignInRequest(SignInRequest request);

    ValueTask<AuthenticationResult> ReadSignInResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default);

    HttpRequestMessage CreateSignUpRequest(SignUpRequest request);

    ValueTask<AuthenticationResult> ReadSignUpResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default);

    HttpRequestMessage CreateSignOutRequest(AuthenticatedSession session);

    ValueTask ReadSignOutResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default);
}
