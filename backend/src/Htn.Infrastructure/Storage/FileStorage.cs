using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Htn.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>Folder for uploaded files (a Docker volume in compose). Relative paths resolve against the content root.</summary>
    public string Path { get; set; } = "uploads";
}

/// <summary>Stores file bytes by ID. Metadata (content type, size, checksum) lives in <c>stored_files</c>.</summary>
public interface IFileStorage
{
    Task SaveAsync(Guid id, ReadOnlyMemory<byte> content, CancellationToken ct);

    /// <summary>Opens the file for reading, or null if it doesn't exist.</summary>
    Task<Stream?> OpenReadAsync(Guid id, CancellationToken ct);
}

/// <summary>
/// Files on local disk as <c>{Storage:Path}/{id:N}</c>. Names come only from GUIDs, so user input never reaches a path.
/// Writes go to a temp file first and are moved into place, so a reader never sees a partial file.
/// </summary>
public sealed class LocalDiskFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalDiskFileStorage(IOptions<StorageOptions> options, IHostEnvironment env)
    {
        var configured = string.IsNullOrWhiteSpace(options.Value.Path) ? "uploads" : options.Value.Path;
        _root = System.IO.Path.GetFullPath(configured, env.ContentRootPath);
    }

    public string Root => _root;

    private string PathFor(Guid id) => System.IO.Path.Combine(_root, id.ToString("N"));

    public async Task SaveAsync(Guid id, ReadOnlyMemory<byte> content, CancellationToken ct)
    {
        Directory.CreateDirectory(_root);
        var final = PathFor(id);
        var temp = final + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await fs.WriteAsync(content, ct);
                await fs.FlushAsync(ct);
            }
            File.Move(temp, final, overwrite: false);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public Task<Stream?> OpenReadAsync(Guid id, CancellationToken ct)
    {
        var path = PathFor(id);
        if (!File.Exists(path)) return Task.FromResult<Stream?>(null);
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult<Stream?>(stream);
    }
}
