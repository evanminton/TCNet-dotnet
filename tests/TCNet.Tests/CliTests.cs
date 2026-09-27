using System.Diagnostics;
using System.Reflection;

namespace TCNet.Tests;

/// <summary>Runs the built tcnet utility (TCNet.Utility.dll) as a process: offline commands and bad input.</summary>
public class CliTests
{
    private static readonly string Utility = typeof(CliTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "TCNetUtility").Value!;

    private static (int Exit, string Out, string Err) Run(params string[] args)
    {
        Assert.True(File.Exists(Utility), $"utility not built: {Utility}");
        var psi = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host && File.Exists(host) ? host : "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(Utility);
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.StandardInput.Close();
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(TimeSpan.FromSeconds(60)))
        {
            p.Kill(entireProcessTree: true);
            Assert.Fail($"tcnet {string.Join(' ', args)} did not exit");
        }
        p.WaitForExit();
        var (o, e) = (stdout.Result, stderr.Result);
        Assert.DoesNotContain("Unhandled exception", o + e);
        Assert.DoesNotContain("   at ", e);
        return (p.ExitCode, o, e);
    }

    [Fact]
    public void Help()
    {
        var (exit, o, _) = Run("help");
        Assert.Equal(0, exit);
        Assert.Contains("TCNet Link Specification", o);
    }

    [Fact]
    public void Codes()
    {
        var (exit, o, _) = Run("codes");
        Assert.Equal(0, exit);
        Assert.Contains("Pioneer", o);
    }

    [Fact]
    public void Layout_Time()
    {
        var (exit, o, _) = Run("layout", "254");
        Assert.Equal(0, exit);
        Assert.Contains("Node ID", o);
    }

    [Fact]
    public void Build_Then_Decode()
    {
        var (exit, o, _) = Run("build", "optin");
        Assert.Equal(0, exit);
        var hex = o.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Last();
        Assert.Matches("^[0-9A-F]+$", hex);

        (exit, o, _) = Run("decode", hex);
        Assert.Equal(0, exit);
        Assert.Contains("Node Name", o);
    }

    [Fact]
    public void Decode_NotTCNet_Exits1()
    {
        var (exit, o, _) = Run("decode", "54434e");
        Assert.Equal(1, exit);
        Assert.Contains("not a TCNet packet", o);
    }

    [Theory]
    [InlineData("layout", "nosuch")]
    [InlineData("options", "nosuch")]
    [InlineData("frobnicate")]
    public void NothingFound_Exits1(params string[] args)
    {
        var (exit, _, e) = Run(args);
        Assert.Equal(1, exit);
        Assert.NotEmpty(e.Trim());
    }

    // All of these fail on the arguments, before a node is started (so nothing touches the network).
    [Theory]
    [InlineData("nodes", "--seconds", "99999999999")]
    [InlineData("listen", "--type", "300")]
    [InlineData("listen", "--id", "70000")]
    [InlineData("listen", "--port", "-1")]
    [InlineData("listen", "--role", "99")]
    [InlineData("listen", "--role", "boss")]
    [InlineData("listen", "--nosuchoption")]
    [InlineData("listen", "--id")]
    [InlineData("master", "--interval", "99999999999")]
    [InlineData("request", "SOMENODE", "metrics", "999")]
    [InlineData("request", "SOMENODE", "300")]
    [InlineData("request", "SOMENODE")]
    [InlineData("sync", "SOMENODE", "99999999999")]
    [InlineData("sync", "SOMENODE", "0")]
    [InlineData("control", "SOMENODE")]
    [InlineData("text")]
    [InlineData("key", "")]
    [InlineData("key", "abc")]
    [InlineData("key", "0x1FFFF")]
    [InlineData("send", "time", "1.2.3.4:99999")]
    [InlineData("send", "time", "bcast:x")]
    [InlineData("send", "nosuchpacket")]
    [InlineData("build")]
    [InlineData("build", "nosuchpacket")]
    [InlineData("decode")]
    [InlineData("decode", "@this-file-does-not-exist.bin")]
    [InlineData("decode", "123")]
    public void BadInput_CleanError(params string[] args)
    {
        var (exit, o, e) = Run(args);
        Assert.Equal(2, exit);
        Assert.StartsWith("tcnet: ", e);
        Assert.DoesNotContain("# ", e);
        Assert.Empty(o);
    }
}
