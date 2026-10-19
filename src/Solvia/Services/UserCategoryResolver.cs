using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Models;
using Solvia.Models.Enums;

namespace Solvia.Services;

/// <summary>
/// Resolves a demo person's category by rule (REDO-PLAN §2b) —
/// Solvia has no login stack, so there is no claim to read; the
/// category is a property of the seeded fictional person:
///
/// 1. The person's <see cref="Student.CategoryOverride"/> wins if set.
/// 2. Else their email domain decides: a domain in
///    <see cref="CategoryOptions.EmployeeDomains"/> → Employee; a
///    domain in <see cref="CategoryOptions.StudentDomains"/> →
///    Student. (An address listed in both resolves Employee — the
///    employee list is consulted first.)
/// 3. Else Public.
///
/// Faculty and Visitor exist as categories (they price like Public)
/// but no seeded person resolves to them by domain, and no screen
/// sets them. The result is snapshotted onto each application at
/// creation; this resolver runs at that moment only.
/// </summary>
public sealed class UserCategoryResolver
{
    private readonly CategoryOptions _options;

    public UserCategoryResolver(IOptions<CategoryOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>Resolves the category for a seeded demo person.</summary>
    public UserCategory Resolve(Student student)
    {
        ArgumentNullException.ThrowIfNull(student);
        return Resolve(student.Email, student.CategoryOverride);
    }

    /// <summary>
    /// The rule itself, split out so callers holding only an email
    /// and an override (never a login claim) resolve identically.
    /// </summary>
    public UserCategory Resolve(string? email, UserCategory? categoryOverride)
    {
        if (categoryOverride is { } over)
        {
            return over;
        }

        var domain = DomainOf(email);
        if (domain is null)
        {
            return UserCategory.Public;
        }

        if (ContainsDomain(_options.EmployeeDomains, domain))
        {
            return UserCategory.Employee;
        }

        if (ContainsDomain(_options.StudentDomains, domain))
        {
            return UserCategory.Student;
        }

        return UserCategory.Public;
    }

    private static string? DomainOf(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var at = email.LastIndexOf('@');
        if (at < 0 || at == email.Length - 1)
        {
            return null;
        }

        return email[(at + 1)..].Trim().ToLowerInvariant();
    }

    private static bool ContainsDomain(IEnumerable<string> domains, string domain) =>
        domains.Any(d => string.Equals(d.Trim(), domain, StringComparison.OrdinalIgnoreCase));
}
