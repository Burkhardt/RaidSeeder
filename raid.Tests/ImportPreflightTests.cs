using RaiDiagram;
using OsLib;

namespace RaidSeeder.Tests;

public sealed class ImportPreflightTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void InvalidImportLeavesExistingArtifactsAndDestinationUntouched(bool managed)
	{
		var root = new RaiFile(new RaiPath(Os.TempDir.FullPath), "raid-preflight-" + Guid.NewGuid().ToString("N")).FullName;
		new RaiPath(root).mkdir();
		try
		{
			var source = new RaiFile(new RaiPath(root), "Order.puml").FullName;
			new TextFile(source).WriteAllText("@startuml\nobject Order\nunsupported Other\n@enduml");
			var destination = new RaiFile(new RaiPath(root), "destination").FullName;
			new RaiPath(destination).mkdir();
			var sentinel = new RaiFile(new RaiPath(destination), "Order.raid").FullName;
			new TextFile(sentinel).WriteAllText("preserve");
			string[] addressing = managed ? ["--root", destination, "--tenant", "Customer"] : ["--out", destination];
			var output = new StringWriter();
			var error = new StringWriter();
			var rc = Program.Run(["import", "--puml", source, "--name", "Order", .. addressing], output, error);
			Assert.Equal(1, rc);
			Assert.Empty(output.ToString());
			Assert.Contains("[PUML002]", error.ToString());
			Assert.Contains(source + ":3:1", error.ToString());
			Assert.Equal("preserve", new TextFile(sentinel).ReadAllText());
			var contents = new RaiPath(destination);
			Assert.Empty(contents.EnumerateDirectories("*"));
			Assert.Equal(sentinel, Assert.Single(contents.EnumerateFiles("*")).FullName);
		}
		finally { new RaiPath(root).rmdir(deleteFiles: true); }
	}

	[Fact]
	public void ObjectImportWritesVisibleSlotsAndPresentationWarningsGoToStderr()
	{
		var root = new RaiFile(new RaiPath(Os.TempDir.FullPath), "raid-object-" + Guid.NewGuid().ToString("N")).FullName;
		new RaiPath(root).mkdir();
		try
		{
			var source = new RaiFile(new RaiPath(root), "Order.puml").FullName;
			new TextFile(source).WriteAllText("@startuml\nskinparam shadowing false\nobject Order { Number = 42 }\n@enduml");
			var output = new StringWriter();
			var error = new StringWriter();
			Assert.Equal(0, Program.Run(["import", "--puml", source, "--out", new RaiFile(new RaiPath(root), "out").FullName, "--domain", "Examples", "--name", "Order", "--name-ext", "OD"], output, error));
			Assert.Contains("[PUML101]", error.ToString());
			Assert.DoesNotContain("PUML101", output.ToString());
			var svg = new TextFile(new RaiFile((new RaiPath(root) / ("out")), "Order_OD.svg").FullName).ReadAllText();
			Assert.Contains("Number = 42", svg);
			AimSvg.Validate(svg);
		}
		finally { new RaiPath(root).rmdir(deleteFiles: true); }
	}

	[Fact]
	public void PlantUmlImport_RequiresAndPersistsDomainContextForEdgeStereotypes()
	{
		var root = new RaiFile(new RaiPath(Os.TempDir.FullPath), "raid-domain-" + Guid.NewGuid().ToString("N")).FullName;
		new RaiPath(root).mkdir();
		try
		{
			var source = new RaiFile(new RaiPath(root), "Order.puml").FullName;
			var destination = new RaiFile(new RaiPath(root), "out").FullName;
			new TextFile(source).WriteAllText("@startuml Order\nobject Order\nobject Invoice\nOrder --> Invoice : <<ReviewsInvoice>>\n@enduml");
			var missingDomainError = new StringWriter();
			Assert.Equal(1, Program.Run(["import", "--puml", source, "--out", destination], new StringWriter(), missingDomainError));
			Assert.Contains("requires --domain", missingDomainError.ToString());
			Assert.False(new RaiPath(destination).Exists());

			Assert.Equal(0, Program.Run(["import", "--puml", source, "--out", destination, "--domain", "Examples"],
				new StringWriter(), new StringWriter()));
			var manifest = new RaidFile(new RaiFile(new RaiPath(destination), "Order.raid").FullName).LoadManifest();
			Assert.Equal("Examples", manifest.Model.Domain);
			Assert.Equal("ReviewsInvoice", Assert.Single(manifest.Projection.Relationships).Stereotype);

			var managedRoot = new RaiFile(new RaiPath(root), "managed").FullName;
			Assert.Equal(0, Program.Run(["import", "--puml", source, "--root", managedRoot, "--tenant", "Examples"],
				new StringWriter(), new StringWriter()));
			var managedFile = Assert.Single(new RaiPath(managedRoot).EnumerateFiles("*.raid", recursive: true));
			var managedManifest = new RaidFile(managedFile.FullName).LoadManifest();
			Assert.Equal("Examples", managedManifest.Model.Domain);
			Assert.Equal("ReviewsInvoice", Assert.Single(managedManifest.Projection.Relationships).Stereotype);
		}
		finally { new RaiPath(root).rmdir(deleteFiles: true); }
	}
}
