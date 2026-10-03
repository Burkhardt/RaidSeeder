# RaidSeeder command reference 4.5.3

The four verbs are reserved and command-first. A misplaced verb is rejected
before artifact access with exit code `2` and an exact corrected invocation.
`-v`/`--version` takes immediate precedence at any argument position.

<details>
<summary><code>raid import</code></summary>

```text
raid import --puml <diagram.puml>
  [--name <ItemId>] [--number <n>] [--name-ext <value>]
  [--out <directory> | <managed-address>]
```

Imports supported PlantUML through `IModelImporter`. An explicit `--out`
receives a flat `.raid`/`.puml`/`.svg` set. A managed address writes the same
stem through `DiagramArtifactSet` and the chosen ItemTree convention. `--out`
is mutually exclusive with `--root` and `--app`.

</details>

<details>
<summary><code>raid export</code></summary>

```text
raid export <ItemId> <managed-address>
  [--number <n>] [--name-ext <value>]
  --format <raid|puml|svg|all>
  [--svg-profile <hydratable|plain>] [--out <directory>]
```

Loads the authoritative `.raid` manifest and derives current output in memory.
It never blindly copies or updates a possibly stale managed sibling. One format
may be written to stdout; `all` requires `--out`.

</details>

<details>
<summary><code>raid refresh</code></summary>

```text
raid refresh <ItemId> <managed-address>
  [--number <n>] [--name-ext <value>]
  [--svg-profile <hydratable|plain>]
```

Compares deterministically derived PUML and SVG with their co-located siblings.
Only missing or stale derivatives are written. A proven-current derivative is
not touched, and the authoritative `.raid` file is never rewritten.

</details>

<details>
<summary><code>raid validate</code></summary>

```text
raid validate <diagram.raid|diagram.puml|diagram.svg>
```

Validates manifest/semantic structure, supported PlantUML import structure, or
the detected hydratable/plain SVG profile. Validation is read-only.

</details>

<details>
<summary>Managed address and artifact identity options</summary>

```text
-c, --cloud <provider>       configured cloud provider
-r, --root <directory>       exact ImageTree root; alternative to --app
-a, --app <directory>        application root; Image is appended
-t, --tenant <name>          subscriber/tenant segment
    --subscriber <name>      compatibility alias for --tenant
-p, --pathconv <1|2|3|4>     ItemIdTree8x2 by default
    --name-ext <value>        optional NameExt, separate from ItemId
    --number <n>              optional number before NameExt
```

The logical filename is `ItemId[_NN][_NameExt].ext`; ItemTree buckets are
always derived from `ItemId` alone.

</details>

<details>
<summary>SVG profiles and static execution boundary</summary>

`hydratable` emits structural RaidCanvas `aim-*` metadata, ports, routing, and
declared `aim-expression` values. It never evaluates or writes dynamic
`aim-satisfied` state. `plain` emits the visible vector diagram with no `aim-*`
metadata.

</details>

<details>
<summary>Global options and package identity</summary>

`-h|--help`, `-v|--version`, `-n|--nologo`, and `-d|--debug` follow the shared
RAIkeep CLI vocabulary. `raid --version` prints `raid v4.5.3`. Install the
`RaidSeeder` package and invoke the `raid` command.

</details>

The reusable public model and artifact APIs live in RaiDiagram and are
documented in [RaiDiagram API.md](https://github.com/Burkhardt/RaiDiagram/blob/main/API.md).

This command contract is governed by
[`CR037_AIA_to_RAIkeep_RaidSeeder_Diagram_Artifact_Management.md`](https://github.com/Burkhardt/RAIkeep/blob/main/doc/CR037_AIA_to_RAIkeep_RaidSeeder_Diagram_Artifact_Management.md).
