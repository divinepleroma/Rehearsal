using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;

namespace Rehearsal.Preview;

internal sealed class Texture(int width, int height, byte[] rgba)
{
    public readonly int Height = height;
    public readonly byte[] Rgba = rgba;
    public readonly int Width = width;
}

/// <summary>
/// A tiny software renderer for ImGui draw data: textured, vertex-coloured triangles with clip
/// rectangles and "over" blending. Slow and simple, but faithful enough to judge a layout by.
/// </summary>
internal sealed unsafe class Canvas
{
    private readonly float[] pixels; // r, g, b per pixel, 0..1

    public Canvas(int width, int height)
    {
        Width = width;
        Height = height;
        pixels = new float[width * height * 3];
    }

    public int Height { get; }
    public int Width { get; }

    public void Clear(Vector3 color)
    {
        for (var i = 0; i < pixels.Length; i += 3)
        {
            pixels[i] = color.X;
            pixels[i + 1] = color.Y;
            pixels[i + 2] = color.Z;
        }
    }

    /// <summary>Fills the canvas with a region of an image, scaled to cover it.</summary>
    public void DrawBackground(Bitmap source, Rectangle region)
    {
        using var scaled = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, new Rectangle(0, 0, Width, Height), region, GraphicsUnit.Pixel);
        }

        var data = scaled.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < Height; y++)
            {
                var row = (byte*)data.Scan0 + y * data.Stride;
                for (var x = 0; x < Width; x++)
                {
                    var i = (y * Width + x) * 3;
                    pixels[i] = row[x * 4 + 2] / 255f;
                    pixels[i + 1] = row[x * 4 + 1] / 255f;
                    pixels[i + 2] = row[x * 4] / 255f;
                }
            }
        }
        finally
        {
            scaled.UnlockBits(data);
        }
    }

    /// <summary>Stand-in for Dalamud's backdrop blur: a few box blur passes inside a rounded rectangle.</summary>
    public void Blur(Vector2 min, Vector2 max, float rounding, int radius)
    {
        var x0 = Math.Clamp((int)MathF.Floor(min.X), 0, Width);
        var y0 = Math.Clamp((int)MathF.Floor(min.Y), 0, Height);
        var x1 = Math.Clamp((int)MathF.Ceiling(max.X), 0, Width);
        var y1 = Math.Clamp((int)MathF.Ceiling(max.Y), 0, Height);
        var w = x1 - x0;
        var h = y1 - y0;
        if (w <= 0 || h <= 0 || radius <= 0)
            return;

        // Blur a padded copy so the edges pull in what is just outside the panel, like the real thing.
        var pad = radius * 3;
        var bx0 = Math.Max(0, x0 - pad);
        var by0 = Math.Max(0, y0 - pad);
        var bx1 = Math.Min(Width, x1 + pad);
        var by1 = Math.Min(Height, y1 + pad);
        var bw = bx1 - bx0;
        var bh = by1 - by0;
        var a = new float[bw * bh * 3];
        var b = new float[bw * bh * 3];
        for (var y = 0; y < bh; y++)
            Array.Copy(pixels, ((by0 + y) * Width + bx0) * 3, a, y * bw * 3, bw * 3);

        for (var pass = 0; pass < 3; pass++)
        {
            BoxPass(a, b, bw, bh, radius, true);
            BoxPass(b, a, bw, bh, radius, false);
        }

        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                if (!InsideRounded(x + 0.5f, y + 0.5f, min, max, rounding))
                    continue;

                var src = ((y - by0) * bw + (x - bx0)) * 3;
                var dst = (y * Width + x) * 3;
                pixels[dst] = a[src];
                pixels[dst + 1] = a[src + 1];
                pixels[dst + 2] = a[src + 2];
            }
        }
    }

    public void Draw(ImDrawDataPtr data, IReadOnlyDictionary<ulong, Texture> textures)
    {
        for (var n = 0; n < data.CmdListsCount; n++)
        {
            var list = data.CmdLists[n];
            var vertices = list->VtxBuffer.Data;
            var indices = list->IdxBuffer.Data;

            for (var c = 0; c < list->CmdBuffer.Size; c++)
            {
                var cmd = &list->CmdBuffer.Data[c];
                if (cmd->UserCallback != null || cmd->ElemCount == 0)
                    continue;

                if (!textures.TryGetValue(cmd->TextureId.Handle, out var texture))
                    continue;

                var clip = cmd->ClipRect;
                for (uint e = 0; e + 2 < cmd->ElemCount; e += 3)
                {
                    var v0 = vertices[indices[cmd->IdxOffset + e] + cmd->VtxOffset];
                    var v1 = vertices[indices[cmd->IdxOffset + e + 1] + cmd->VtxOffset];
                    var v2 = vertices[indices[cmd->IdxOffset + e + 2] + cmd->VtxOffset];
                    Triangle(v0, v1, v2, texture, clip);
                }
            }
        }
    }

    public void Save(string path)
    {
        using var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[Width * 4];
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    var i = (y * Width + x) * 3;
                    row[x * 4] = ToByte(pixels[i + 2]);
                    row[x * 4 + 1] = ToByte(pixels[i + 1]);
                    row[x * 4 + 2] = ToByte(pixels[i]);
                    row[x * 4 + 3] = 255;
                }

                Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, row.Length);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        // Photographs of the UI over scenery are far smaller as JPEG; anything else stays lossless.
        if (path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
        {
            var codec = Array.Find(ImageCodecInfo.GetImageEncoders(), c => c.FormatID == ImageFormat.Jpeg.Guid)
                        ?? throw new InvalidOperationException("No JPEG encoder available.");
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(Encoder.Quality, 90L);
            bitmap.Save(path, codec, parameters);
            return;
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    /// <summary>Copies another canvas into this one at the given position.</summary>
    public void Blit(Canvas source, int left, int top)
    {
        for (var y = 0; y < source.Height; y++)
        {
            if (top + y < 0 || top + y >= Height)
                continue;

            for (var x = 0; x < source.Width; x++)
            {
                if (left + x < 0 || left + x >= Width)
                    continue;

                var s = (y * source.Width + x) * 3;
                var d = ((top + y) * Width + left + x) * 3;
                pixels[d] = source.pixels[s];
                pixels[d + 1] = source.pixels[s + 1];
                pixels[d + 2] = source.pixels[s + 2];
            }
        }
    }

    private static byte ToByte(float v)
    {
        return (byte)(Math.Clamp(v, 0f, 1f) * 255f + 0.5f);
    }

    private static void BoxPass(float[] src, float[] dst, int w, int h, int radius, bool horizontal)
    {
        var length = horizontal ? w : h;
        var lines = horizontal ? h : w;
        var norm = 1f / (radius * 2 + 1);

        for (var line = 0; line < lines; line++)
        {
            for (var channel = 0; channel < 3; channel++)
            {
                float sum = 0;
                for (var k = -radius; k <= radius; k++)
                    sum += src[Index(Math.Clamp(k, 0, length - 1))];

                for (var i = 0; i < length; i++)
                {
                    dst[Index(i)] = sum * norm;
                    sum += src[Index(Math.Min(length - 1, i + radius + 1))] - src[Index(Math.Max(0, i - radius))];
                }

                continue;

                int Index(int i)
                {
                    return ((horizontal ? line * w + i : i * w + line) * 3) + channel;
                }
            }
        }
    }

    private static bool InsideRounded(float x, float y, Vector2 min, Vector2 max, float r)
    {
        if (x < min.X || y < min.Y || x > max.X || y > max.Y)
            return false;

        var cx = Math.Clamp(x, min.X + r, max.X - r);
        var cy = Math.Clamp(y, min.Y + r, max.Y - r);
        var dx = x - cx;
        var dy = y - cy;
        return dx * dx + dy * dy <= r * r;
    }

    private static float Edge(Vector2 a, Vector2 b, float px, float py)
    {
        return (px - a.X) * (b.Y - a.Y) - (py - a.Y) * (b.X - a.X);
    }

    /// <summary>Whether pixels lying exactly on the edge a-b belong to the triangle (top-left fill rule).</summary>
    private static bool OwnsEdge(Vector2 a, Vector2 b)
    {
        var dy = b.Y - a.Y;
        return dy > 0f || (dy == 0f && b.X - a.X < 0f);
    }

    private void Triangle(ImDrawVert v0, ImDrawVert v1, ImDrawVert v2, Texture texture, Vector4 clip)
    {
        var area = Edge(v0.Pos, v1.Pos, v2.Pos.X, v2.Pos.Y);
        if (area == 0f)
            return;

        if (area < 0f)
        {
            (v1, v2) = (v2, v1);
            area = -area;
        }

        var minX = Math.Max((int)MathF.Floor(MathF.Min(v0.Pos.X, MathF.Min(v1.Pos.X, v2.Pos.X))), Math.Max(0, (int)MathF.Floor(clip.X)));
        var minY = Math.Max((int)MathF.Floor(MathF.Min(v0.Pos.Y, MathF.Min(v1.Pos.Y, v2.Pos.Y))), Math.Max(0, (int)MathF.Floor(clip.Y)));
        var maxX = Math.Min((int)MathF.Ceiling(MathF.Max(v0.Pos.X, MathF.Max(v1.Pos.X, v2.Pos.X))), Math.Min(Width, (int)MathF.Ceiling(clip.Z)));
        var maxY = Math.Min((int)MathF.Ceiling(MathF.Max(v0.Pos.Y, MathF.Max(v1.Pos.Y, v2.Pos.Y))), Math.Min(Height, (int)MathF.Ceiling(clip.W)));
        if (minX >= maxX || minY >= maxY)
            return;

        var own0 = OwnsEdge(v1.Pos, v2.Pos);
        var own1 = OwnsEdge(v2.Pos, v0.Pos);
        var own2 = OwnsEdge(v0.Pos, v1.Pos);
        var c0 = Unpack(v0.Col);
        var c1 = Unpack(v1.Col);
        var c2 = Unpack(v2.Col);
        var inv = 1f / area;

        for (var y = minY; y < maxY; y++)
        {
            var py = y + 0.5f;
            if (py < clip.Y || py >= clip.W)
                continue;

            for (var x = minX; x < maxX; x++)
            {
                var px = x + 0.5f;
                if (px < clip.X || px >= clip.Z)
                    continue;

                var w0 = Edge(v1.Pos, v2.Pos, px, py);
                var w1 = Edge(v2.Pos, v0.Pos, px, py);
                var w2 = Edge(v0.Pos, v1.Pos, px, py);
                if (w0 < 0f || w1 < 0f || w2 < 0f)
                    continue;
                if ((w0 == 0f && !own0) || (w1 == 0f && !own1) || (w2 == 0f && !own2))
                    continue;

                w0 *= inv;
                w1 *= inv;
                w2 *= inv;

                var color = c0 * w0 + c1 * w1 + c2 * w2;
                var uv = v0.Uv * w0 + v1.Uv * w1 + v2.Uv * w2;
                color *= Sample(texture, uv);

                var alpha = color.W;
                if (alpha <= 0f)
                    continue;

                var i = (y * Width + x) * 3;
                pixels[i] = color.X * alpha + pixels[i] * (1f - alpha);
                pixels[i + 1] = color.Y * alpha + pixels[i + 1] * (1f - alpha);
                pixels[i + 2] = color.Z * alpha + pixels[i + 2] * (1f - alpha);
            }
        }
    }

    private static Vector4 Unpack(uint color)
    {
        return new Vector4((color & 0xFF) / 255f, ((color >> 8) & 0xFF) / 255f, ((color >> 16) & 0xFF) / 255f,
                           ((color >> 24) & 0xFF) / 255f);
    }

    private static Vector4 Sample(Texture texture, Vector2 uv)
    {
        var fx = Math.Clamp(uv.X * texture.Width - 0.5f, 0f, texture.Width - 1);
        var fy = Math.Clamp(uv.Y * texture.Height - 0.5f, 0f, texture.Height - 1);
        var x0 = (int)fx;
        var y0 = (int)fy;
        var x1 = Math.Min(texture.Width - 1, x0 + 1);
        var y1 = Math.Min(texture.Height - 1, y0 + 1);
        var tx = fx - x0;
        var ty = fy - y0;

        var top = Vector4.Lerp(Texel(texture, x0, y0), Texel(texture, x1, y0), tx);
        var bottom = Vector4.Lerp(Texel(texture, x0, y1), Texel(texture, x1, y1), tx);
        return Vector4.Lerp(top, bottom, ty);
    }

    private static Vector4 Texel(Texture texture, int x, int y)
    {
        var i = (y * texture.Width + x) * 4;
        return new Vector4(texture.Rgba[i] / 255f, texture.Rgba[i + 1] / 255f, texture.Rgba[i + 2] / 255f, texture.Rgba[i + 3] / 255f);
    }
}
