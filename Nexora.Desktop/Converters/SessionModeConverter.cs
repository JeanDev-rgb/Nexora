using System.Globalization;
using System.Windows.Data;
using Nexora.Core.Enums;

namespace Nexora.Desktop.Converters;

public sealed class SessionModeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is SessionMode.Prepaid ? "Tiempo contratado" : "Tiempo libre";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
