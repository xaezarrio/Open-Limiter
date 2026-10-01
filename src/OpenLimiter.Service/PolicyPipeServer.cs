using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using OpenLimiter.Protocol;

namespace OpenLimiter.Service;

public sealed class PolicyPipeServer(
    ServiceSettings settings,
    IPolicyRequestHandler requestHandler,
    ILogger<PolicyPipeServer> logger)
{
    private static readonly TimeSpan ClientReadTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(45);
    private const int MaximumConcurrentClients = 16;
    private readonly SemaphoreSlim clientGate = new(MaximumConcurrentClients, MaximumConcurrentClients);

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Policy IPC server is listening on {PipeName}.", settings.PipeName);

        while (!stoppingToken.IsCancellationRequested)
        {
            await clientGate.WaitAsync(stoppingToken);
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = CreateServerPipe();
                await pipe.WaitForConnectionAsync(stoppingToken);
            }
            catch
            {
                if (pipe is not null)
                {
                    await pipe.DisposeAsync();
                }
                clientGate.Release();
                throw;
            }

            _ = HandleClientAndReleaseAsync(pipe, stoppingToken);
        }
    }

    private async Task HandleClientAndReleaseAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        try
        {
            await HandleClientAsync(pipe, stoppingToken);
        }
        finally
        {
            clientGate.Release();
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        await using (pipe)
        {
            Guid requestId = Guid.Empty;

            try
            {
                var caller = pipe.GetImpersonationUserName();
                if (string.IsNullOrWhiteSpace(caller))
                {
                    throw new UnauthorizedAccessException("The named-pipe client has no authenticated Windows identity.");
                }

                PolicyRequest request;
                using (var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
                {
                    readTimeout.CancelAfter(ClientReadTimeout);
                    request = await PipeMessageCodec.ReadAsync<PolicyRequest>(pipe, readTimeout.Token);
                }

                requestId = request.RequestId;
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                requestTimeout.CancelAfter(RequestTimeout);
                var response = await requestHandler.HandleAsync(request, caller, requestTimeout.Token);
                await PipeMessageCodec.WriteAsync(pipe, response, requestTimeout.Token);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Policy IPC client timed out.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Policy IPC request was rejected.");
                if (pipe.IsConnected)
                {
                    try
                    {
                        using var errorResponseTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                        errorResponseTimeout.CancelAfter(ClientReadTimeout);
                        await PipeMessageCodec.WriteAsync(pipe, new PolicyResponse
                        {
                            RequestId = requestId,
                            Succeeded = false,
                            ErrorCode = "invalid_message",
                            ErrorMessage = "The policy service rejected the IPC message.",
                        }, errorResponseTimeout.Token);
                    }
                    catch (Exception writeException) when (writeException is not OperationCanceledException)
                    {
                        logger.LogDebug(writeException, "Could not return the IPC error response.");
                    }
                }
            }
        }
    }

    private NamedPipeServerStream CreateServerPipe()
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Deny));
        security.AddAccessRule(new(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new(
            settings.AllowedUserSid,
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        var currentSid = WindowsIdentity.GetCurrent().User;
        if (currentSid is not null)
        {
            security.AddAccessRule(new(currentSid, PipeAccessRights.FullControl, AccessControlType.Allow));
        }

        return NamedPipeServerStreamAcl.Create(
            settings.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough,
            0,
            0,
            security);
    }
}
