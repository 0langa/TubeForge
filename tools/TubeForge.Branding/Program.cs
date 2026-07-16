using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TubeForge.Branding;

internal static class Program
{
    private static readonly int[] IconSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
    private static readonly Geometry PlayGeometry = Geometry.Parse(
        "M 78,59 C 71,55 63,60 63,68 L 63,188 C 63,196 72,201 79,197 L 184,136 C 191,132 191,123 184,119 Z");
    private static readonly Geometry SparkGeometry = Geometry.Parse(
        "M 194,27 L 201,47 L 222,54 L 201,61 L 194,82 L 187,61 L 166,54 L 187,47 Z");

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            Console.Error.WriteLine("Usage: TubeForge.Branding <assets-directory>");
            return 2;
        }

        var outputDirectory = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(outputDirectory);
        var images = IconSizes.Select(RenderPng).ToArray();
        File.WriteAllBytes(Path.Combine(outputDirectory, "TubeForge.png"), images[^1].Bytes);
        WriteIcon(Path.Combine(outputDirectory, "TubeForge.ico"), images);
        return 0;
    }

    private static IconImage RenderPng(int size)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var scale = size / 256d;
            context.PushTransform(new ScaleTransform(scale, scale));
            var surface = new LinearGradientBrush(
                Color.FromRgb(0x8b, 0x6c, 0xff),
                Color.FromRgb(0x5c, 0x3e, 0xe6),
                new Point(0.14, 0.09),
                new Point(0.86, 0.91));
            surface.Freeze();
            context.DrawRoundedRectangle(surface, null, new Rect(0, 0, 256, 256), 58, 58);
            context.DrawGeometry(Brushes.White, null, PlayGeometry);
            var spark = new SolidColorBrush(Color.FromRgb(0xff, 0xad, 0x42));
            spark.Freeze();
            context.DrawGeometry(spark, null, SparkGeometry);
            var ray = new Pen(spark, 10)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            ray.Freeze();
            context.DrawLine(ray, new Point(214, 88), new Point(228, 96));
            context.DrawLine(ray, new Point(161, 27), new Point(153, 13));
            context.Pop();
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return new IconImage(size, stream.ToArray());
    }

    private static void WriteIcon(string destination, IReadOnlyList<IconImage> images)
    {
        using var stream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write(checked((ushort)images.Count));

        var offset = checked(6 + images.Count * 16);
        foreach (var image in images)
        {
            writer.Write(image.Size == 256 ? (byte)0 : checked((byte)image.Size));
            writer.Write(image.Size == 256 ? (byte)0 : checked((byte)image.Size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(image.Bytes.Length);
            writer.Write(offset);
            offset = checked(offset + image.Bytes.Length);
        }

        foreach (var image in images)
        {
            writer.Write(image.Bytes);
        }
    }

    private sealed record IconImage(int Size, byte[] Bytes);
}
