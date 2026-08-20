using Meddle.Utils.Export.Ue;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class UeBuildProvenanceTests
{
    [TestMethod]
    public void FromMetadata_AcceptsValidSha1Commit()
    {
        var commit = new string('a', 40);
        var provenance = UeBuildProvenance.FromMetadata(
            "1.2.3",
            commit,
            "true",
            "build-id");

        Assert.AreEqual("1.2.3", provenance.Version);
        Assert.AreEqual(commit, provenance.Commit);
        Assert.IsTrue(provenance.Dirty);
        Assert.AreEqual("build-id", provenance.BuildId);
    }

    [TestMethod]
    public void FromMetadata_AcceptsValidSha256Commit()
    {
        var commit = new string('b', 64);
        var provenance = UeBuildProvenance.FromMetadata(
            "1.2.3",
            commit,
            "false",
            "build-id");

        Assert.AreEqual(commit, provenance.Commit);
        Assert.IsFalse(provenance.Dirty);
    }

    [TestMethod]
    public void FromMetadata_RejectsGitStderrAsCommit()
    {
        var provenance = UeBuildProvenance.FromMetadata(
            "1.2.3",
            "fatal: not a git repository",
            "false",
            "build-id");

        Assert.IsNull(provenance.Commit);
    }

    [TestMethod]
    public void FromMetadata_RejectsEmptyOrWhitespaceCommit()
    {
        Assert.IsNull(UeBuildProvenance.FromMetadata("1.2.3", "", "false", "build-id").Commit);
        Assert.IsNull(UeBuildProvenance.FromMetadata("1.2.3", "   ", "false", "build-id").Commit);
    }

    [TestMethod]
    public void FromMetadata_LeavesMissingGitMetadataNull()
    {
        var provenance = UeBuildProvenance.FromMetadata(
            "1.2.3",
            null,
            null,
            "build-id");

        Assert.IsNull(provenance.Commit);
        Assert.IsNull(provenance.Dirty);
        Assert.AreEqual("build-id", provenance.BuildId);
    }

    [TestMethod]
    public void FromMetadata_DoesNotFabricateCommitOrDirtyFromInvalidValues()
    {
        var provenance = UeBuildProvenance.FromMetadata(
            "1.2.3",
            null,
            "not-a-bool",
            "build-id");

        Assert.IsNull(provenance.Commit);
        Assert.IsNull(provenance.Dirty);
    }

    [TestMethod]
    public void FromMetadata_EmptyDirtyValueRemainsUnknown()
    {
        var provenance = UeBuildProvenance.FromMetadata(
            "1.2.3",
            null,
            "",
            "build-id");

        Assert.IsNull(provenance.Dirty);
    }
}
