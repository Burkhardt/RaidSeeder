# `raid` — RAI Diagram Seeder & Manager

## Latest Version 4.5.11

PlantUML diagram ingestion with typed edge stereotypes, deterministic SVG
output, and bounded deployment import.

Latest release notes: [RaidSeeder 4.5.11 release notes](https://github.com/Burkhardt/RAIkeep/blob/main/doc/releases/v4.x/RaidSeeder_RELEASE_NOTES_4.x.md#4511).

## Previous Versions

For release notes and changelogs of earlier releases, see the [RaidSeeder 4.x Release Notes](https://github.com/Burkhardt/RAIkeep/blob/main/doc/releases/v4.x/RaidSeeder_RELEASE_NOTES_4.x.md).

### Object and deployment import (CR052)

```plantuml
@startuml Orders
object "Order42 : Order" as order {
  Number = 42
  Details = { 'Currency': 'EUR', 'Total': '25.00' }
}
actor "Customer" as customer
customer --> order : places
@enduml
```

```bash
raid import --puml Orders.puml --out ./artifacts --domain Examples --name Orders --name-ext OD
```

ObjectProperties preserves ordered text slot values in schema 1.1 and renders
visible SVG compartments. Existing schema 1.0 remains readable. Deployment
PlantUML supports `node`, `cloud`, `component`, `database`, `artifact`, `folder`,
and `frame`, with resident containment and protocol labels. Use `_VD` via
`--name-ext VD` or `DistributionDiagramBuilder` for deployment models.

Parsing errors report source:line:column and codes PUML001–PUML005 before writes.
PUML101 warns about retained presentation hints not applied by the canvas renderer.
RAID201 identifies unsupported rendering. Unsupported constructs, includes, and
macros are rejected instead of silently dropping content or inventing classes.

PlantUML is the active interchange format. The retained, frozen deployment-only
Poseidon/OTW importer is available as `raid import-xmi file.xmi --list-diagrams`
and `raid import-xmi file.xmi --diagram <id-or-name> --out ./artifacts --name Servers`.
`import-otw` is an alias. It preserves drawing bounds and authored paths, without
claiming complete legacy visual fidelity or support for other XMI diagram families.


`RaidSeeder` installs the `raid` command. It manages the co-located diagram
artifact set in an ImageTree: authoritative `.raid`, derived `.puml`, and
derived `.svg`.

RaidSeeder preserves the authoritative semantic diagram manifest and produces
deterministic textual and visual projections without turning the generated
`.puml` or `.svg` files into competing sources of truth.

With CR064, `raid import` stores edge stereotypes as the separate
`DiagramRelationship.Stereotype` field, preserving their source spelling.
For an unmanaged `--out` import, specify `--domain <Domain>`; managed imports
use `--tenant` as the Domain. The generated PlantUML and SVG expose the same
stereotype while `.raid` remains the authoritative source.

**CLI tools:** use `raid` to import, export, refresh, and validate diagram
artifact families. Use [`amafu init`](https://github.com/Burkhardt/Amafu) to
detect cloud drives and create the shared RAIkeep configuration before using
cloud-backed addressing. `iorg` remains available for general ImageTree file
discovery and movement when no diagram refresh is required.

The command grammar is verb-first. A reserved verb placed after a modifier
(for example `raid -n refresh ...`) exits `2` before filesystem access and
prints the corrected `raid refresh -n ...` invocation. `-v`/`--version` takes
immediate precedence wherever it appears.

## Installation

### .NET global tool

```bash
dotnet tool install --global RaidSeeder
```

To update:

```bash
dotnet tool update --global RaidSeeder
```

*(Requires `~/.dotnet/tools` on your PATH.)*

### .NET tool in `/usr/local/bin` (Recommended for macOS & Linux)

Install directly to `/usr/local/bin` so no `$PATH` modification is needed:

```bash
sudo dotnet tool install RaidSeeder --tool-path /usr/local/bin
```

To update:

```bash
sudo dotnet tool update RaidSeeder --tool-path /usr/local/bin
```

> **Note:** `sudo` is only required during installation and updates to write to `/usr/local/bin`. Running the installed CLI command never requires `sudo` or elevated privileges.

The predecessor package was named `RaidCli`. Because both packages install the
same command, migrate explicitly:

```bash
dotnet tool uninstall --global RaidCli
dotnet tool install --global RaidSeeder --version 4.5.11
```

## Commands

```bash
# Import to an explicit flat directory.
raid import --puml ChangeRequestWorkflow.puml --out artifacts --domain Examples --name Workflow

# Import to OneDrive/AIA/Image/nomsa using ItemIdTree8x2.
raid import --puml Workflow.puml \
  -c OneDrive --app AIA -t nomsa \
  --name Workflow --name-ext AD

# Export current derivatives from the authoritative manifest.
raid export Workflow \
  -c OneDrive --app AIA -t nomsa \
  --name-ext AD --format all --out ./export

# Emit ordinary SVG without RAI/AIA hydration metadata.
raid export Workflow \
  -c OneDrive --app AIA -t nomsa \
  --name-ext AD --format svg --svg-profile plain --out ./export

# Refresh only missing or stale co-located PUML/SVG files.
raid refresh Workflow -c OneDrive --app AIA -t nomsa --name-ext AD

raid validate ./export/Workflow_AD.raid
raid validate ./export/Workflow_AD.puml
raid validate ./export/Workflow_AD.svg
```

`-r|--root` selects an exact ImageTree root. `-a|--app` selects an application
root and appends `Image`. `-t|--tenant` selects the subscriber directory;
`--subscriber` is a compatibility alias. `-p|--pathconv` defaults to
`ItemIdTree8x2`. `--number` and `--name-ext` remain separate from `ItemId`, so
`--name Workflow --number 1 --name-ext AD` produces `Workflow_01_AD.*` while
the buckets remain derived from `Workflow`.

PlantUML imports store edge stereotypes as semantic `stereotype` values in
`.raid`, preserving their source spelling. Unmanaged imports require
`--domain <Domain>`; managed imports use `--tenant` as their Domain. The Domain
is recorded under `model.domain` and is not inferred from the output path.

If cloud-backed addressing is requested before the shared configuration exists,
`raid` reports:

```text
RAIkeep configuration was not found at '~/.config/RAIkeep.json5'. Run 'amafu init' to detect cloud providers and create it.
```

`.raid` is authoritative. Export derives content in memory and does not trust
or mutate a stored sibling. Refresh writes a derivative only when it is missing
or stale and never rewrites the authoritative manifest. Managed files are
created directly at their final `RaiFile` location; no TempDir-to-cloud move or
directory swap is used.

Hydratable SVG contains structural `aim-*`, docking-port, routing, and declared
`aim-expression` metadata for `@dr2rai/raid-canvas`. It never contains dynamic
`aim-satisfied` state. Plain SVG omits all `aim-*` metadata. Exported PUML
round-trips through `raid import --puml` to an equivalent semantic manifest.

## Terminal font

> **Font note:** The `raid` help screen uses glyph icons from Nerd Fonts. Most
> Nerd Font-patched fonts render correctly in most terminal environments. Blink
> on iPadOS showed clipping and character-width problems with some choices; the
> tested solution was Blink's
> [Jet Brains Mono Nerd Font stylesheet](https://github.com/blinksh/patched-fonts/blob/main/Jet%20Brains%20Mono%20Nerd%20Font.css).
> See the RAIkeep
> [terminal font guide](https://github.com/Burkhardt/RAIkeep/blob/main/doc/TERMINAL_FONTS.md)
> for Blink, macOS, and Ubuntu setup.

Foldable command reference: [API.md](https://github.com/Burkhardt/RaidSeeder/blob/main/API.md).

Foldable API documentation for the underlying model and artifact library is in
[RaiDiagram API.md](https://github.com/Burkhardt/RaiDiagram/blob/main/API.md).


Governing request: [CR037_AIA_to_RAIkeep_RaidSeeder_Diagram_Artifact_Management.md](https://github.com/Burkhardt/RAIkeep/blob/main/doc/CR037_AIA_to_RAIkeep_RaidSeeder_Diagram_Artifact_Management.md).

CLI dispatch hardening: [CR037.1_AIA_to_RAIkeep_CLI_Global_Flag_and_Verb_Dispatch_Resilience.md](https://github.com/Burkhardt/RAIkeep/blob/main/doc/CR037.1_AIA_to_RAIkeep_CLI_Global_Flag_and_Verb_Dispatch_Resilience.md).
