using System.Text.Json;

namespace OpenLimiter.Service;

public sealed class JsonAuditLog(ServiceSettings settings) : IAuditLog
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(settings.DataDirectory);
        var line = JsonSerializer.Serialize(entry, SerializerOptions) + Environment.NewLine;

        await gate.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(settings.AuditFilePath, line, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }
}
