using System.Globalization;
using Carpoolr.Api.Features.Trips;
using Carpoolr.Api.Security;

namespace Carpoolr.Api.Features;

public static class ApplicationRules
{
    public static Guid Id(string value, string field)
    {
        if (!Guid.TryParseExact(value, "D", out var id) || id == Guid.Empty)
            throw new TripValidationException($"{field} must be a non-empty UUID.");
        return id;
    }

    public static string Text(string? value, string field, int maximum = 200)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > maximum || text.Any(char.IsControl))
            throw new TripValidationException($"{field} must contain 1..{maximum} characters without control characters.");
        return text;
    }

    public static decimal Decimal(string value, string field, decimal maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 20 ||
            !decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) ||
            number <= 0 || number > maximum || decimal.Round(number, 4) != number)
            throw new TripValidationException($"{field} must be a positive decimal with at most four decimal places.");
        return number;
    }

    public static string Format(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    public static bool IsOwner(Trip trip, UserIdentity actor) =>
        trip.OwnerIssuer == actor.Issuer && trip.OwnerSubject == actor.Subject;

    public static int PageSize(int value)
    {
        if (value is < 0 or > 100) throw new TripValidationException("page_size must be between 0 and 100.");
        return value == 0 ? 20 : value;
    }

    public static int Offset(int value)
    {
        if (value is < 0 or > 10000) throw new TripValidationException("offset must be between 0 and 10000.");
        return value;
    }
}
