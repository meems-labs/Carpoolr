using Carpoolr.Api.Features.Trips;
using global::Grpc.Core;
using global::Grpc.Core.Interceptors;

namespace Carpoolr.Api.Grpc;

/// <summary>This application exposes unary RPCs. Add matching overrides before adding streams.</summary>
public sealed class GrpcExceptionInterceptor(ILogger<GrpcExceptionInterceptor> logger) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context);
        }
        catch (RpcException)
        {
            throw;
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            var status = context.Deadline <= DateTime.UtcNow
                ? StatusCode.DeadlineExceeded
                : StatusCode.Cancelled;
            throw new RpcException(new Status(status, "The call was cancelled or its deadline elapsed."));
        }
        catch (TripValidationException exception)
        {
            throw Failure(StatusCode.InvalidArgument, exception.Message);
        }
        catch (TripNotFoundException exception)
        {
            throw Failure(StatusCode.NotFound, exception.Message);
        }
        catch (TripPermissionException exception)
        {
            throw Failure(StatusCode.PermissionDenied, exception.Message);
        }
        catch (TripConflictException exception)
        {
            throw Failure(StatusCode.AlreadyExists, exception.Message);
        }
        catch (TripPreconditionException exception)
        {
            throw Failure(StatusCode.FailedPrecondition, exception.Message);
        }
        catch (UnauthorizedAccessException)
        {
            throw Failure(StatusCode.Unauthenticated, "A validated user identity is required.");
        }
        catch (Exception exception)
        {
            var traceId = context.GetHttpContext().TraceIdentifier;
            // Do not log complete requests, credentials, or bearer tokens.
            logger.LogError(exception, "Unhandled failure in {Method}; trace {TraceId}", context.Method, traceId);
            throw new RpcException(
                new Status(StatusCode.Internal, "An unexpected server error occurred."),
                new Metadata { { "trace-id", traceId } });
        }
    }

    private static RpcException Failure(StatusCode code, string message) => new(new Status(code, message));
}
