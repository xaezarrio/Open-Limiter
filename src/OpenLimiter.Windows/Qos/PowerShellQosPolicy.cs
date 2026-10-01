using System.Diagnostics;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using OpenLimiter.Core.Naming;

namespace OpenLimiter.Windows.Qos;

public sealed class PowerShellQosPolicy : IQosPolicy
{
    public async Task ApplyUploadLimitAsync(Guid ruleId, string executablePath, long bitsPerSecond, CancellationToken cancellationToken = default)
    {
        if (bitsPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bitsPerSecond));
        }

        await RemoveAsync(ruleId, cancellationToken);

        var name = OwnedPolicyName.Qos(ruleId);
        var script = string.Join(
            Environment.NewLine,
            "$ProgressPreference = 'SilentlyContinue'",
            "$ErrorActionPreference = 'Stop'",
            $"New-NetQosPolicy -Name '{Escape(name)}' -AppPathNameMatchCondition '{Escape(Path.GetFullPath(executablePath))}' -ThrottleRateActionBitsPerSecond {bitsPerSecond} -NetworkProfile All | Out-Null");

        await RunPowerShellAsync(script, cancellationToken);
    }

    public Task RemoveAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        var name = OwnedPolicyName.Qos(ruleId);
        var script = string.Join(
            Environment.NewLine,
            "$ProgressPreference = 'SilentlyContinue'",
            "$ErrorActionPreference = 'Stop'",
            $"$policy = Get-NetQosPolicy -Name '{Escape(name)}' -ErrorAction SilentlyContinue",
            "if ($null -ne $policy) {",
            "    $policy | Remove-NetQosPolicy -Confirm:$false -ErrorAction Stop",
            "}",
            "exit 0");

        return RunPowerShellAsync(script, cancellationToken);
    }

    internal static async Task RunPowerShellAsync(string script, CancellationToken cancellationToken)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(encoded);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows PowerShell could not be started.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }

            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(standardOutput, standardError);
            throw;
        }

        if (process.ExitCode != 0)
        {
            var error = await standardError;
            throw new InvalidOperationException(FormatPowerShellError(error, process.ExitCode));
        }

        await Task.WhenAll(standardOutput, standardError);
    }

    internal static string FormatPowerShellError(string standardError, int exitCode)
    {
        if (string.IsNullOrWhiteSpace(standardError))
        {
            return $"PowerShell exited with code {exitCode}.";
        }

        var trimmedError = standardError.Trim();
        const string CliXmlPrefix = "#< CLIXML";
        if (!trimmedError.StartsWith(CliXmlPrefix, StringComparison.Ordinal))
        {
            return trimmedError;
        }

        try
        {
            var document = XDocument.Parse(
                trimmedError[CliXmlPrefix.Length..].TrimStart(),
                LoadOptions.PreserveWhitespace);
            var serializedErrors = document
                .Descendants()
                .Where(element =>
                    element.Name.LocalName == "S" &&
                    string.Equals((string?)element.Attribute("S"), "Error", StringComparison.OrdinalIgnoreCase))
                .Select(element => element.Value);
            var decodedError = XmlConvert.DecodeName(string.Concat(serializedErrors)).Trim();
            return string.IsNullOrWhiteSpace(decodedError)
                ? $"PowerShell exited with code {exitCode} and returned an unreadable error."
                : decodedError;
        }
        catch (XmlException)
        {
            return $"PowerShell exited with code {exitCode} and returned an unreadable error.";
        }
    }

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
