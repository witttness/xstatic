using Extatic.Api.Services;

namespace Extatic.Api.Features.Authentication;

/// <summary>Password rules: per-App minimum length, max 256 chars, not the email, not a very common password.</summary>
public static class PasswordPolicy
{
    public const int AbsoluteMinLength = 8;
    public const int MaxLength = 256;

    // A small built-in deny list of the most common passwords (case-insensitive) that pass a length check.
    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "password12", "password123", "password1234", "passw0rd", "p@ssw0rd",
        "p@ssword", "password!", "password1!", "12345678", "123456789", "1234567890", "12345678910",
        "123123123", "11111111", "111111111", "1111111111", "00000000", "0000000000", "87654321",
        "987654321", "0987654321", "qwertyuiop", "qwerty123", "qwerty1234", "qwertyui", "1q2w3e4r",
        "1q2w3e4r5t", "1qaz2wsx", "zaq12wsx", "asdfghjkl", "asdfasdf", "zxcvbnm1", "abcd1234",
        "abc12345", "abcdefgh", "abcdefg1", "iloveyou", "iloveyou1", "sunshine", "sunshine1",
        "princess", "princess1", "football", "football1", "baseball", "superman", "trustno1",
        "welcome1", "welcome123", "letmein1", "letmein123", "changeme", "changeme123", "admin123",
        "administrator", "starwars", "whatever", "computer", "internet", "michelle", "jennifer",
        "liverpool", "chocolate", "passpass", "qwertyqwerty", "aaaaaaaa", "secret123", "monkey123",
        "dragon123", "master123", "shadow123", "loveyou1", "q1w2e3r4", "q1w2e3r4t5", "1234qwer",
    };

    public static List<ValidationError> Validate(string? password, string? email, int minLength,
        string pointer = "password")
    {
        var errors = new List<ValidationError>();
        minLength = Math.Max(minLength, AbsoluteMinLength);
        if (string.IsNullOrEmpty(password))
        {
            errors.Add(new ValidationError(pointer, "Password is required"));
            return errors;
        }
        if (password.Length < minLength)
            errors.Add(new ValidationError(pointer, $"Password must be at least {minLength} characters"));
        else if (password.Length > MaxLength)
            errors.Add(new ValidationError(pointer, $"Password must be at most {MaxLength} characters"));
        else if (email is not null && string.Equals(password.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase))
            errors.Add(new ValidationError(pointer, "Password must not be your email address"));
        else if (IsCommon(password))
            errors.Add(new ValidationError(pointer, "This password is too common"));
        return errors;
    }

    public static bool IsCommon(string password) => CommonPasswords.Contains(password.Trim());

    public static void EnsureValid(string? password, string? email, int minLength, string pointer = "password")
    {
        var errors = Validate(password, email, minLength, pointer);
        if (errors.Count > 0) throw new ValidationException("Password does not meet the policy", errors);
    }
}
