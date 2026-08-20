using Meddle.Utils.Export.Ue;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class UeResourceIdentityTests
{
    [TestMethod]
    public void Sha256_IsLowerHexAndStable()
    {
        var data = "meddle-ue"u8.ToArray();
        var first = UeResourceIdentity.GetSha256(data);
        var second = UeResourceIdentity.GetSha256(data);

        Assert.AreEqual(first, second);
        Assert.IsTrue(first.All(char.IsAsciiHexDigit));
        Assert.AreEqual(first, first.ToLowerInvariant());
    }

    [TestMethod]
    public void NormalizeExtension_AddsDotAndLowercases()
    {
        Assert.AreEqual(".tex", UeResourceIdentity.NormalizeExtension(".TEX"));
        Assert.AreEqual(".tex", UeResourceIdentity.NormalizeExtension("tex"));
        Assert.AreEqual(string.Empty, UeResourceIdentity.NormalizeExtension(null));
    }

    [TestMethod]
    public void ClassifyOrigin_RootedResolvedPathIsModded()
    {
        Assert.AreEqual(
            UeResourceOrigin.Modded,
            UeResourceIdentity.ClassifyOrigin(@"C:\mods\chara\test.mdl"));
        Assert.AreEqual(
            UeResourceOrigin.Game,
            UeResourceIdentity.ClassifyOrigin("chara/test.mdl"));
        Assert.AreEqual(
            UeResourceOrigin.Unknown,
            UeResourceIdentity.ClassifyOrigin(null));
        Assert.AreEqual(
            UeResourceOrigin.Game,
            UeResourceIdentity.ClassifyOrigin("|mod|chara/test.mdl"));
        Assert.AreEqual(
            UeResourceOrigin.Unknown,
            UeResourceIdentity.ClassifyOrigin("|broken"));
    }

    [TestMethod]
    public void HandlePrefix_DoesNotAffectPathClassification()
    {
        Assert.AreEqual(
            UeResourceOrigin.Modded,
            UeResourceIdentity.ClassifyOrigin(@"|mod|C:\mods\chara\test.mdl"));
        Assert.AreEqual(
            "chara/test.mdl",
            UeResourceIdentity.TrimHandlePrefix("|mod|chara/test.mdl"));
    }
}
