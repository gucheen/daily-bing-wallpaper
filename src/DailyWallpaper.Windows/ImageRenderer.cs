using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DailyWallpaper;

public sealed class ImageRenderer : IImageRenderer
{
    public bool IsValid(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var image = Image.FromStream(stream, true, true);
            using var decoded = new Bitmap(image);
            return decoded.Width > 0 && decoded.Height > 0;
        }
        catch (Exception e) when (e is IOException or ArgumentException or ExternalException or OutOfMemoryException)
        { return false; }
    }

    public byte[] CreateDarkJpeg(byte[] original)
    {
        using var input = new MemoryStream(original);
        using var image = Image.FromStream(input, true, true);
        if (image.PropertyIdList.Contains(0x0112))
        {
            var orientation = image.GetPropertyItem(0x0112)?.Value;
            if (orientation is { Length: >= 2 })
                image.RotateFlip(BitConverter.ToUInt16(orientation, 0) switch
                {
                    2 => RotateFlipType.RotateNoneFlipX,
                    3 => RotateFlipType.Rotate180FlipNone,
                    4 => RotateFlipType.Rotate180FlipX,
                    5 => RotateFlipType.Rotate90FlipX,
                    6 => RotateFlipType.Rotate90FlipNone,
                    7 => RotateFlipType.Rotate270FlipX,
                    8 => RotateFlipType.Rotate270FlipNone,
                    _ => RotateFlipType.RotateNoneFlipNone
                });
        }
        using var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Black);
            graphics.DrawImage(image, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        }
        var locked = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
        try
        {
            var row = new byte[bitmap.Width * 3];
            for (var y = 0; y < bitmap.Height; y++)
            {
                var address = IntPtr.Add(locked.Scan0, y * locked.Stride);
                Marshal.Copy(address, row, 0, row.Length);
                for (var x = 0; x < row.Length; x += 3)
                {
                    var color = DarkTone.Apply(row[x + 2], row[x + 1], row[x]);
                    row[x] = color.B; row[x + 1] = color.G; row[x + 2] = color.R;
                }
                Marshal.Copy(row, 0, address, row.Length);
            }
        }
        finally { bitmap.UnlockBits(locked); }
        using var output = new MemoryStream();
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 95L);
        bitmap.Save(output, ImageCodecInfo.GetImageEncoders().Single(c => c.FormatID == ImageFormat.Jpeg.Guid), parameters);
        return output.ToArray();
    }
}
