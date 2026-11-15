using Weather.UseCases.WeatherHistory.QueryWeatherHistory;

namespace Weather.Infrastructure.WeatherHistory;

internal static class WeatherTimeZone
{
    public static string ToSqlServerId(string timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZone) || timeZone.Length > 128)
            throw InvalidTimeZone();
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
            if (zone.Id == "UTC") return "UTC";
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(zone.Id, out var windowsId)) return windowsId;
            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out _)) return zone.Id;
        }
        catch (TimeZoneNotFoundException) { }
        catch (InvalidTimeZoneException) { }
        throw InvalidTimeZone();
    }

    private static WeatherHistoryQueryException InvalidTimeZone() =>
        new("Unknown or unsupported timeZone. Use an IANA timezone such as America/Chicago, a Windows timezone such as Central Standard Time, or UTC.");
}
