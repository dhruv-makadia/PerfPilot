using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using PerfPilot.Core.Abstractions;
using PerfPilot.Core.Common;

namespace PerfPilot.Infrastructure.Security;

/// <summary>Encrypts environment secrets at rest with ASP.NET Core Data Protection.</summary>
internal sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("PerfPilot.EnvironmentSecrets.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string protectedValue)
    {
        try
        {
            return _protector.Unprotect(protectedValue);
        }
        catch (CryptographicException ex)
        {
            throw new ConflictException(
                "A stored secret could not be decrypted (the Data Protection key ring may have changed). Re-enter the environment's secrets. " + ex.Message);
        }
    }
}
