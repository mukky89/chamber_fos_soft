using System.Security.Cryptography;
using System.Text;

namespace VotschVc3.Core.Security;

/// <summary>Access level of a user.</summary>
public enum UserRole
{
    /// <summary>Can view and connect, but not change set points or programs.</summary>
    Operator = 0,

    /// <summary>Can operate the chambers (set points, profiles).</summary>
    Supervisor = 1,

    /// <summary>Full access including user and chamber management.</summary>
    Admin = 2,
}

/// <summary>An application user. Passwords are stored only as a SHA-256 hash.</summary>
public sealed class User
{
    public string Name { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Operator;
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Hashes a clear-text password to a lowercase hex SHA-256 string.</summary>
    public static string Hash(string password)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Verifies a clear-text password against the stored hash.</summary>
    public bool VerifyPassword(string password) =>
        string.Equals(PasswordHash, Hash(password), StringComparison.OrdinalIgnoreCase);
}
