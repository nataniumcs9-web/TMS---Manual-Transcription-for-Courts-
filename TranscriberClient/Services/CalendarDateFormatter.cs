using System.Globalization;
using System.Windows.Data;

namespace TranscriberClient.Services;

public static class CalendarDateFormatter
{
    private static readonly string[] EthiopianMonthNames =
    [
        "Meskerem", "Tikimt", "Hidar", "Tahsas", "Tir", "Yekatit",
        "Megabit", "Miazia", "Ginbot", "Sene", "Hamle", "Nehase", "Pagume"
    ];

    public static string FormatDate(DateTime? date)
    {
        return FormatDate(date, AppSettings.UiPreferences.UseEthiopianCalendar);
    }

    public static string FormatDate(DateTime? date, bool useEthiopianCalendar)
    {
        if (!date.HasValue)
        {
            return string.Empty;
        }

        if (!useEthiopianCalendar)
        {
            return date.Value.ToString("d", CultureInfo.CurrentCulture);
        }

        var ethiopian = ToEthiopianDate(date.Value);
        return $"{ethiopian.Day} {EthiopianMonthNames[ethiopian.Month - 1]} {ethiopian.Year}";
    }

    public static bool TryParseDate(string? text, out DateTime date)
    {
        return TryParseDate(text, AppSettings.UiPreferences.UseEthiopianCalendar, out date);
    }

    public static bool TryParseDate(string? text, bool useEthiopianCalendar, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (!useEthiopianCalendar)
        {
            return DateTime.TryParseExact(
                    value,
                    ["yyyy-MM-dd", "d/M/yyyy", "dd/MM/yyyy"],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out date)
                || DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out date);
        }

        var isoParts = value.Split('-');
        if (isoParts.Length == 3
            && int.TryParse(isoParts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var isoYear)
            && int.TryParse(isoParts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var isoMonth)
            && int.TryParse(isoParts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var isoDay))
        {
            return TryFromEthiopianDate(isoYear, isoMonth, isoDay, out date);
        }

        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 3
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var day)
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var year))
        {
            var month = Array.FindIndex(
                EthiopianMonthNames,
                name => string.Equals(name, parts[1], StringComparison.OrdinalIgnoreCase)) + 1;
            if (month > 0)
            {
                return TryFromEthiopianDate(year, month, day, out date);
            }
        }

        var numericParts = value.Split('/');
        return numericParts.Length == 3
            && int.TryParse(numericParts[0], NumberStyles.None, CultureInfo.InvariantCulture, out day)
            && int.TryParse(numericParts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var numericMonth)
            && int.TryParse(numericParts[2], NumberStyles.None, CultureInfo.InvariantCulture, out year)
            && TryFromEthiopianDate(year, numericMonth, day, out date);
    }

    private static (int Year, int Month, int Day) ToEthiopianDate(DateTime date)
    {
        var year = date.Year - 7;
        var newYear = EthiopianNewYear(year);
        if (date.Date < newYear)
        {
            year--;
            newYear = EthiopianNewYear(year);
        }

        var dayOfYear = (date.Date - newYear).Days;
        return (year, dayOfYear / 30 + 1, dayOfYear % 30 + 1);
    }

    private static bool TryFromEthiopianDate(int year, int month, int day, out DateTime date)
    {
        date = default;
        if (year < 1 || month is < 1 or > 13 || day < 1 || day > 30)
        {
            return false;
        }

        if (month == 13 && day > (IsEthiopianLeapYear(year) ? 6 : 5))
        {
            return false;
        }

        try
        {
            date = EthiopianNewYear(year).AddDays((month - 1) * 30 + day - 1);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException)
        {
            return false;
        }
    }

    private static DateTime EthiopianNewYear(int ethiopianYear)
    {
        var gregorianYear = checked(ethiopianYear + 7);
        var start = new DateTime(gregorianYear, 9, 11);
        return IsEthiopianLeapYear(ethiopianYear - 1) ? start.AddDays(1) : start;
    }

    private static bool IsEthiopianLeapYear(int year) => year % 4 == 3;
}

public sealed class CalendarDateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is DateTime date ? CalendarDateFormatter.FormatDate(date) : string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
