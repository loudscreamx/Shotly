using System;

namespace Shotly.Core;

/// <summary>
/// Прямоугольник в физических пикселях виртуального рабочего стола.
/// Все координаты внутри приложения хранятся только в этом виде;
/// в DIP (единицы WPF) переводим только при отрисовке конкретного окна.
/// </summary>
public readonly record struct PxRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public long Area => IsEmpty ? 0 : (long)Width * Height;

    public static PxRect FromPoints(int x1, int y1, int x2, int y2)
    {
        int left = Math.Min(x1, x2), top = Math.Min(y1, y2);
        return new PxRect(left, top, Math.Abs(x2 - x1), Math.Abs(y2 - y1));
    }

    public static PxRect FromLtrb(int left, int top, int right, int bottom) =>
        new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));

    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    public PxRect Intersect(PxRect other) =>
        FromLtrb(Math.Max(X, other.X), Math.Max(Y, other.Y),
                 Math.Min(Right, other.Right), Math.Min(Bottom, other.Bottom));

    public PxRect Offset(int dx, int dy) => new(X + dx, Y + dy, Width, Height);

    /// <summary>Сдвигает прямоугольник внутрь <paramref name="bounds"/>, не меняя размера (если влезает).</summary>
    public PxRect ClampInside(PxRect bounds)
    {
        int w = Math.Min(Width, bounds.Width), h = Math.Min(Height, bounds.Height);
        int x = Math.Clamp(X, bounds.X, bounds.Right - w);
        int y = Math.Clamp(Y, bounds.Y, bounds.Bottom - h);
        return new PxRect(x, y, w, h);
    }

    public override string ToString() => $"{Width} × {Height} @ ({X}, {Y})";
}
