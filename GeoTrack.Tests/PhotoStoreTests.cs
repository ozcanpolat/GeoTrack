using GeoTrack.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GeoTrack.Tests;

public sealed class PhotoStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "geotrack-photo-tests-" + Guid.NewGuid().ToString("N"));
    private readonly PhotoStore _store;

    public PhotoStoreTests()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Photos:Path"] = _root }).Build();
        _store = new PhotoStore(config);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private static IFormFile File(byte[] content, string name = "x.jpg")
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "files", name);
    }

    private static byte[] Jpeg(int size = 64) { var b = new byte[size]; b[0] = 0xFF; b[1] = 0xD8; b[2] = 0xFF; return b; }

    [Fact]
    public async Task Save_ValidJpeg_IsStoredAndListed()
    {
        var (ok, error) = await _store.SaveAsync(7, PhotoStore.Citizen, File(Jpeg()), default);
        Assert.True(ok, error);
        var photo = Assert.Single(_store.List(7));
        Assert.Equal(PhotoStore.Citizen, photo.Kind);
        Assert.EndsWith(".jpg", photo.Name);
        Assert.NotNull(_store.PathFor(7, photo.Name));
    }

    [Fact]
    public async Task Save_RenamedTextFile_IsRejectedByContentSniffing()
    {
        var (ok, _) = await _store.SaveAsync(7, PhotoStore.Citizen, File("<script>alert(1)</script>"u8.ToArray(), "evil.jpg"), default);
        Assert.False(ok);
        Assert.Empty(_store.List(7));
    }

    [Fact]
    public async Task Save_TooLarge_IsRejected()
    {
        var (ok, error) = await _store.SaveAsync(7, PhotoStore.Citizen, File(Jpeg((int)PhotoStore.MaxBytes + 1)), default);
        Assert.False(ok);
        Assert.Contains("5 MB", error);
    }

    [Fact]
    public async Task Save_StopsAtMaxPerKind_ButKindsAreIndependent()
    {
        for (var i = 0; i < PhotoStore.MaxPerKind; i++)
            Assert.True((await _store.SaveAsync(7, PhotoStore.Citizen, File(Jpeg()), default)).Ok);
        Assert.False((await _store.SaveAsync(7, PhotoStore.Citizen, File(Jpeg()), default)).Ok);
        Assert.True((await _store.SaveAsync(7, PhotoStore.Resolution, File(Jpeg()), default)).Ok);
    }

    [Fact]
    public async Task Save_InvalidKind_IsRejected() =>
        Assert.False((await _store.SaveAsync(7, "other", File(Jpeg()), default)).Ok);

    [Theory]
    [InlineData("../secret.jpg")]
    [InlineData("c_..%2f..%2fx.jpg")]
    [InlineData("nothex.jpg")]
    public void PathFor_RejectsTraversalAndUnknownNames(string name) => Assert.Null(_store.PathFor(7, name));

    [Fact]
    public async Task DeleteAll_RemovesIncidentFolder()
    {
        await _store.SaveAsync(9, PhotoStore.Citizen, File(Jpeg()), default);
        _store.DeleteAll(9);
        Assert.Empty(_store.List(9));
    }

    [Fact]
    public async Task CitizenAndResolutionPhotos_AreStoredSeparately_AndNeverOverwriteEachOther()
    {
        await _store.SaveAsync(5, PhotoStore.Citizen, File(Jpeg()), default);
        var citizen = Assert.Single(_store.List(5));
        var citizenPath = _store.PathFor(5, citizen.Name)!;
        var before = System.IO.File.ReadAllBytes(citizenPath);

        var resolutionBytes = Jpeg(128);
        await _store.SaveAsync(5, PhotoStore.Resolution, File(resolutionBytes), default);

        var all = _store.List(5);
        Assert.Equal(2, all.Count);
        var resolution = Assert.Single(all, p => p.Kind == PhotoStore.Resolution);
        var resolutionPath = _store.PathFor(5, resolution.Name)!;
        Assert.NotEqual(citizenPath, resolutionPath);
        Assert.NotEqual(Path.GetDirectoryName(citizenPath), Path.GetDirectoryName(resolutionPath));
        Assert.StartsWith("c_", citizen.Name);
        Assert.StartsWith("r_", resolution.Name);
        Assert.Equal(before, System.IO.File.ReadAllBytes(citizenPath));
        Assert.Equal(resolutionBytes, System.IO.File.ReadAllBytes(resolutionPath));
    }

    [Fact]
    public async Task DeletingResolutionPhoto_LeavesCitizenPhotoIntact()
    {
        await _store.SaveAsync(6, PhotoStore.Citizen, File(Jpeg()), default);
        await _store.SaveAsync(6, PhotoStore.Resolution, File(Jpeg()), default);
        var resolution = _store.List(6).Single(p => p.Kind == PhotoStore.Resolution);
        Assert.True(_store.Delete(6, resolution.Name));
        Assert.Equal(PhotoStore.Citizen, Assert.Single(_store.List(6)).Kind);
    }

    [Fact]
    public async Task LegacyFlatLayout_StillReadable()
    {
        var dir = Path.Combine(_root, "8");
        Directory.CreateDirectory(dir);
        var name = "c_" + Guid.NewGuid().ToString("N") + ".jpg";
        await System.IO.File.WriteAllBytesAsync(Path.Combine(dir, name), Jpeg());
        Assert.Equal(PhotoStore.Citizen, Assert.Single(_store.List(8)).Kind);
        Assert.NotNull(_store.PathFor(8, name));
    }
}
