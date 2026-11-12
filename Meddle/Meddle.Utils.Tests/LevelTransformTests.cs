using System.Numerics;
using Meddle.Unreal;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class LevelTransformTests
{
    [TestMethod]
    [DataRow(1f)]
    [DataRow(-1f)]
    public void LocalMeshAndRelativeInstanceReproduceWorldPlacement(float sign)
    {
        var world = Matrix4x4.CreateScale(sign * 2, 3, 4) * Matrix4x4.CreateRotationY(.7f)
            * Matrix4x4.CreateTranslation(20, -5, 12);
        var point = new Vector3(1, 2, 3);
        var origin = Coordinates.Position(new Vector3(10, 3, 5));
        var instance = Coordinates.InstanceMatrix(world);
        instance.Translation -= origin;
        var actual = Vector3.Transform(Coordinates.Position(point), instance) + origin;
        var expected = Coordinates.Position(Vector3.Transform(point, world));
        Assert.IsTrue(Vector3.Distance(actual, expected) < .001f);
        Assert.AreEqual(sign < 0, instance.GetDeterminant() < 0);
        Assert.AreEqual(Vector3.Zero, Coordinates.Position(Vector3.Zero));
    }
}
