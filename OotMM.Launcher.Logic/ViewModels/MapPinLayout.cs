namespace OotMM.Launcher.ViewModels;

/// <summary>Places map pins so none overlap, moving crowded ones as little as possible from their true spots.</summary>
public static class MapPinLayout
{
    private const double Gap = 4;
    private const double EdgeMargin = 3;

    public static (double X, double Y)[] Spread(
        IReadOnlyList<(double X, double Y, double Radius)> anchors, double width, double height)
    {
        var positions = anchors.Select(a => (a.X, a.Y)).ToArray();
        for (var pass = 0; pass < 120; pass++)
        {
            Push(anchors, positions, width, height, 0.05);
        }

        for (var pass = 0; pass < 300; pass++)
        {
            Push(anchors, positions, width, height, 0);
        }

        return positions;
    }

    private static void Push(
        IReadOnlyList<(double X, double Y, double Radius)> anchors, (double X, double Y)[] positions,
        double width, double height, double pull)
    {
        for (var i = 0; i < positions.Length; i++)
        {
            for (var j = i + 1; j < positions.Length; j++)
            {
                var dx = positions[j].X - positions[i].X;
                var dy = positions[j].Y - positions[i].Y;
                var distance = Math.Sqrt(dx * dx + dy * dy);
                var need = anchors[i].Radius + anchors[j].Radius + Gap;
                if (distance >= need)
                {
                    continue;
                }

                if (distance < 0.01)
                {
                    dx = Math.Cos(i * 2.4 + j);
                    dy = Math.Sin(i * 2.4 + j);
                    distance = 1;
                }

                var move = (need - distance) / 2;
                positions[i] = (positions[i].X - dx / distance * move, positions[i].Y - dy / distance * move);
                positions[j] = (positions[j].X + dx / distance * move, positions[j].Y + dy / distance * move);
            }
        }

        for (var i = 0; i < positions.Length; i++)
        {
            var edge = anchors[i].Radius + EdgeMargin;
            var x = positions[i].X + (anchors[i].X - positions[i].X) * pull;
            var y = positions[i].Y + (anchors[i].Y - positions[i].Y) * pull;
            positions[i] = (Math.Clamp(x, edge, Math.Max(edge, width - edge)),
                            Math.Clamp(y, edge, Math.Max(edge, height - edge)));
        }
    }
}
