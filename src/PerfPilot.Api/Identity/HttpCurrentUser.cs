using Microsoft.Extensions.Options;
using PerfPilot.Core.Abstractions;

namespace PerfPilot.Api.Identity;

public sealed class IdentityOptions
{
    public const string SectionName = "Identity";

    /// <summary>"Header" (UI asks for a display name, sent as X-PerfPilot-User) or "Windows" (Negotiate/Kerberos/NTLM).</summary>
    public string Mode { get; set; } = IdentityModes.Header;

    public bool IsWindows => string.Equals(Mode, IdentityModes.Windows, StringComparison.OrdinalIgnoreCase);
}

public static class IdentityModes
{
    public const string Header = "Header";
    public const string Windows = "Windows";
}

/// <summary>Resolves who is making the request, for "started by" and audit fields.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor, IOptions<IdentityOptions> options) : ICurrentUser
{
    public const string HeaderName = "X-PerfPilot-User";
    private const int MaxLength = 100;

    public string Name
    {
        get
        {
            var context = accessor.HttpContext;
            if (context is null)
            {
                return "system";
            }
            if (options.Value.IsWindows)
            {
                return context.User.Identity is { IsAuthenticated: true, Name: { Length: > 0 } name } ? name : "anonymous";
            }

            var header = context.Request.Headers[HeaderName].ToString();
            if (string.IsNullOrWhiteSpace(header))
            {
                return "anonymous";
            }
            var decoded = Uri.UnescapeDataString(header).Trim();
            return decoded.Length > MaxLength ? decoded[..MaxLength] : decoded;
        }
    }
}
