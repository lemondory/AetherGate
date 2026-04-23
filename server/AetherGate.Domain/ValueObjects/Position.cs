namespace AetherGate.Domain.ValueObjects;

public readonly record struct Position(float X, float Y)
{
    public float DistanceTo(Position other)
    {
        float dx = X - other.X;
        float dy = Y - other.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    public Position MoveToward(Position target, float speed)
    {
        float dx = target.X - X;
        float dy = target.Y - Y;
        float dist = MathF.Sqrt(dx * dx + dy * dy);

        if (dist <= speed) return target;

        float ratio = speed / dist;
        return new Position(X + dx * ratio, Y + dy * ratio);
    }

    public override string ToString() => $"({X:F1}, {Y:F1})";
}
