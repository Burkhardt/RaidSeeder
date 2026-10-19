using RaiDiagram;

namespace RaidSeeder.Tests;

public sealed class ImportPreflightTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidImportLeavesExistingArtifactsAndDestinationUntouched(bool managed)
    {
        var root = Path.Combine(Path.GetTempPath(), "raid-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "Order.puml");
            File.WriteAllText(source, "@startuml\nobject Order\nunsupported Other\n@enduml");
            var destination = Path.Combine(root, "destination");
            Directory.CreateDirectory(destination);
            var sentinel = Path.Combine(destination, "Order.raid");
            File.WriteAllText(sentinel, "preserve");
            string[] addressing = managed ? ["--root", destination, "--tenant", "Customer"] : ["--out", destination];
            var output = new StringWriter();
            var error = new StringWriter();
            var rc = Program.Run(["import", "--puml", source, "--name", "Order", .. addressing], output, error);
            Assert.Equal(1, rc);
            Assert.Empty(output.ToString());
            Assert.Contains("[PUML002]", error.ToString());
            Assert.Contains(source + ":3:1", error.ToString());
            Assert.Equal("preserve", File.ReadAllText(sentinel));
            Assert.Single(Directory.EnumerateFileSystemEntries(destination));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ObjectImportWritesVisibleSlotsAndPresentationWarningsGoToStderr()
    {
        var root = Path.Combine(Path.GetTempPath(), "raid-object-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "Order.puml");
            File.WriteAllText(source, "@startuml\nskinparam shadowing false\nobject Order { Number = 42 }\n@enduml");
            var output = new StringWriter();
            var error = new StringWriter();
            Assert.Equal(0, Program.Run(["import", "--puml", source, "--out", Path.Combine(root, "out"), "--domain", "Examples", "--name", "Order", "--name-ext", "OD"], output, error));
            Assert.Contains("[PUML101]", error.ToString());
            Assert.DoesNotContain("PUML101", output.ToString());
            var svg = File.ReadAllText(Path.Combine(root, "out", "Order_OD.svg"));
            Assert.Contains("Number = 42", svg);
            AimSvg.Validate(svg);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void PlantUmlImport_RequiresAndPersistsDomainContextForEdgeStereotypes()
    {
        var root = Path.Combine(Path.GetTempPath(), "raid-domain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "Order.puml");
            var destination = Path.Combine(root, "out");
            File.WriteAllText(source, "@startuml Order\nobject Order\nobject Invoice\nOrder --> Invoice : <<ReviewsInvoice>>\n@enduml");
            var missingDomainError = new StringWriter();
            Assert.Equal(1, Program.Run(["import", "--puml", source, "--out", destination], new StringWriter(), missingDomainError));
            Assert.Contains("requires --domain", missingDomainError.ToString());
            Assert.False(Directory.Exists(destination));

            Assert.Equal(0, Program.Run(["import", "--puml", source, "--out", destination, "--domain", "Examples"],
                new StringWriter(), new StringWriter()));
            var manifest = new RaidFile(Path.Combine(destination, "Order.raid")).LoadManifest();
            Assert.Equal("Examples", manifest.Model.Domain);
            Assert.Equal("ReviewsInvoice", Assert.Single(manifest.Projection.Relationships).Stereotype);

            var managedRoot = Path.Combine(root, "managed");
            Assert.Equal(0, Program.Run(["import", "--puml", source, "--root", managedRoot, "--tenant", "Examples"],
                new StringWriter(), new StringWriter()));
            var managedFile = Assert.Single(Directory.EnumerateFiles(managedRoot, "*.raid", SearchOption.AllDirectories));
            var managedManifest = new RaidFile(managedFile).LoadManifest();
            Assert.Equal("Examples", managedManifest.Model.Domain);
            Assert.Equal("ReviewsInvoice", Assert.Single(managedManifest.Projection.Relationships).Stereotype);
        }
        finally { Directory.Delete(root, true); }
    }
}
