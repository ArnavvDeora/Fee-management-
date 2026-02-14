using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace SchoolFeeSystem.Presentation.Converters
{
    /// <summary>
    /// Converts byte[] (photo data) to BitmapImage for WPF Image controls
    /// Handles null/empty arrays gracefully
    /// </summary>
    public class ByteArrayToImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || !(value is byte[] bytes) || bytes.Length == 0)
            {
                // Return null for no image (WPF will show nothing)
                return null;
            }

            try
            {
                using (var stream = new MemoryStream(bytes))
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze(); // Important for performance!
                    return image;
                }
            }
            catch (Exception ex)
            {
                // Log the error for debugging
                System.Diagnostics.Debug.WriteLine($"⚠️ Image conversion failed: {ex.Message}");
                return null; // Return null if conversion fails
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // We don't need two-way binding for images
            throw new NotImplementedException();
        }
    }
}