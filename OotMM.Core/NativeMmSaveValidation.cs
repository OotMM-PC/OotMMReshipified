using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace OotMM.Core;

public static class NativeMmSaveValidation
{
    public const string SavePairJournalSuffix = ".pair-transaction";

    public static string SerializePairRecoveryRequest(string primary, string saveId, uint slot)
    {
        if (!SessionPaths.IsValidSaveId(saveId) || slot > 2
            || Path.GetFileName(primary) != $"file{slot + 1}.json")
            throw new ArgumentException("Invalid MM save pair path or identity.");
        return JsonSerializer.Serialize(new { version = 1, saveId, slot, primary = Path.GetFullPath(primary) });
    }

    public static Task<string?> RecoverPairAsync(string executable, string primary, string saveId, uint slot,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(primary + SavePairJournalSuffix)) return Task.FromResult<string?>(null);
        return RunAsync(executable, () => SerializePairRecoveryRequest(primary, saveId, slot),
            "--ootmm-recover-save-pair", "mm-save-pair-v1", cancellationToken);
    }

    public static string SerializeRequest(string contents, string saveId, uint slot)
    {
        if (!SessionPaths.IsValidSaveId(saveId) || slot > 2 || Encoding.UTF8.GetByteCount(contents) > 8 * 1024 * 1024)
            throw new ArgumentException("Invalid MM validation source or identity.");
        return JsonSerializer.Serialize(new { version = 1, saveId, slot, contents });
    }

    public static string? ReadResult(string output, int exitCode)
    {
        const string invalid = "MM save validation returned an invalid result.";
        if (output.Length > 16384) return invalid;
        try
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 3
                || !NativeSaveCopyPayload.HasUniqueFields(root)
                || !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var number) || number != 1
                || !root.TryGetProperty("valid", out var valid) || valid.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !root.TryGetProperty("reason", out var reason) || reason.ValueKind != JsonValueKind.String
                || reason.GetString() is not { Length: <= 4096 } message)
                return invalid;
            if (valid.GetBoolean()) return exitCode == 0 && message.Length == 0 ? null : invalid;
            return exitCode == 1 && !string.IsNullOrWhiteSpace(message) ? "MM save validation failed: " + message : invalid;
        }
        catch (JsonException)
        {
            return invalid;
        }
    }

    public static Task<string?> ValidateAsync(string executable, MmSaveSource source, string saveId, uint slot,
        CancellationToken cancellationToken) => RunAsync(executable, () => SerializeRequest(source.Contents, saveId, slot),
            "--ootmm-validate-save", "mm-validation-command-v1", cancellationToken);

    private static async Task<string?> RunAsync(string executable, Func<string> serialize, string command,
        string capability, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var token = timeout.Token;
        using var process = new Process();
        var started = false;
        try
        {
            var compatibility = await Task.Run(() => NativeBuildCompatibility.Validate(executable, Game.Mm,
                out var error, capability) ? null : error, token).ConfigureAwait(false);
            if (compatibility is not null) return compatibility;
            var request = await Task.Run(serialize, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            process.StartInfo = new ProcessStartInfo(Path.GetFullPath(executable))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false, true),
                StandardOutputEncoding = new UTF8Encoding(false, true),
                StandardErrorEncoding = new UTF8Encoding(false, true),
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable))!,
            };
            process.StartInfo.ArgumentList.Add(command);
            started = process.Start();
            if (!started) return "Could not start native MM save validation.";
            var stdout = ReadLimitedAsync(process.StandardOutput, token);
            var stderr = ReadLimitedAsync(process.StandardError, token);
            var send = SendAsync(process.StandardInput, request, token);
            await Task.WhenAll(stdout, stderr, send, process.WaitForExitAsync(token)).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return ReadResult(await stdout.ConfigureAwait(false), process.ExitCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return "Native MM save validation timed out. No game was started.";
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or UnauthorizedAccessException or ArgumentException)
        {
            return "Could not validate the MM save: " + ex.Message;
        }
        finally
        {
            if (started && !process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                }
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private static async Task SendAsync(StreamWriter writer, string request, CancellationToken token)
    {
        await writer.WriteAsync(request.AsMemory(), token).ConfigureAwait(false);
        await writer.FlushAsync(token).ConfigureAwait(false);
        writer.Close();
    }

    private static async Task<string> ReadLimitedAsync(StreamReader reader, CancellationToken token)
    {
        var result = new StringBuilder();
        var buffer = new char[2048];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0)
        {
            if (result.Length + count > 16384) throw new InvalidDataException("Native validation output is too large.");
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }
}
