using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Solvia.Models.Enums;

namespace Solvia.Services;

public sealed record PrepaidPlan(string Owner, string Campus, TermKind Term,
    DateOnly[] Dates, DateTimeOffset ExpiresAt);

/// <summary>A short-lived server-authenticated schedule. Prices are always resolved separately.</summary>
public sealed class PrepaidPlanProtection(IDataProtectionProvider provider, TimeProvider clock)
{
    private readonly IDataProtector _protector = provider.CreateProtector("Solvia.PrepaidPlan.v1");
    public string Protect(string owner, string campus, TermKind term, IReadOnlyList<DateOnly> dates) =>
        _protector.Protect(JsonSerializer.Serialize(new PrepaidPlan(owner, campus, term,
            dates.Distinct().Order().ToArray(), clock.GetUtcNow().AddHours(2))));

    public PrepaidPlan? Read(string? token, string owner)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 8192) return null;
        try
        {
            var plan = JsonSerializer.Deserialize<PrepaidPlan>(_protector.Unprotect(token));
            return plan is not null && plan.Owner == owner && plan.ExpiresAt > clock.GetUtcNow() &&
                plan.Dates is { Length: >= 1 and <= 182 } && Enum.IsDefined(plan.Term) &&
                plan.Dates.Distinct().Count() == plan.Dates.Length ? plan : null;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or ArgumentException)
        { return null; }
    }
}
