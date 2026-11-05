using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace AionDPS.Ui;

/// <summary>
/// Class name -> icon, for the places a class name is bound at runtime rather than hardcoded in
/// XAML (the "Your Characters" list and the Players grid's Class column -- static ComboBoxItem
/// lists like ClassFilter/NewCharacterClassBox just reference the icon files directly). User
/// asked for icons instead of text names for classes wherever they appear. Icons are the same
/// assets/classes/icons/*.png files ClassFilter's dropdown items use, copied next to the exe via
/// the csproj's assets/**/*.* CopyToOutputDirectory item.
/// </summary>
public sealed class ClassIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string className || className.Length == 0)
        {
            return null;
        }

        lock (Cache)
        {
            if (Cache.TryGetValue(className, out BitmapSource? known))
            {
                return known;
            }

            string path = Path.Combine(AppContext.BaseDirectory, "assets", "classes", "icons", $"{className}.png");
            BitmapSource? icon = null;
            if (File.Exists(path))
            {
                // read once and shared by every line (a new picture per line and per refresh before), frozen so the overlay windows can use it too
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                icon = image;
            }

            Cache[className] = icon;
            return icon;
        }
    }

    private static readonly Dictionary<string, BitmapSource?> Cache = new(StringComparer.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
