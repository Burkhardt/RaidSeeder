using System.Reflection;
using OsLib;
using RaiDiagram;
using RaiImage;

namespace RaidSeeder;

public static class Program
{
	private const int HelpOptionWidth = 25;
	private static readonly string[] Commands = ["import", "import-xmi", "import-otw", "export", "refresh", "validate"];
	private static readonly string[] GlobalSwitches =
	[
		"-h", "--help", "-v", "--version", "-n", "--nologo", "-d", "--debug"
	];

	public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

	public static int Run(string[] args, TextWriter output, TextWriter error)
	{
		ArgumentNullException.ThrowIfNull(args);
		ArgumentNullException.ThrowIfNull(output);
		ArgumentNullException.ThrowIfNull(error);
		try
		{
			if (Has(args, "-v", "--version"))
			{
				output.WriteLine($"raid v{Version()}");
				return 0;
			}
			if (args.Length == 0)
			{
				WriteHelp(output, noLogo: false);
				return 0;
			}

			var command = args[0];
			if (Has(args, "-h", "--help"))
			{
				WriteHelp(output, Has(args, "-n", "--nologo"),
					Commands.Contains(command, StringComparer.Ordinal) ? command : null);
				return 0;
			}
			if (CliVerbDispatch.DetectMisplacedVerb("raid", args, Commands) is { } diagnostic)
			{
				error.WriteLine(diagnostic.Message);
				return 2;
			}

			return command switch
			{
				"import" => Import(args[1..], output, error),
				"import-xmi" or "import-otw" => ImportXmi(args[1..], output),
				"export" => Export(args[1..], output),
				"refresh" => Refresh(args[1..], output),
				"validate" => Validate(args[1..], output),
				_ => Fail(error, $"Unknown command '{command}'. Run raid --help.")
			};
		}
		catch (Exception exception)
		{
			error.WriteLine($"raid: {(exception is UnsupportedDiagramConstructException or DiagramRenderingException ? "[RAID201] " : string.Empty)}{exception.Message}");
			return 1;
		}
	}

	private static int Import(string[] args, TextWriter output, TextWriter error)
	{
		var allowedValues = new HashSet<string>(StringComparer.Ordinal)
		{
			"--puml", "--out", "--name", "--name-ext", "--number",
			"-c", "--cloud", "-r", "--root", "-a", "--app",
			"-t", "--tenant", "--subscriber", "-p", "--pathconv"
		};
		EnsureKnown(args, allowedValues, GlobalSwitches);
		var sourceName = Value(args, "--puml")
			?? throw new ArgumentException("import requires --puml <diagram.puml>.");
		var source = new TextFile(sourceName);
		if (!source.Exists())
			throw new RaiPathNotFoundException(
				$"The PlantUML source does not exist: {source.FullName}", source.FullName);

		var importer = new PlantUmlModelImporter();
		if (!importer.CanImport(source.FullName))
			throw new ArgumentException($"Unsupported import source '{source.FullName}'. Expected a .puml file.");
		var imported = importer.Import(new StringReader(source.ReadAllText()), source.FullName);
		foreach (var diagnostic in imported.Diagnostics) error.WriteLine(diagnostic);
		var manifest = imported.Manifest;
		return WriteImported(args, output, manifest, source.Path);
	}

	private static int ImportXmi(string[] args, TextWriter output)
	{
		var allowed = ManagedValueOptions().Concat(["--out", "--name", "--diagram"]).ToHashSet(StringComparer.Ordinal);
		var positionals = EnsureKnown(args, allowed, GlobalSwitches.Concat(["--list-diagrams"]));
		if (positionals.Count != 1) throw new ArgumentException("import-xmi requires exactly one .xmi file.");
		var file = new TextFile(positionals[0]);
		var importer = new XmiDeploymentImporter();
		if (Has(args, "--list-diagrams"))
		{
			foreach (var info in importer.ListDiagrams(file.FullName))
				output.WriteLine($"{info.Id}\t{info.Type}\t{info.Name}");
			return 0;
		}
		var manifest = importer.Import(file.FullName, Value(args, "--diagram")).Manifest;
		return WriteImported(args, output, manifest, file.Path);
	}

	private static int WriteImported(string[] args, TextWriter output, DiagramManifest manifest, RaiPath sourcePath)
	{
		var itemId = Value(args, "--name") ?? manifest.Diagram.Id;
		var nameExt = Value(args, "--name-ext") ?? string.Empty;
		var number = Number(args);
		ValidateIdentity(itemId, nameExt);
		var diagramId = DiagramId(itemId, number, nameExt);
		manifest.Diagram.Id = diagramId;
		manifest.Model.ModelId = diagramId;

		var outName = Value(args, "--out");
		var rootName = Value(args, "-r", "--root");
		var appName = Value(args, "-a", "--app");
		EnsureDestinationChoice(outName, rootName, appName);
		var manager = new DiagramArtifactManager();
		// Derive every artifact before a managed manifest or destination can be written.
		_ = manager.Derive(manifest);
		if (!string.IsNullOrWhiteSpace(rootName) || !string.IsNullOrWhiteSpace(appName))
		{
			var address = ResolveAddress(args);
			var artifacts = new DiagramArtifactSet(
				address.ImageTreeRoot,
				address.Tenant,
				itemId,
				nameExt,
				number,
				address.Convention);
			artifacts.RaidManifest.SaveManifest(manifest);
			manager.Refresh(artifacts);
			output.WriteLine(artifacts.RaidManifest.FullName);
			output.WriteLine(artifacts.PlantUmlSource.FullName);
			output.WriteLine(artifacts.Svg.FullName);
			return 0;
		}

		var destination = !string.IsNullOrWhiteSpace(outName) ? new RaiPath(outName) : sourcePath;
		foreach (var file in manager.Write(destination, diagramId, manifest))
			output.WriteLine(file.FullName);
		return 0;
	}

	private static int Export(string[] args, TextWriter output)
	{
		var allowedValues = ManagedValueOptions()
			.Concat(["--format", "--svg-profile", "--out"])
			.ToHashSet(StringComparer.Ordinal);
		var positionals = EnsureKnown(args, allowedValues, GlobalSwitches);
		if (positionals.Count != 1)
			throw new ArgumentException("export requires exactly one ItemId.");
		var identity = Identity(args, positionals[0]);
		var address = ResolveAddress(args);
		var artifacts = Artifacts(address, identity);
		var format = ParseFormat(Value(args, "--format") ?? "all");
		var profile = ParseProfile(Value(args, "--svg-profile") ?? "hydratable");
		var outName = Value(args, "--out");
		var manager = new DiagramArtifactManager();
		if (!string.IsNullOrWhiteSpace(outName))
		{
			foreach (var file in manager.Export(artifacts, new RaiPath(outName), format, profile))
				output.WriteLine(file.FullName);
			return 0;
		}
		if (format == DiagramArtifactFormat.All)
			throw new ArgumentException("export --format all requires --out <directory>.");
		var manifest = artifacts.RaidManifest.Exists()
			? artifacts.RaidManifest.LoadManifest()
			: throw new RaiPathNotFoundException(
				$"The authoritative diagram manifest does not exist: {artifacts.RaidManifest.FullName}",
				artifacts.RaidManifest.FullName);
		var derived = manager.Derive(manifest, profile);
		output.Write(format switch
		{
			DiagramArtifactFormat.Raid => derived.Raid,
			DiagramArtifactFormat.PlantUml => derived.PlantUml,
			DiagramArtifactFormat.Svg => derived.Svg,
			_ => throw new ArgumentOutOfRangeException(nameof(format))
		});
		return 0;
	}

	private static int Refresh(string[] args, TextWriter output)
	{
		var allowedValues = ManagedValueOptions()
			.Concat(["--svg-profile"])
			.ToHashSet(StringComparer.Ordinal);
		var positionals = EnsureKnown(args, allowedValues, GlobalSwitches);
		if (positionals.Count != 1)
			throw new ArgumentException("refresh requires exactly one ItemId.");
		var identity = Identity(args, positionals[0]);
		var artifacts = Artifacts(ResolveAddress(args), identity);
		var result = new DiagramArtifactManager().Refresh(
			artifacts,
			ParseProfile(Value(args, "--svg-profile") ?? "hydratable"));
		output.WriteLine(result.Changed
			? $"refreshed: {result.DiagramId}; puml={result.PlantUmlWritten}; svg={result.SvgWritten}"
			: $"current: {result.DiagramId}; no files written");
		return 0;
	}

	private static int Validate(string[] args, TextWriter output)
	{
		var positionals = EnsureKnown(args, new HashSet<string>(StringComparer.Ordinal), GlobalSwitches);
		if (positionals.Count != 1)
			throw new ArgumentException("validate requires exactly one .raid, .puml, or .svg path.");
		var file = new TextFile(positionals[0]);
		if (!file.Exists())
			throw new RaiPathNotFoundException(
				$"The validation target does not exist: {file.FullName}", file.FullName);
		var contents = file.ReadAllText();
		if (file.Ext.Equals("raid", StringComparison.OrdinalIgnoreCase))
			RaidJson5.Parse(contents);
		else if (file.Ext.Equals("puml", StringComparison.OrdinalIgnoreCase))
			new PlantUmlModelImporter().Import(new StringReader(contents), file.FullName);
		else if (file.Ext.Equals("svg", StringComparison.OrdinalIgnoreCase))
			AimSvg.Validate(contents, contents.Contains("aim-node=", StringComparison.Ordinal)
				? AimSvgProfile.Hydratable
				: AimSvgProfile.Plain);
		else
			throw new ArgumentException("validate supports .raid, .puml, and .svg files.");
		output.WriteLine($"valid: {file.FullName}");
		return 0;
	}

	private static DiagramArtifactSet Artifacts(ManagedAddress address, ArtifactIdentity identity)
		=> new(
			address.ImageTreeRoot,
			address.Tenant,
			identity.ItemId,
			identity.NameExt,
			identity.Number,
			address.Convention);

	private static ArtifactIdentity Identity(string[] args, string itemId)
	{
		var nameExt = Value(args, "--name-ext") ?? string.Empty;
		ValidateIdentity(itemId, nameExt);
		return new ArtifactIdentity(itemId, Number(args), nameExt);
	}

	private static ManagedAddress ResolveAddress(string[] args)
	{
		var root = Value(args, "-r", "--root");
		var app = Value(args, "-a", "--app");
		if (!string.IsNullOrWhiteSpace(root) && !string.IsNullOrWhiteSpace(app))
			throw new ArgumentException("Use only one of -r/--root or -a/--app.");
		var selected = root ?? app
			?? throw new ArgumentException("One of -r/--root or -a/--app is required.");
		var tenant = Value(args, "-t", "--tenant", "--subscriber")
			?? throw new ArgumentException("ImageTree addressing requires -t/--tenant <name>.");
		ValidateSegment(tenant, "tenant");

		var requestedCloud = Value(args, "-c", "--cloud");
		var cloud = EffectiveCloud(requestedCloud, selected);
		RaiPath resolved;
		if (cloud is not null)
		{
			string? cloudDirectory = Os.Config?.Cloud?[cloud];
			if (string.IsNullOrWhiteSpace(cloudDirectory))
				throw new InvalidOperationException(
					$"The requested cloud provider '{cloud}' is missing or empty in {Os.DefaultConfigFileLocation}.");
			resolved = new RaiPath(cloudDirectory) / new RaiRelPath(selected);
		}
		else
			resolved = new RaiPath(selected);
		if (app is not null)
			resolved /= "Image";

		return new ManagedAddress(resolved, tenant, ParseConvention(Value(args, "-p", "--pathconv")));
	}

	private static string? EffectiveCloud(string? requested, string root)
	{
		if (!string.IsNullOrWhiteSpace(requested))
		{
			if (!Os.IsConfigLoaded)
				throw new ArgumentException(MissingConfigurationDiagnostic());
			var configured = ConfiguredCloudProviders();
			return configured.FirstOrDefault(item => string.Equals(item, requested, StringComparison.OrdinalIgnoreCase))
				?? throw new ArgumentException(
					$"The cloud provider '{requested}' is not configured. Available: {string.Join(", ", configured)}.");
		}
		if (root == ".")
			return null;
		try
		{
			_ = new RaiRelPath(root);
		}
		catch (ArgumentException)
		{
			return null;
		}

		if (!Os.IsConfigLoaded)
			throw new ArgumentException(MissingConfigurationDiagnostic());
		return ConfiguredCloudProviders().FirstOrDefault();
	}

	internal static string MissingConfigurationDiagnostic()
		=> $"RAIkeep configuration was not found at '{Os.DefaultConfigFileLocation}'. " +
			"Run 'amafu init' to detect cloud providers and create it.";

	private static string[] ConfiguredCloudProviders()
	{
		try
		{
			dynamic? order = Os.Config?.DefaultCloudOrder;
			dynamic? cloud = Os.Config?.Cloud;
			if (order is null || cloud is null) return [];
			var configured = new List<string>();
			foreach (var value in order)
			{
				string? name = value?.ToString();
				if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(cloud[name]?.ToString()))
					configured.Add(name!);
			}
			return configured.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		}
		catch
		{
			return [];
		}
	}

	private static IReadOnlyList<string> ManagedValueOptions() =>
	[
		"-c", "--cloud", "-r", "--root", "-a", "--app",
		"-t", "--tenant", "--subscriber", "-p", "--pathconv",
		"--name-ext", "--number"
	];

	private static List<string> EnsureKnown(
		string[] args,
		IReadOnlySet<string> valueOptions,
		IEnumerable<string> switchOptions)
	{
		var switches = switchOptions.ToHashSet(StringComparer.Ordinal);
		var positionals = new List<string>();
		for (var index = 0; index < args.Length; index++)
		{
			var token = args[index];
			if (!token.StartsWith("-", StringComparison.Ordinal))
			{
				positionals.Add(token);
				continue;
			}
			if (valueOptions.Contains(token))
			{
				if (++index >= args.Length || args[index].StartsWith("-", StringComparison.Ordinal))
					throw new ArgumentException($"{token} requires a value.");
				continue;
			}
			if (!switches.Contains(token))
				throw new ArgumentException($"Unknown option '{token}'.");
		}
		return positionals;
	}

	private static string? Value(string[] args, params string[] options)
	{
		var matches = args.Select((value, index) => (value, index))
			.Where(item => options.Contains(item.value, StringComparer.Ordinal))
			.ToArray();
		if (matches.Length > 1)
			throw new ArgumentException($"Use only one of {string.Join('/', options)}.");
		if (matches.Length == 0) return null;
		var found = matches[0].index;
		if (found + 1 >= args.Length || args[found + 1].StartsWith("-", StringComparison.Ordinal))
			throw new ArgumentException($"{args[found]} requires a value.");
		return args[found + 1];
	}

	private static bool Has(string[] args, params string[] options)
		=> args.Any(argument => options.Contains(argument, StringComparer.Ordinal));

	private static int Number(string[] args)
	{
		var value = Value(args, "--number");
		if (value is null) return ItemTreeTextFile.NoItemNumber;
		if (!int.TryParse(value, out var number) || number < 0)
			throw new ArgumentException("--number requires a non-negative integer.");
		return number;
	}

	private static PathConventionType ParseConvention(string? value)
	{
		if (string.IsNullOrWhiteSpace(value)) return PathConventionType.ItemIdTree8x2;
		if (int.TryParse(value, out var number) && number is >= 1 and <= 4)
			return (PathConventionType)(number - 1);
		if (Enum.TryParse<PathConventionType>(value, ignoreCase: true, out var convention))
			return convention;
		throw new ArgumentException("--pathconv accepts 1, 2, 3, 4, or a PathConventionType name.");
	}

	private static DiagramArtifactFormat ParseFormat(string value) => value.ToLowerInvariant() switch
	{
		"raid" => DiagramArtifactFormat.Raid,
		"puml" => DiagramArtifactFormat.PlantUml,
		"svg" => DiagramArtifactFormat.Svg,
		"all" => DiagramArtifactFormat.All,
		_ => throw new ArgumentException("--format accepts raid, puml, svg, or all.")
	};

	private static AimSvgProfile ParseProfile(string value) => value.ToLowerInvariant() switch
	{
		"hydratable" => AimSvgProfile.Hydratable,
		"plain" => AimSvgProfile.Plain,
		_ => throw new ArgumentException("--svg-profile accepts hydratable or plain.")
	};

	private static void EnsureDestinationChoice(string? output, string? root, string? app)
	{
		if (!string.IsNullOrWhiteSpace(root) && !string.IsNullOrWhiteSpace(app))
			throw new ArgumentException("Use only one of -r/--root or -a/--app.");
		if (!string.IsNullOrWhiteSpace(output)
			&& (!string.IsNullOrWhiteSpace(root) || !string.IsNullOrWhiteSpace(app)))
			throw new ArgumentException("--out is mutually exclusive with -r/--root and -a/--app.");
	}

	private static void ValidateIdentity(string itemId, string nameExt)
	{
		ValidateSegment(itemId, "ItemId");
		if (!string.IsNullOrWhiteSpace(nameExt)) ValidateSegment(nameExt, "NameExt");
	}

	private static void ValidateSegment(string value, string name)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Contains('/') || value.Contains('\\'))
			throw new ArgumentException($"{name} must be one plain path segment.", name);
	}

	private static string DiagramId(string itemId, int number, string nameExt)
	{
		var stem = number == ItemTreeTextFile.NoItemNumber ? itemId : $"{itemId}_{number:D3}";
		return string.IsNullOrWhiteSpace(nameExt) ? stem : $"{stem}_{nameExt}";
	}

	private static string Version()
	{
		var informational = Assembly.GetExecutingAssembly()
			.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
		return (informational ?? "0.0.0").Split('+')[0];
	}

	private static int Fail(TextWriter error, string message)
	{
		error.WriteLine($"raid: {message}");
		return 2;
	}

	private static void WriteHelp(TextWriter output, bool noLogo, string? command = null)
	{
		if (!noLogo)
		{
			output.WriteLine($"{Icons.Banner} ─────────────────────────────");
			output.WriteLine($"{Icons.Info} RAI Diagram Seeder & Manager");
			output.WriteLine($"{Icons.Banner} ─────────────────────────────");
		}
		if (command is not null)
		{
			WriteCommandHelp(output, command);
			return;
		}
		output.WriteLine(HelpLine("Commands", Icons.Info, "import, export, refresh, validate"));
		output.WriteLine("  raid import --puml <file> [--out <dir> | ((-r|--root) <dir>|(-a|--app) <dir>) (-t|--tenant) <name>]");
		output.WriteLine("  raid import-xmi <file.xmi> [--diagram <id|name>] [--list-diagrams] [--out <dir>] [--name <ItemId>]");
		output.WriteLine("  raid import-otw is an alias for import-xmi.");
		output.WriteLine("  raid export <ItemId> ((-r|--root) <dir>|(-a|--app) <dir>) (-t|--tenant) <name> --format <raid|puml|svg|all> [--out <dir>]");
		output.WriteLine("  raid refresh <ItemId> ((-r|--root) <dir>|(-a|--app) <dir>) (-t|--tenant) <name>");
		output.WriteLine("  raid validate <diagram.raid|diagram.puml|diagram.svg>");
		output.WriteLine(HelpLine("-h, --help", Icons.Help, "print command and option help"));
		output.WriteLine(HelpLine("-v, --version", Icons.Info, "print raid and synchronized suite version"));
		output.WriteLine(HelpLine("-n, --nologo", Icons.NoBanner, "do not display the banner"));
		output.WriteLine(HelpLine("-d, --debug", Icons.Runner, "enable diagnostic output"));
		output.WriteLine(HelpLine("-c, --cloud", DefaultCloudIcon(), CloudDescription()));
		output.WriteLine(HelpLine("-r, --root", Icons.Folder, "exact ImageTree root; alternative to -a/--app"));
		output.WriteLine(HelpLine("-a, --app", Icons.Folder, "application root; Image is appended"));
		output.WriteLine(HelpLine("-t, --tenant", Icons.Folder, "subscriber/tenant; --subscriber is an alias"));
		output.WriteLine(HelpLine("-p, --pathconv", Icons.Number3, "1 CanonicalByName, 2 ItemIdTree3x3, 3 ItemIdTree8x2 (default), 4 Flat"));
		output.WriteLine(HelpLine("--name-ext", Icons.File, "optional NameExt kept separate from ItemId"));
		output.WriteLine(HelpLine("--number", Icons.File, "optional artifact number before NameExt"));
		output.WriteLine(HelpLine("--svg-profile", Icons.Info, "hydratable (default) or plain"));
		output.WriteLine(HelpLine("Package", Icons.Info, "install RaidSeeder; invoke it with raid"));
		output.WriteLine("Examples:");
		output.WriteLine("  raid import --puml Workflow.puml --out artifacts --name Workflow");
		output.WriteLine("  raid refresh Workflow -c OneDrive --app AIA -t nomsa --name-ext AD");
	}

	private static void WriteCommandHelp(TextWriter output, string command)
	{
		output.WriteLine(command switch
		{
			"import" => "Usage: raid import --puml <file> [--name <ItemId>] [--name-ext <value>] [--number <n>] [--out <dir> | managed address]",
			"import-xmi" or "import-otw" => "Usage: raid import-xmi <file.xmi> [--diagram <id|name>] [--list-diagrams] [--name <ItemId>] [--out <dir> | managed address]",
			"export" => "Usage: raid export <ItemId> <managed address> [--name-ext <value>] [--number <n>] --format <raid|puml|svg|all> [--svg-profile <hydratable|plain>] [--out <dir>]",
			"refresh" => "Usage: raid refresh <ItemId> <managed address> [--name-ext <value>] [--number <n>] [--svg-profile <hydratable|plain>]",
			_ => "Usage: raid validate <diagram.raid|diagram.puml|diagram.svg>"
		});
		if (command is "import" or "export" or "refresh")
		{
			output.WriteLine(HelpLine("managed address", Icons.Folder,
				"((-r|--root) <dir>|(-a|--app) <dir>) (-t|--tenant) <name> [-c|--cloud <provider>] [-p|--pathconv <1|2|3|4>]"));
		}
		output.WriteLine(command switch
		{
			"import" => "Example: raid import --puml Workflow.puml -c OneDrive --app AIA -t nomsa --name Workflow --name-ext AD",
			"export" => "Example: raid export Workflow -c OneDrive --app AIA -t nomsa --name-ext AD --format svg --svg-profile plain --out export",
			"refresh" => "Example: raid refresh Workflow -c OneDrive --app AIA -t nomsa --name-ext AD",
			_ => "Example: raid validate Workflow_AD.raid"
		});
	}

	private static string CloudDescription()
	{
		var providers = ConfiguredCloudProviders();
		if (providers.Length == 0)
			providers = ["OneDrive", "Dropbox", "GoogleDrive", "ICloudDrive"];
		return string.Join(", ", providers.Select((provider, index) =>
			$"{CloudIcon(provider, index + 1)} {provider}{(index == 0 ? " (default)" : string.Empty)}"));
	}

	private static string DefaultCloudIcon()
	{
		var providers = ConfiguredCloudProviders();
		return providers.Length == 0 ? Icons.OneDrive : CloudIcon(providers[0], 1);
	}

	private static string CloudIcon(string provider, int fallbackNumber) => provider.ToLowerInvariant() switch
	{
		"onedrive" => Icons.OneDrive,
		"dropbox" => Icons.Dropbox,
		"googledrive" => Icons.GoogleDrive,
		"iclouddrive" => Icons.ICloudDrive,
		_ => fallbackNumber is > 0 and <= 9 ? Icons.NumberBoxOutlines[fallbackNumber - 1] : $"({fallbackNumber})"
	};

	private static string HelpLine(string option, string icon, string description)
		=> $"{option.PadRight(HelpOptionWidth)}\t{icon}\t{description}{Icons.WidthCompensation}";

	private sealed record ArtifactIdentity(string ItemId, int Number, string NameExt);
	private sealed record ManagedAddress(RaiPath ImageTreeRoot, string Tenant, PathConventionType Convention);

	private static class Icons
	{
		public const string Info = "\uea74";
		public const string Help = "\uf059";
		public const string File = "\uea7b";
		public const string Folder = "\uea83";
		public const string Banner = "\ueb1e";
		public const string NoBanner = "\ueb24";
		public const string Runner = "\uf04b";
		public const string OneDrive = "\U000F0C15";
		public const string Dropbox = "\U000F0BF4";
		public const string GoogleDrive = "\U000F0BFD";
		public const string ICloudDrive = "\U000F0C03";
		public const string Number3 = "\U000F03AA";
		public const string WidthCompensation = "  ";
		public static readonly string[] NumberBoxOutlines =
		[
			"\U000F03A6", "\U000F03A9", "\U000F03AC", "\U000F03AE", "\U000F03B0",
			"\U000F03B5", "\U000F03B8", "\U000F03BB", "\U000F03BE"
		];
	}
}
