using ICSharpCode.SharpZipLib.Zip;
using ZstdSharp;
using System.Xml;

namespace Asteriq.Services;

/// <summary>
/// Extracts files from Star Citizen's Data.p4k archive.
/// P4K files are ZIP archives with PKZip Classic encryption used by CryEngine/Star Citizen.
/// </summary>
public class P4kExtractorService : IDisposable
{
    private readonly string _p4kPath;
    private ZipFile? _zipFile;
    private FileStream? _fileStream;
    private bool _disposed;

    /// <summary>
    /// Path to the default profile within the p4k archive
    /// </summary>
    public const string DefaultProfilePath = "Data/Libs/Config/defaultProfile.xml";

    /// <summary>
    /// Alternative paths where default profile might be located
    /// </summary>
    private static readonly string[] s_alternativeProfilePaths =
    {
        "Data/Libs/Config/defaultProfile.xml",
        "data/libs/config/defaultprofile.xml",
        "Libs/Config/defaultProfile.xml"
    };

    // Zstandard compression method IDs used in p4k files
    private const int CompressionMethodZstd93 = 93;
    private const int CompressionMethodZstd100 = 100;

    public P4kExtractorService(string p4kPath)
    {
        if (!File.Exists(p4kPath))
            throw new FileNotFoundException($"P4K file not found: {p4kPath}");

        _p4kPath = p4kPath;
    }

    /// <summary>
    /// Opens the p4k archive for reading
    /// </summary>
    public bool Open()
    {
        try
        {
            _fileStream = File.OpenRead(_p4kPath);
            _zipFile = new ZipFile(_fileStream);

            System.Diagnostics.Debug.WriteLine($"[P4kExtractor] Opened p4k archive: {_p4kPath} ({_zipFile.Count} entries)");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[P4kExtractor] Failed to open p4k archive: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Checks if a file exists in the archive
    /// </summary>
    public bool FileExists(string entryPath)
    {
        if (_zipFile is null)
            return false;

        var entry = _zipFile.GetEntry(entryPath);
        return entry is not null;
    }

    /// <summary>
    /// Gets the number of entries in the archive
    /// </summary>
    public long EntryCount => _zipFile?.Count ?? 0;

    /// <summary>
    /// Finds the default profile entry in the archive
    /// </summary>
    public ZipEntry? FindDefaultProfileEntry()
    {
        if (_zipFile is null)
            return null;

        System.Diagnostics.Debug.WriteLine($"[P4kExtractor] Searching for defaultProfile.xml in {_zipFile.Count} entries...");

        // Try known paths first
        foreach (var path in s_alternativeProfilePaths)
        {
            var entry = _zipFile.GetEntry(path);
            if (entry is not null)
            {
                System.Diagnostics.Debug.WriteLine($"[P4kExtractor] Found default profile at known path: {path} (encrypted: {entry.IsCrypted}, size: {entry.Size})");
                return entry;
            }
        }

        // Search for it by name (case-insensitive)
        int searchedCount = 0;
        foreach (ZipEntry entry in _zipFile)
        {
            searchedCount++;
            if (entry.Name.Contains("defaultProfile", StringComparison.OrdinalIgnoreCase) ||
                entry.Name.Contains("default_profile", StringComparison.OrdinalIgnoreCase))
            {
                System.Diagnostics.Debug.WriteLine($"[P4kExtractor] Found potential profile match: {entry.Name} (encrypted: {entry.IsCrypted}, size: {entry.Size})");
                if (entry.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            // Log progress periodically
            if (searchedCount % 100000 == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[P4kExtractor] Searched {searchedCount} entries so far...");
            }
        }

        System.Diagnostics.Debug.WriteLine($"[P4kExtractor] Default profile not found after searching {searchedCount} entries");
        return null;
    }

    /// <summary>
    /// Extracts a file from the archive as bytes
    /// </summary>
    public byte[]? ExtractFile(string entryPath)
    {
        if (_zipFile is null)
            return null;

        var entry = _zipFile.GetEntry(entryPath);
        if (entry is null)
            return null;

        return ExtractEntry(entry);
    }

    /// <summary>
    /// Extracts an entry from the archive as bytes
    /// </summary>
    public byte[]? ExtractEntry(ZipEntry entry)
    {
        if (_zipFile is null || _fileStream is null || entry is null)
            return null;

        try
        {
            // Check for Zstandard compression (methods 93 or 100) which SharpZipLib doesn't support
            var compressionMethod = (int)entry.CompressionMethod;
            if (compressionMethod == CompressionMethodZstd93 || compressionMethod == CompressionMethodZstd100)
            {
                System.Diagnostics.Debug.WriteLine($"[P4kExtractor] Entry {entry.Name} uses Zstandard compression, decompressing manually...");
                return ExtractZstdEntry(entry);
            }

            // Check if entry is encrypted
            if (entry.IsCrypted)
            {
                System.Diagnostics.Debug.WriteLine($"[P4kExtractor] Entry {entry.Name} is encrypted, attempting to read anyway...");
                // Try reading anyway - some entries might have weak/no actual encryption
                try
                {
                    using var stream = _zipFile.GetInputStream(entry);
                    using var ms = new MemoryStream();

                    var buffer = new byte[4096];
                    int bytesRead;
                    while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ms.Write(buffer, 0, bytesRead);
                    }

                    return ms.ToArray();
                }
                catch
                {
                    System.Diagnostics.Debug.WriteLine($"[P4kExtractor] Entry {entry.Name} encryption prevents reading");
                    return null;
                }
            }

            // Standard extraction for supported compression methods
            using var inputStream = _zipFile.GetInputStream(entry);
            using var outputMs = new MemoryStream();

            var buf = new byte[4096];
            int read;
            while ((read = inputStream.Read(buf, 0, buf.Length)) > 0)
            {
                outputMs.Write(buf, 0, read);
            }

            return outputMs.ToArray();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[P4kExtractor] Failed to extract entry '{entry.Name}' " +
                                               $"(Size: {entry.Size}, Compressed: {entry.CompressedSize}, Encrypted: {entry.IsCrypted}). " +
                                               $"Error type: {ex.GetType().Name}, Details: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Extracts an entry that uses Zstandard compression
    /// </summary>
    private byte[]? ExtractZstdEntry(ZipEntry entry)
    {
        if (_fileStream is null)
            return null;

        try
        {
            // Read the compressed data directly from the file
            // ZipEntry.Offset gives us the start of the local file header
            var localHeaderOffset = entry.Offset;

            _fileStream.Seek(localHeaderOffset, SeekOrigin.Begin);

            // Read local file header (minimum 30 bytes)
            var localHeader = new byte[30];
            _fileStream.Read(localHeader, 0, 30);

            // Verify signature (0x04034b50 = "PK\x03\x04")
            if (localHeader[0] != 0x50 || localHeader[1] != 0x4B ||
                localHeader[2] != 0x03 || localHeader[3] != 0x04)
            {
                System.Diagnostics.Debug.WriteLine($"[P4kExtractor] Invalid local file header signature for {entry.Name}");
                return null;
            }

            // Get filename and extra field lengths to skip past them
            var fileNameLength = BitConverter.ToUInt16(localHeader, 26);
            var extraFieldLength = BitConverter.ToUInt16(localHeader, 28);

            // Skip to the compressed data
            _fileStream.Seek(localHeaderOffset + 30 + fileNameLength + extraFieldLength, SeekOrigin.Begin);

            // Read compressed data
            var compressedData = new byte[entry.CompressedSize];
            var totalRead = 0;
            while (totalRead < compressedData.Length)
            {
                var read = _fileStream.Read(compressedData, totalRead, (int)(compressedData.Length - totalRead));
                if (read == 0)
                    break;
                totalRead += read;
            }

            if (totalRead != compressedData.Length)
            {
                System.Diagnostics.Debug.WriteLine($"[P4kExtractor] Only read {totalRead} of {compressedData.Length} compressed bytes for {entry.Name}");
            }

            // Decompress using Zstandard
            using var decompressor = new Decompressor();
            var decompressedData = decompressor.Unwrap(compressedData);

            System.Diagnostics.Debug.WriteLine($"[P4kExtractor] Decompressed {compressedData.Length} -> {decompressedData.Length} bytes for {entry.Name}");
            return decompressedData.ToArray();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[P4kExtractor] Failed to extract Zstandard-compressed entry '{entry.Name}' " +
                                               $"(Size: {entry.Size}, Compressed: {entry.CompressedSize}). " +
                                               $"Error type: {ex.GetType().Name}, Details: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Extracts the default profile XML from the archive and parses it.
    /// Handles both regular XML and CryXmlB binary format.
    /// </summary>
    public XmlDocument? ExtractDefaultProfile()
    {
        System.Diagnostics.Debug.WriteLine("[P4kExtractor] Attempting to extract defaultProfile.xml from p4k...");

        var entry = FindDefaultProfileEntry();
        if (entry is null)
        {
            System.Diagnostics.Debug.WriteLine("[P4kExtractor] Could not find defaultProfile.xml entry in p4k");
            return null;
        }

        System.Diagnostics.Debug.WriteLine($"[P4kExtractor] Extracting entry: {entry.Name} (size: {entry.Size}, compressed: {entry.CompressedSize}, encrypted: {entry.IsCrypted})");

        var data = ExtractEntry(entry);
        if (data is null || data.Length == 0)
        {
            System.Diagnostics.Debug.WriteLine("[P4kExtractor] Extracted empty data for default profile");
            return null;
        }

        System.Diagnostics.Debug.WriteLine($"[P4kExtractor] Extracted {data.Length} bytes from default profile");

        // Log first few bytes to see if it's XML or binary
        var header = data.Length >= 20 ? BitConverter.ToString(data, 0, 20) : BitConverter.ToString(data);
        System.Diagnostics.Debug.WriteLine($"[P4kExtractor] Data header bytes: {header}");

        // Parse the XML (handles both regular XML and CryXmlB)
        var doc = CryXmlService.Deserialize(data);
        if (doc is not null)
        {
            System.Diagnostics.Debug.WriteLine($"[P4kExtractor] Successfully parsed XML document, root element: {doc.DocumentElement?.Name}");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[P4kExtractor] Failed to parse XML document");
        }

        return doc;
    }

    /// <summary>
    /// Lists entries matching a pattern (for debugging)
    /// </summary>
    public IEnumerable<string> ListEntries(string pattern)
    {
        if (_zipFile is null)
            yield break;

        foreach (ZipEntry entry in _zipFile)
        {
            if (entry.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                yield return entry.Name;
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _zipFile?.Close();
            _zipFile = null;
            _fileStream?.Dispose();
            _fileStream = null;
            _disposed = true;
        }
    }
}
