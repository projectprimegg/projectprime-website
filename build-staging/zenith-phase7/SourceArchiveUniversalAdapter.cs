using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using MphRead.Mods.MapGen.Universal;

namespace MphRead.Mods.MapGen.Universal;

/// <summary>
/// Tier-2 adapter for a structured archive containing one or more OBJ/glTF/GLB
/// worlds plus optional dedicated collision. The archive is inspected by
/// content instead of being assumed to be a Quake package because its extension
/// is .zip. This is the reusable source-pack route used by Zenith and future
/// extracted map packs.
/// </summary>
public sealed class SourceArchiveUniversalAdapter : IUniversalMapAdapter
{
    private static readonly string[] _extensions = { ".zip" };
    private readonly ExistingModelUniversalAdapter _modelAdapter = new();

    public string Id => "structured-source-archive";
    public string DisplayName => "Structured 3D source pack";
    public int FidelityTier => 2;
    public IReadOnlyCollection<string> Extensions => _extensions;
    public bool CanImport(string path)
        => Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase);

    public UniversalImportResult Import(
        string path,
        UniversalImportOptions options,
        CancellationToken cancellation = default)
    {
        path = Path.GetFullPath(path);
        SourceArchiveInspection inspection = SourceArchiveInspector.Inspect(path, cancellation);
        if (inspection.SelectedRenderModels.Count == 0)
            throw new InvalidDataException("The source archive contains no supported render model. Add OBJ, glTF or GLB geometry, or provide projectprime-source.json.");

        string root = Path.Combine(Path.GetTempPath(),
            "ProjectPrime-source-pack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            SourceArchiveInspector.ExtractValidated(path, root, cancellation);
            SourceArchiveManifest? manifest = inspection.Manifest;
            UniversalImportOptions effective = ApplyManifest(options, manifest);
            bool hasDedicatedCollision = inspection.SelectedCollisionModels.Count > 0;
            if (effective.Collision == ModelCollisionMode.Companion && !hasDedicatedCollision)
                throw new InvalidDataException("Companion collision was requested, but the archive contains no dedicated collision model.");

            var combined = new UniversalMap
            {
                Name = manifest?.Name ?? Path.GetFileNameWithoutExtension(path),
                Source = new UniversalSourceInfo
                {
                    AdapterId = Id,
                    AdapterVersion = "1",
                    SourcePath = path,
                    SourceFormat = "structured-3d-archive",
                    CoordinateSystem = effective.ZUp ? "Z-up source converted to Project Prime" : "Project Prime / source Y-up",
                    UnitsToMph = effective.Scale,
                    Dependencies = new List<string> { path }
                }
            };
            combined.Metadata["archive.entries"] = inspection.EntryCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            combined.Metadata["archive.uncompressedBytes"] = inspection.TotalUncompressedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture);
            combined.Metadata["archive.renderModels"] = string.Join("|", inspection.SelectedRenderModels);
            combined.Metadata["archive.collisionModels"] = string.Join("|", inspection.SelectedCollisionModels);
            foreach (string warning in inspection.Warnings)
                combined.Diagnostics.Add(new UniversalDiagnostic
                {
                    Code = "PP-SOURCE-PACK",
                    Severity = "warning",
                    Message = warning
                });

            var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var assetSources = new Dictionary<string, string>(StringComparer.Ordinal);
            UniversalImportOptions renderOptions = effective with
            {
                Collision = hasDedicatedCollision ? ModelCollisionMode.None : effective.Collision
            };

            foreach (string relative in inspection.SelectedRenderModels)
            {
                cancellation.ThrowIfCancellationRequested();
                UniversalImportResult part = _modelAdapter.Import(
                    SourceArchiveInspector.ResolveExtracted(root, relative),
                    renderOptions,
                    cancellation);
                MergePart(combined, part, collisionOnly: false, assets, assetSources, path);
            }

            foreach (string relative in inspection.SelectedCollisionModels)
            {
                cancellation.ThrowIfCancellationRequested();
                UniversalImportResult part = _modelAdapter.Import(
                    SourceArchiveInspector.ResolveExtracted(root, relative),
                    effective with { Collision = ModelCollisionMode.None },
                    cancellation);
                MergePart(combined, part, collisionOnly: true, assets, assetSources, path);
            }

            if (combined.RenderMeshes.Count == 0)
                throw new InvalidDataException("The selected source-pack models produced no render geometry.");
            if (effective.Collision != ModelCollisionMode.None && combined.CollisionMeshes.Count == 0)
                throw new InvalidDataException("The selected source-pack settings produced no collision geometry.");

            combined.Metadata["archive.adapterFidelity"] = "2";
            combined.Metadata["archive.collisionSource"] = hasDedicatedCollision ? "dedicated" : effective.Collision.ToString();
            return new UniversalImportResult(
                combined,
                assets,
                assetSources,
                new[] { path });
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static UniversalImportOptions ApplyManifest(
        UniversalImportOptions options,
        SourceArchiveManifest? manifest)
    {
        if (manifest == null) return options;
        return options with
        {
            Scale = manifest.Scale ?? options.Scale,
            FlipWinding = manifest.FlipWinding ?? options.FlipWinding,
            ZUp = manifest.ZUp ?? options.ZUp,
            FlipUvVertical = manifest.FlipUvVertical ?? options.FlipUvVertical
        };
    }

    private static void MergePart(
        UniversalMap destination,
        UniversalImportResult part,
        bool collisionOnly,
        Dictionary<string, byte[]> assets,
        Dictionary<string, string> assetSources,
        string archivePath)
    {
        int materialOffset = destination.Materials.Count;
        destination.Materials.AddRange(part.Map.Materials);
        IEnumerable<UniversalMesh> meshes = part.Map.RenderMeshes.Concat(part.Map.CollisionMeshes);
        foreach (UniversalMesh mesh in meshes)
        {
            for (int index = 0; index < mesh.FaceMaterials.Count; index++)
            {
                int material = mesh.FaceMaterials[index];
                if (material >= 0) mesh.FaceMaterials[index] = material + materialOffset;
            }
            if (collisionOnly)
            {
                mesh.CollisionOnly = true;
                mesh.Solid = true;
                destination.CollisionMeshes.Add(mesh);
            }
            else if (mesh.CollisionOnly)
            {
                destination.CollisionMeshes.Add(mesh);
            }
            else
            {
                destination.RenderMeshes.Add(mesh);
            }
        }
        destination.Entities.AddRange(part.Map.Entities);
        destination.Diagnostics.AddRange(part.Map.Diagnostics);
        foreach ((string key, byte[] value) in part.Assets)
        {
            if (assets.TryGetValue(key, out byte[]? existing)
                && !existing.AsSpan().SequenceEqual(value))
                throw new InvalidDataException($"Two source-pack models produced different assets at '{key}'. Add unique material/texture paths in the source pack.");
            assets[key] = value;
            assetSources[key] = archivePath;
        }
    }
}

public sealed class SourceArchiveManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string? Name { get; set; }
    public string? RenderModel { get; set; }
    public List<string> RenderModels { get; set; } = new();
    public string? CollisionModel { get; set; }
    public List<string> CollisionModels { get; set; } = new();
    public float? Scale { get; set; }
    public bool? FlipWinding { get; set; }
    public bool? ZUp { get; set; }
    public bool? FlipUvVertical { get; set; }
}

public sealed class SourceArchiveInspection
{
    public string ArchivePath { get; set; } = "";
    public int EntryCount { get; set; }
    public long TotalUncompressedBytes { get; set; }
    public string? ManifestEntry { get; set; }
    public SourceArchiveManifest? Manifest { get; set; }
    public List<string> RenderCandidates { get; set; } = new();
    public List<string> CollisionCandidates { get; set; } = new();
    public List<string> UnsupportedModelCandidates { get; set; } = new();
    public List<string> SelectedRenderModels { get; set; } = new();
    public List<string> SelectedCollisionModels { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public static class SourceArchiveInspector
{
    public const int MaxEntries = 100_000;
    public const long MaxEntryBytes = 4L * 1024 * 1024 * 1024;
    public const long MaxTotalBytes = 16L * 1024 * 1024 * 1024;
    private static readonly string[] _supported = { ".obj", ".gltf", ".glb" };
    private static readonly string[] _recognizedUnsupported = { ".fbx", ".dae", ".3ds", ".blend", ".ply", ".stl", ".smd" };
    private static readonly string[] _collisionWords = { "collision", "collider", "physics", "phys", "proxy", "clip" };
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true
    };

    public static SourceArchiveInspection Inspect(
        string archivePath,
        CancellationToken cancellation = default)
    {
        archivePath = Path.GetFullPath(archivePath);
        if (!File.Exists(archivePath)) throw new FileNotFoundException("Source archive was not found.", archivePath);
        var result = new SourceArchiveInspection { ArchivePath = archivePath };
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > MaxEntries)
            throw new InvalidDataException($"Source archive has {archive.Entries.Count:N0} entries; the safety limit is {MaxEntries:N0}.");

        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellation.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name)) continue;
            string canonical = CanonicalEntry(entry.FullName);
            if (entry.Length > MaxEntryBytes)
                throw new InvalidDataException($"Archive entry '{canonical}' exceeds the per-file safety limit.");
            result.TotalUncompressedBytes = checked(result.TotalUncompressedBytes + entry.Length);
            if (result.TotalUncompressedBytes > MaxTotalBytes)
                throw new InvalidDataException("Source archive expands beyond the 16 GiB safety limit.");
            if (entries.ContainsKey(canonical))
                throw new InvalidDataException($"Archive contains duplicate path '{canonical}'.");
            entries[canonical] = entry;
        }
        result.EntryCount = entries.Count;

        string? manifestName = entries.Keys.FirstOrDefault(name =>
            name.Equals("projectprime-source.json", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("/projectprime-source.json", StringComparison.OrdinalIgnoreCase));
        if (manifestName != null)
        {
            result.ManifestEntry = manifestName;
            using Stream stream = entries[manifestName].Open();
            result.Manifest = JsonSerializer.Deserialize<SourceArchiveManifest>(stream, _json)
                ?? throw new InvalidDataException("projectprime-source.json is empty or invalid.");
            if (result.Manifest.SchemaVersion != 1)
                throw new InvalidDataException($"Unsupported source-pack manifest version {result.Manifest.SchemaVersion}.");
        }

        foreach (string name in entries.Keys)
        {
            string extension = Path.GetExtension(name);
            if (_supported.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                if (LooksLikeCollision(name)) result.CollisionCandidates.Add(name);
                else result.RenderCandidates.Add(name);
            }
            else if (_recognizedUnsupported.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                result.UnsupportedModelCandidates.Add(name);
            }
        }

        if (result.Manifest != null)
        {
            AddManifestSelection(result.SelectedRenderModels, result.Manifest.RenderModel, result.Manifest.RenderModels, entries, "render");
            AddManifestSelection(result.SelectedCollisionModels, result.Manifest.CollisionModel, result.Manifest.CollisionModels, entries, "collision");
        }
        if (result.SelectedRenderModels.Count == 0 && result.RenderCandidates.Count > 0)
        {
            string selected = result.RenderCandidates
                .OrderByDescending(name => CandidateScore(name, entries[name].Length))
                .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
                .First();
            result.SelectedRenderModels.Add(selected);
            if (result.RenderCandidates.Count > 1)
                result.Warnings.Add($"Multiple render models were found; auto-selected '{selected}'. Add projectprime-source.json to make the selection explicit.");
        }
        if (result.SelectedCollisionModels.Count == 0 && result.CollisionCandidates.Count > 0)
        {
            string? modelStem = result.SelectedRenderModels.Count == 1
                ? Path.GetFileNameWithoutExtension(result.SelectedRenderModels[0]) : null;
            string selected = result.CollisionCandidates
                .OrderByDescending(name => CollisionScore(name, entries[name].Length, modelStem))
                .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
                .First();
            result.SelectedCollisionModels.Add(selected);
            if (result.CollisionCandidates.Count > 1)
                result.Warnings.Add($"Multiple collision models were found; auto-selected '{selected}'. Add projectprime-source.json to make the selection explicit.");
        }
        if (result.RenderCandidates.Count == 0 && result.UnsupportedModelCandidates.Count > 0)
            result.Warnings.Add("The archive contains model formats that this client recognizes but cannot import yet: "
                + string.Join(", ", result.UnsupportedModelCandidates.Take(12)));
        if (result.SelectedCollisionModels.Count == 0)
            result.Warnings.Add("No dedicated collision model was selected. Do not certify this map until collision has been generated, repaired and all movement profiles pass.");
        return result;
    }

    public static void ExtractValidated(
        string archivePath,
        string destination,
        CancellationToken cancellation = default)
    {
        archivePath = Path.GetFullPath(archivePath);
        destination = Path.GetFullPath(destination);
        Directory.CreateDirectory(destination);
        string prefix = destination.EndsWith(Path.DirectorySeparatorChar)
            ? destination : destination + Path.DirectorySeparatorChar;
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > MaxEntries) throw new InvalidDataException("Source archive contains too many entries.");
        long total = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellation.ThrowIfCancellationRequested();
            string canonical = CanonicalEntry(entry.FullName);
            string output = Path.GetFullPath(Path.Combine(destination,
                canonical.Replace('/', Path.DirectorySeparatorChar)));
            if (!output.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Archive entry escapes extraction root: {canonical}");
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(output);
                continue;
            }
            total = checked(total + entry.Length);
            if (entry.Length > MaxEntryBytes || total > MaxTotalBytes)
                throw new InvalidDataException("Source archive exceeds extraction safety limits.");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using Stream input = entry.Open();
            using var outputStream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(outputStream);
        }
    }

    public static string ResolveExtracted(string root, string relative)
    {
        root = Path.GetFullPath(root);
        string path = Path.GetFullPath(Path.Combine(root,
            CanonicalEntry(relative).Replace('/', Path.DirectorySeparatorChar)));
        string prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Source-pack selection escapes extraction root.");
        if (!File.Exists(path)) throw new FileNotFoundException("Selected source-pack file was not extracted.", path);
        return path;
    }

    public static string ToJson(SourceArchiveInspection inspection)
        => JsonSerializer.Serialize(inspection, _json);

    private static void AddManifestSelection(
        List<string> destination,
        string? single,
        IEnumerable<string>? many,
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        string role)
    {
        IEnumerable<string> supplied = (many ?? Array.Empty<string>())
            .Concat(string.IsNullOrWhiteSpace(single) ? Array.Empty<string>() : new[] { single! });
        foreach (string item in supplied)
        {
            string canonical = CanonicalEntry(item);
            if (!entries.ContainsKey(canonical))
                throw new InvalidDataException($"Manifest {role} model '{canonical}' does not exist in the archive.");
            string extension = Path.GetExtension(canonical);
            if (!_supported.Contains(extension, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"Manifest {role} model '{canonical}' uses unsupported format '{extension}'.");
            if (!destination.Contains(canonical, StringComparer.OrdinalIgnoreCase)) destination.Add(canonical);
        }
    }

    private static int CandidateScore(string name, long size)
    {
        string lower = name.ToLowerInvariant();
        int score = (int)Math.Min(30, size / (1024 * 1024));
        if (lower.Contains("zenith")) score += 120;
        if (lower.Contains("world") || lower.Contains("scene") || lower.Contains("level") || lower.Contains("map")) score += 60;
        if (lower.Contains("render") || lower.Contains("visual")) score += 30;
        if (Path.GetExtension(lower) == ".glb") score += 8;
        score -= name.Count(ch => ch == '/') * 2;
        if (lower.Contains("lod")) score -= 40;
        if (lower.Contains("preview") || lower.Contains("thumbnail")) score -= 80;
        return score;
    }

    private static int CollisionScore(string name, long size, string? modelStem)
    {
        string lower = name.ToLowerInvariant();
        int score = (int)Math.Min(30, size / (1024 * 1024));
        if (lower.Contains("collision")) score += 100;
        if (lower.Contains("playerclip") || lower.Contains("player_clip")) score += 80;
        if (lower.Contains("clip")) score += 40;
        if (modelStem != null && lower.Contains(modelStem.ToLowerInvariant())) score += 60;
        if (lower.Contains("simplified") || lower.Contains("clean")) score += 20;
        return score;
    }

    private static bool LooksLikeCollision(string name)
    {
        string stem = Path.GetFileNameWithoutExtension(name).ToLowerInvariant();
        return _collisionWords.Any(word => stem.Contains(word, StringComparison.Ordinal));
    }

    private static string CanonicalEntry(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("Archive contains an empty path.");
        string canonical = value.Replace('\\', '/').TrimStart('/');
        if (canonical.Length == 0 || Path.IsPathRooted(canonical)
            || canonical.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException($"Unsafe archive path '{value}'.");
        return canonical;
    }
}
