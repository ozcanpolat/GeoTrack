using System.Text.RegularExpressions;

namespace GeoTrack.Web.Services;

public sealed record PhotoInfo(string Name, string Kind);

/// <summary>Olay fotoğraflarını diskte, türe göre ayrı klasörlerde saklar: {kök}/{olayId}/citizen/c_{guid}.ext (bildirim) ve {kök}/{olayId}/resolution/r_{guid}.ext (çözüm).
/// Dosya adları rastgeledir; hiçbir yükleme mevcut bir dosyanın üzerine yazmaz. Eski sürümde {kök}/{olayId}/ altına yazılan dosyalar okunmaya devam eder.</summary>
public sealed partial class PhotoStore(IConfiguration configuration)
{
    public const long MaxBytes = 5 * 1024 * 1024;
    public const int MaxPerKind = 5;
    public const string Citizen = "citizen";
    public const string Resolution = "resolution";

    [GeneratedRegex(@"^[cr]_[0-9a-f]{32}\.(jpg|png|webp)$")]
    private static partial Regex NamePattern();

    private string Root => configuration["Photos:Path"] is { Length: > 0 } p ? p : Path.Combine(AppContext.BaseDirectory, "photos");
    private string DirFor(int id) => Path.Combine(Root, id.ToString());
    private string DirFor(int id, string kind) => Path.Combine(DirFor(id), kind);
    private static string KindOf(string name) => name[0] == 'c' ? Citizen : Resolution;

    public static bool IsValidKind(string? kind) => kind is Citizen or Resolution;

    public IReadOnlyList<PhotoInfo> List(int id)
    {
        var found = new List<(string Name, string Path)>();
        foreach (var dir in new[] { DirFor(id, Citizen), DirFor(id, Resolution), DirFor(id) }) // son: eski düz yerleşim
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var name = Path.GetFileName(file);
                if (NamePattern().IsMatch(name) && !found.Any(f => f.Name == name)) found.Add((name, file));
            }
        }
        return found.OrderBy(f => File.GetCreationTimeUtc(f.Path)).Select(f => new PhotoInfo(f.Name, KindOf(f.Name))).ToList();
    }

    public async Task<(bool Ok, string? Error)> SaveAsync(int id, string kind, IFormFile file, CancellationToken ct)
    {
        if (!IsValidKind(kind)) return (false, "Geçersiz fotoğraf türü.");
        if (file.Length == 0) return (false, $"{Safe(file)} boş.");
        if (file.Length > MaxBytes) return (false, $"{Safe(file)} 5 MB sınırını aşıyor.");
        if (List(id).Count(p => p.Kind == kind) >= MaxPerKind) return (false, $"En fazla {MaxPerKind} fotoğraf eklenebilir.");

        var header = new byte[12];
        await using var input = file.OpenReadStream();
        var read = await input.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        var ext = Sniff(header.AsSpan(0, read));
        if (ext is null) return (false, $"{Safe(file)} desteklenmiyor (JPG, PNG veya WebP olmalı).");

        var dir = DirFor(id, kind);
        Directory.CreateDirectory(dir);
        var name = $"{(kind == Citizen ? 'c' : 'r')}_{Guid.NewGuid():N}{ext}";
        await using (var output = new FileStream(Path.Combine(dir, name), FileMode.CreateNew, FileAccess.Write)) // asla üzerine yazmaz
        {
            await output.WriteAsync(header.AsMemory(0, read), ct);
            await input.CopyToAsync(output, ct);
        }
        return (true, null);
    }

    public string? PathFor(int id, string name)
    {
        if (!NamePattern().IsMatch(name)) return null;
        var current = Path.Combine(DirFor(id, KindOf(name)), name);
        if (File.Exists(current)) return current;
        var legacy = Path.Combine(DirFor(id), name);
        return File.Exists(legacy) ? legacy : null;
    }

    public static string ContentType(string name) => name.EndsWith(".png") ? "image/png" : name.EndsWith(".webp") ? "image/webp" : "image/jpeg";

    public bool Delete(int id, string name)
    {
        var path = PathFor(id, name);
        if (path is null) return false;
        File.Delete(path);
        return true;
    }

    public void DeleteAll(int id)
    {
        try { if (Directory.Exists(DirFor(id))) Directory.Delete(DirFor(id), true); } catch (IOException) { }
    }

    private static string? Sniff(ReadOnlySpan<byte> h)
    {
        if (h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return ".jpg";
        if (h.Length >= 4 && h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47) return ".png";
        if (h.Length >= 12 && h[0] == 'R' && h[1] == 'I' && h[2] == 'F' && h[3] == 'F' && h[8] == 'W' && h[9] == 'E' && h[10] == 'B' && h[11] == 'P') return ".webp";
        return null;
    }

    private static string Safe(IFormFile f) => "Dosya \"" + Path.GetFileName(f.FileName).Replace('"', '\'')[..Math.Min(40, Path.GetFileName(f.FileName).Length)] + "\"";
}
