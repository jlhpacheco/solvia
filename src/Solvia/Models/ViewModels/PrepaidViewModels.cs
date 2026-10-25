using Solvia.Services;

namespace Solvia.Models.ViewModels;

public sealed record PrepaidVehicleOption(Guid Id, string Label);
public sealed record PrepaidChooseViewModel(string Plan, PrepaidPlan Schedule,
    IReadOnlyList<PrepaidVehicleOption> Vehicles, Guid? VehicleId, IReadOnlySet<DateOnly> SelectedDates,
    int? UnitCents, string? Error = null);
public sealed record PrepaidReviewViewModel(PrepaidPurchase Purchase, string Snapshot,
    int? CurrentUnitCents, PlanResult Comparison, string? Error = null)
{
    public bool Settled => Purchase.PaymentStatus is Enums.PaymentStatus.Paid or Enums.PaymentStatus.NotRequired;
    public bool PriceChanged => string.IsNullOrWhiteSpace(Purchase.PayPalOrderId) && CurrentUnitCents != Purchase.UnitPriceCents;
}
