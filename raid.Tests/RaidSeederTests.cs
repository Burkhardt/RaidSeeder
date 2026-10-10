using OsLib;
using RaiDiagram;

namespace RaidSeeder.Tests;

public sealed class RaidSeederTests : IDisposable
{
	private readonly RaiPath root = Os.TempDir / "RAIkeep" / "raid-seeder-tests" / Guid.NewGuid().ToString("N");

	public RaidSeederTests()
	{
		Cleanup();
		root.mkdir();
	}

	public void Dispose() => Cleanup();

	[Fact]
	public void MissingConfigurationDiagnostic_DirectsOperatorToAmafu()
	{
		Assert.Equal(
			"RAIkeep configuration was not found at '~/.config/RAIkeep.json5'. " +
			"Run 'amafu init' to detect cloud providers and create it.",
			Program.MissingConfigurationDiagnostic());
	}

	[Fact]
	public void Version_IsSuiteVersion()
	{
		var output = new StringWriter();
		var exitCode = Program.Run(["--version"], output, new StringWriter());
		Assert.Equal(0, exitCode);
		Assert.Equal("raid v4.5.8", output.ToString().Trim());
	}

	[Fact]
	public void MisplacedVerb_FailsFastWithActionableCorrection()
	{
		var output = new StringWriter();
		var error = new StringWriter();
		var exitCode = Program.Run(
			["-n", "refresh", "SignContract", "-c", "OneDrive", "-r", "AIA/Image", "-t", "AfricaStage"],
			output,
			error);

		Assert.Equal(2, exitCode);
		Assert.Empty(output.ToString());
		Assert.Contains("Subcommand 'refresh' must be the first parameter", error.ToString());
		Assert.Contains(
			"raid refresh -n SignContract -c OneDrive -r AIA/Image -t AfricaStage",
			error.ToString());
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void VersionFlag_TakesImmediatePrecedence(bool versionFirst)
	{
		var args = versionFirst ? new[] { "-v", "export" } : new[] { "export", "-v" };
		var output = new StringWriter();
		var error = new StringWriter();
		var exitCode = Program.Run(args, output, error);

		Assert.Equal(0, exitCode);
		Assert.Equal("raid v4.5.8", output.ToString().Trim());
		Assert.Empty(error.ToString());
	}

	[Fact]
	public void Help_UsesSuitePresentationAndDocumentsManagerAddressing()
	{
		var output = new StringWriter();
		var exitCode = Program.Run(["--help"], output, new StringWriter());
		var help = output.ToString();

		Assert.Equal(0, exitCode);
		Assert.Contains("RAI Diagram Seeder & Manager", help);
		Assert.Contains("import, export, refresh, validate", help);
		Assert.Contains("-c, --cloud", help);
		Assert.Contains("-r, --root", help);
		Assert.Contains("-a, --app", help);
		Assert.Contains("-t, --tenant", help);
		Assert.Contains("-p, --pathconv", help);
		Assert.Contains("install RaidSeeder; invoke it with raid", help);
		Assert.Contains("OneDrive", help);
		Assert.Contains("Dropbox", help);
		Assert.Contains("GoogleDrive", help);
		Assert.Contains("ICloudDrive", help);
		Assert.Contains("\uea74", help);
	}

	[Fact]
	public void Import_Cr036Fixture_WritesCanonicalRaidPumlAndHydratableSvg()
	{
		var output = new StringWriter();
		var exitCode = Program.Run(
			["import", "--puml", Fixture().FullName, "--out", root.FullPath],
			output,
			new StringWriter());

		Assert.Equal(0, exitCode);
		var raid = new RaidFile(root, "ChangeRequestWorkflow");
		var puml = new TextFile(root, "ChangeRequestWorkflow", "puml");
		var svg = new TextFile(root, "ChangeRequestWorkflow", "svg");
		Assert.True(raid.Exists());
		Assert.True(puml.Exists());
		Assert.True(svg.Exists());
		Assert.Equal(DiagramKind.Activity, raid.LoadManifest().Diagram.Kind);
		AimSvg.Validate(svg.ReadAllText());
		Assert.Contains(raid.FullName, output.ToString());
		Assert.Contains(puml.FullName, output.ToString());
		Assert.Contains(svg.FullName, output.ToString());
	}

	[Fact]
	public void Import_NameOverrideControlsOutputAndManifestIdentity()
	{
		var exitCode = Program.Run(
			["import", "--puml", Fixture().FullName, "--out", root.FullPath, "--name", "Workflow_AIA"],
			new StringWriter(),
			new StringWriter());

		Assert.Equal(0, exitCode);
		var raid = new RaidFile(root, "Workflow_AIA");
		Assert.Equal("Workflow_AIA", raid.LoadManifest().Diagram.Id);
		Assert.True(new TextFile(root, "Workflow_AIA", "svg").Exists());
	}

	[Fact]
	public void Import_ManagedAddressKeepsItemIdNumberAndNameExtSeparate()
	{
		var exitCode = Program.Run(
			[
				"import", "--puml", Fixture().FullName,
				"--root", root.FullPath, "--tenant", "AfricaStage",
				"--name", "SignContract", "--number", "2", "--name-ext", "UCD"
			],
			new StringWriter(),
			new StringWriter());

		Assert.Equal(0, exitCode);
		var set = new DiagramArtifactSet(root, "AfricaStage", "SignContract", "UCD", 2);
		Assert.Equal("SignContract", set.ItemId);
		Assert.Equal(2, set.ItemNumber);
		Assert.Equal("UCD", set.NameExt);
		Assert.Equal("SignContract_002_UCD", set.DiagramId);
		Assert.Contains("AfricaStage/SignCont/SignContra/SignContract_002_UCD.raid", set.RaidManifest.FullName);
		Assert.True(set.RaidManifest.Exists());
		Assert.True(set.PlantUmlSource.Exists());
		Assert.True(set.Svg.Exists());
		Assert.Equal(set.DiagramId, set.RaidManifest.LoadManifest().Diagram.Id);
	}

	[Fact]
	public void Export_PlantUmlRoundTripsToEquivalentSemanticManifest()
	{
		var set = ImportManaged("RoundTrip", "AD");
		var exportRoot = root / "exports";
		var exitCode = Program.Run(
			[
				"export", "RoundTrip", "--root", root.FullPath, "--tenant", "AIA",
				"--name-ext", "AD", "--format", "puml", "--out", exportRoot.FullPath
			],
			new StringWriter(),
			new StringWriter());

		Assert.Equal(0, exitCode);
		var exported = new TextFile(exportRoot, "RoundTrip_AD", "puml");
		Assert.True(exported.Exists());
		var reparsed = new PlantUmlModelImporter().Import(new StringReader(exported.ReadAllText())).Manifest;
		Assert.Equal(
			DiagramSemanticHasher.Compute(set.RaidManifest.LoadManifest()),
			DiagramSemanticHasher.Compute(reparsed));
	}

	[Fact]
	public void Export_PlainSvgContainsNoHydrationAttributes()
	{
		ImportManaged("PlainDiagram", "UCD");
		var output = new StringWriter();
		var exitCode = Program.Run(
			[
				"export", "PlainDiagram", "--root", root.FullPath, "--tenant", "AIA",
				"--name-ext", "UCD", "--format", "svg", "--svg-profile", "plain"
			],
			output,
			new StringWriter());

		Assert.Equal(0, exitCode);
		Assert.Contains("<svg", output.ToString());
		Assert.DoesNotContain("aim-", output.ToString(), StringComparison.Ordinal);
		AimSvg.Validate(output.ToString(), AimSvgProfile.Plain);
	}

	[Fact]
	public void Refresh_WhenDerivativesAreCurrentPerformsNoWrites()
	{
		var set = ImportManaged("CurrentDiagram", "AD");
		var pumlWrite = set.PlantUmlSource.LastWriteTimeUtc;
		var svgWrite = set.Svg.LastWriteTimeUtc;
		var output = new StringWriter();
		var exitCode = Program.Run(
			[
				"refresh", "CurrentDiagram", "--root", root.FullPath, "--tenant", "AIA",
				"--name-ext", "AD"
			],
			output,
			new StringWriter());

		Assert.Equal(0, exitCode);
		Assert.Contains("current: CurrentDiagram_AD; no files written", output.ToString());
		Assert.Equal(pumlWrite, set.PlantUmlSource.LastWriteTimeUtc);
		Assert.Equal(svgWrite, set.Svg.LastWriteTimeUtc);
	}

	[Fact]
	public void Validate_RejectsMalformedAimSvg()
	{
		var svg = new TextFile(root, "broken", "svg")
		{
			Lines = ["<svg xmlns=\"http://www.w3.org/2000/svg\" />"],
			Changed = true
		};
		svg.Save();
		var error = new StringWriter();
		var exitCode = Program.Run(["validate", svg.FullName], new StringWriter(), error);
		Assert.Equal(1, exitCode);
		Assert.Contains("contains no visible diagram nodes", error.ToString());
	}

	private TextFile Fixture() => new(new RaiPath(AppContext.BaseDirectory) / "Fixtures",
		"ChangeRequestWorkflow", "puml");

	private DiagramArtifactSet ImportManaged(string itemId, string nameExt)
	{
		var exitCode = Program.Run(
			[
				"import", "--puml", Fixture().FullName,
				"--root", root.FullPath, "--tenant", "AIA",
				"--name", itemId, "--name-ext", nameExt
			],
			new StringWriter(),
			new StringWriter());
		Assert.Equal(0, exitCode);
		return new DiagramArtifactSet(root, "AIA", itemId, nameExt);
	}

	private void Cleanup()
	{
		if (root.Exists())
			new RaiFile(root.Path).rmdir(depth: 8, deleteFiles: true);
	}
}
