using System.Windows;
using SpaceLens.Controls;

namespace SpaceLens.Tests.ViewModels;

public sealed class SquarifiedLayoutTests
{
    [Fact]
    public void TilesFillTheAreaInProportionToTheirWeights()
    {
        var weights = new double[] { 600, 300, 60, 30, 10 };
        var bounds = new Rect(0, 0, 400, 250);

        var tiles = SquarifiedLayout.Arrange(weights, bounds);

        var total = weights.Sum();
        for (var i = 0; i < weights.Length; i++)
        {
            Assert.Equal(weights[i] / total * bounds.Width * bounds.Height, tiles[i].Width * tiles[i].Height, precision: 6);
            Assert.True(bounds.Contains(tiles[i].TopLeft) && tiles[i].Right <= bounds.Right + 1e-9 && tiles[i].Bottom <= bounds.Bottom + 1e-9);
        }
    }

    [Fact]
    public void TilesDoNotOverlap()
    {
        var tiles = SquarifiedLayout.Arrange([50, 40, 30, 20, 10, 5, 5], new Rect(0, 0, 300, 200));

        for (var i = 0; i < tiles.Length; i++)
        {
            for (var j = i + 1; j < tiles.Length; j++)
            {
                var overlap = Rect.Intersect(tiles[i], tiles[j]);
                Assert.True(overlap.IsEmpty || overlap.Width * overlap.Height < 1e-6, $"Tiles {i} and {j} overlap.");
            }
        }
    }

    [Fact]
    public void NothingToShowGivesEmptyTiles()
    {
        var tiles = SquarifiedLayout.Arrange([0, 0], new Rect(0, 0, 100, 100));

        Assert.All(tiles, tile => Assert.True(tile.IsEmpty || tile.Width * tile.Height == 0));
    }
}
