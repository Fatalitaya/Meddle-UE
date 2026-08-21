using System.Text.Json;
using Meddle.Utils.Audit;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class RuntimeAuditSerializerTests
{
    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "meddle-audit-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static RuntimeAuditSession CreateSession()
    {
        var context = new RuntimeAuditCollectorContext(
            "0x1234",
            "Test Character",
            "Pc",
            0x1234,
            new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc));
        var collectors = new IRuntimeAuditCollector[]
        {
            new TestCollector(
                RuntimeAuditCategory.CharacterComposition,
                "DrawObject",
                new Dictionary<string, object?> { ["models"] = new object?[] { "c0101e0001_top" } }),
            new TestCollector(
                RuntimeAuditCategory.Material,
                "Material",
                new Dictionary<string, object?> { ["shaderPackage"] = "character.shpk" }),
            new TestCollector(
                RuntimeAuditCategory.Customize,
                "Human",
                new Dictionary<string, object?> { ["height"] = 50 })
        };
        return RuntimeAuditRunner.RunAll("A", context, collectors);
    }

    [TestMethod]
    public async Task WriteThenLoad_RoundTripsObservations()
    {
        var directory = CreateTempDirectory();
        try
        {
            var session = CreateSession();
            await RuntimeAuditSerializer.WriteAsync(session, directory);

            var loaded = await RuntimeAuditSerializer.LoadAsync(directory);
            Assert.IsNotNull(loaded);
            Assert.AreEqual("A", loaded!.CaptureId);
            Assert.AreEqual("0x1234", loaded.ActorId);
            Assert.AreEqual(3, loaded.Observations.Count);
            Assert.AreEqual(3, loaded.CollectorStatus.Count);
            Assert.IsTrue(loaded.Observations.All(o => !string.IsNullOrWhiteSpace(o.Time)));
            Assert.IsTrue(loaded.Observations.All(o => !string.IsNullOrWhiteSpace(o.Category)));
            Assert.IsTrue(loaded.Observations.All(o => !string.IsNullOrWhiteSpace(o.SourceType)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Load_MissingRuntimeJson_ReturnsNull()
    {
        var directory = CreateTempDirectory();
        try
        {
            var loaded = await RuntimeAuditSerializer.LoadAsync(directory);
            Assert.IsNull(loaded);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Load_MissingProjectionFiles_StillLoadsCanonicalSession()
    {
        var directory = CreateTempDirectory();
        try
        {
            var session = CreateSession();
            await RuntimeAuditSerializer.WriteAsync(session, directory);
            File.Delete(Path.Combine(directory, RuntimeAuditFormat.MaterialsJson));
            File.Delete(Path.Combine(directory, RuntimeAuditFormat.SkeletonsJson));

            var loaded = await RuntimeAuditSerializer.LoadAsync(directory);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(3, loaded!.Observations.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task AllDumpFiles_AreMarkedExperimentalAuditFormat()
    {
        var directory = CreateTempDirectory();
        try
        {
            await RuntimeAuditSerializer.WriteAsync(CreateSession(), directory);
            foreach (var fileName in RuntimeAuditFormat.DumpFiles)
            {
                var path = Path.Combine(directory, fileName);
                Assert.IsTrue(File.Exists(path), $"Missing dump file {fileName}");
                var json = await File.ReadAllTextAsync(path);
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                Assert.AreEqual(
                    RuntimeAuditFormat.FormatMarker,
                    root.GetProperty("format").GetString(),
                    $"File {fileName} must be marked experimental");
                Assert.AreEqual(
                    RuntimeAuditFormat.SchemaVersion,
                    root.GetProperty("schemaVersion").GetInt32(),
                    $"File {fileName} must carry the schema version");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task ProjectionFiles_ContainOnlyTheirCategories()
    {
        var directory = CreateTempDirectory();
        try
        {
            await RuntimeAuditSerializer.WriteAsync(CreateSession(), directory);

            var materials = await LoadProjectionCategories(directory, RuntimeAuditFormat.MaterialsJson);
            CollectionAssert.AreEquivalent(new[] { "Material" }, materials);

            var resources = await LoadProjectionCategories(directory, RuntimeAuditFormat.ResourcesJson);
            CollectionAssert.AreEquivalent(
                new[] { "CharacterComposition", "RuntimeTexture" },
                resources);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task MissingData_UnavailableObservation_SerializesWithoutValue()
    {
        var directory = CreateTempDirectory();
        try
        {
            var context = new RuntimeAuditCollectorContext(
                "0x1",
                "X",
                "Pc",
                1,
                DateTime.UtcNow);
            var session = RuntimeAuditRunner.RunAll(
                "missing",
                context,
                new IRuntimeAuditCollector[]
                {
                    new TestCollector(
                        RuntimeAuditCategory.SkeletonEvidence,
                        "Skeleton",
                        null,
                        RuntimeAuditAvailability.Unavailable,
                        "skeleton is null")
                });
            await RuntimeAuditSerializer.WriteAsync(session, directory);

            var loaded = await RuntimeAuditSerializer.LoadAsync(directory);
            Assert.IsNotNull(loaded);
            var observation = loaded!.Observations.Single();
            Assert.AreEqual(RuntimeAuditAvailability.Unavailable, observation.Availability);
            Assert.IsNull(observation.Value);
            StringAssert.Contains(observation.Error, "skeleton is null");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<string[]> LoadProjectionCategories(string directory, string fileName)
    {
        var json = await File.ReadAllTextAsync(Path.Combine(directory, fileName));
        using var document = JsonDocument.Parse(json);
        return document.RootElement
                       .GetProperty("categories")
                       .EnumerateArray()
                       .Select(element => element.GetString()!)
                       .ToArray();
    }

    private sealed class TestCollector : IRuntimeAuditCollector
    {
        private readonly string sourceType;
        private readonly object? value;
        private readonly RuntimeAuditAvailability availability;
        private readonly string? error;

        public TestCollector(
            RuntimeAuditCategory category,
            string sourceType,
            object? value,
            RuntimeAuditAvailability availability = RuntimeAuditAvailability.Available,
            string? error = null)
        {
            Category = category;
            this.sourceType = sourceType;
            this.value = value;
            this.availability = availability;
            this.error = error;
        }

        public RuntimeAuditCategory Category { get; }

        public RuntimeAuditCollectorResult Collect(RuntimeAuditCollectorContext context)
        {
            return new RuntimeAuditCollectorResult
            {
                Status = availability,
                Error = error,
                Observations =
                {
                    new RuntimeAuditObservation
                    {
                        Time = context.CapturedAtUtc.ToString("O"),
                        ActorId = context.ActorId,
                        Category = Category.ToString(),
                        SourceType = sourceType,
                        Value = value,
                        Availability = availability,
                        Error = error
                    }
                }
            };
        }
    }
}
