using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AliantWebS.Data;
using AliantWebS.Models;
using ExifLibrary;

namespace AliantWebS.Pages
{
    /// <summary>
    /// The photos and videos attached to an event, element, structure or site: where they are stored, which files
    /// count, and how the camera adds one. The layout on disk is FileManagementService's, unchanged
    /// (Images/&lt;structure&gt;/Event/&lt;x&gt;/&lt;10 characters of the id&gt;&lt;name&gt; and so on), so files uploaded before
    /// the camera and the gallery existed are still found. Two things are new, both in hidden subfolders that the
    /// non-recursive listing never returns: ".thumbs" (small previews, written by the camera, used by the gallery)
    /// and ".upload" (a file being received, moved into place only once it is complete).
    /// </summary>
    public static class MediaLibrary
    {
        public const string Folder = "Images";
        public const string ThumbnailFolder = ".thumbs";
        public const string UploadFolder = ".upload";
        public const long MaxPhotoBytes = 40L * 1024 * 1024;
        public const long MaxVideoBytes = 600L * 1024 * 1024;
        public const long MaxThumbnailBytes = 2L * 1024 * 1024;

        private static readonly HashSet<string> PhotoExtensions =
            new(StringComparer.OrdinalIgnoreCase) { "jpg", "jpeg", "png", "webp", "gif", "bmp", "heic", "heif" };

        private static readonly HashSet<string> VideoExtensions =
            new(StringComparer.OrdinalIgnoreCase) { "mp4", "webm", "mov", "m4v" };

        /// <summary>Photos and videos can be kept for these four types only (FilesUpload.PreparePath has no folder for
        /// anything else, an Equipement for instance), and only once the item exists: an unsaved item still has an
        /// empty id, and its files would be stored under zeros and never found again.</summary>
        public static bool CanStore(object? owner)
        {
            var type = owner?.GetType();
            var supported = type == typeof(Event) || type == typeof(Element) || type == typeof(Structure) || type == typeof(Site);
            return supported && IdOf(owner) != Guid.Empty && (owner is not Event e || e.StructureId != Guid.Empty)
                   && (owner is not Element el || el.StructureId != Guid.Empty);
        }

        public static bool IsSupportedType(object? owner) =>
            owner is Event or Element or Structure or Site;

        public static Guid IdOf(object? owner) => owner switch
        {
            Event e => e.EventId,
            Element el => el.ElementId,
            Structure s => s.StructureId,
            Site site => site.SiteId,
            Equipement q => q.EquipementId,
            _ => Guid.Empty,
        };

        public static string? NameOf(object? owner) => owner switch
        {
            Event e => e.Name,
            Element el => el.Name,
            Structure s => s.Name,
            Site site => site.Name,
            Equipement q => q.Name,
            _ => null,
        };

        /// <summary>"Event", "Element"... - a localization key, translated by the caller.</summary>
        public static string KindOf(object? owner) => owner switch
        {
            Event => "Event",
            Element => "Element",
            Structure => "Structure",
            Site => "Site",
            Equipement => "Equipment",
            _ => "Item",
        };

        public static MediaKind KindOfFile(string path)
        {
            var ext = Path.GetExtension(path).TrimStart('.');
            if (PhotoExtensions.Contains(ext)) return MediaKind.Photo;
            if (VideoExtensions.Contains(ext)) return MediaKind.Video;
            return MediaKind.Other;
        }

        /// <summary>The extension to store a file under, lower case, or null when it is not a photo or video of the kind
        /// announced (a ".exe" called a photo is refused, whatever the browser says).</summary>
        public static string? CheckExtension(string? ext, MediaKind kind)
        {
            ext = (ext ?? "").Trim().TrimStart('.').ToLowerInvariant();
            if (kind == MediaKind.Photo && PhotoExtensions.Contains(ext)) return ext;
            if (kind == MediaKind.Video && VideoExtensions.Contains(ext)) return ext;
            return null;
        }

        public static long MaxBytes(MediaKind kind) => kind == MediaKind.Video ? MaxVideoBytes : MaxPhotoBytes;

        /// <summary>A file name without its extension, safe on Windows and Linux, at most 80 characters; null when
        /// nothing usable is left.</summary>
        public static string? CleanBaseName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            name = Path.GetFileNameWithoutExtension(name.Replace('\\', '/').Split('/').Last());
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars()) { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };
            var builder = new StringBuilder(name.Length);
            foreach (var c in name) builder.Append(invalid.Contains(c) || char.IsControl(c) ? '_' : c);
            var clean = builder.ToString().Trim().Trim('.').Trim();
            if (clean.Length > 80) clean = clean[..80].Trim();
            return clean.Length == 0 ? null : clean;
        }

        /// <summary>The camera's naming, kept from before: the local time the shot was taken, "2026-09-30 14-22-05".</summary>
        public static string TimestampName(DateTime local) => local.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture);

        /// <summary>A path for a new file of this owner that no file uses yet: "name.ext", then "name (2).ext"...
        /// Null when the owner cannot keep files.</summary>
        public static string? NewPath(object owner, string contentRoot, string baseName, string ext)
        {
            if (!CanStore(owner)) return null;
            var root = Path.Combine(contentRoot, Folder);
            for (var n = 1; n < 1000; n++)
            {
                var name = n == 1 ? baseName : $"{baseName} ({n})";
                var full = FilesUpload.PreparePath($"{name}.{ext}", owner, root);
                if (string.IsNullOrEmpty(full)) return null;
                if (!File.Exists(full)) return full;
            }
            return null;
        }

        /// <summary>Stores a new file for the owner, or replaces one. <paramref name="writeTo"/> fills a temporary file
        /// next to the destination, which is moved into place only once complete, so the gallery never lists half a
        /// file. If another user takes the name meanwhile, the next free name is used rather than their file
        /// overwritten. Returns the stored path.</summary>
        public static async Task<string> StoreAsync(object owner, string contentRoot, string baseName, string ext,
            Func<string, Task> writeTo, string? replacing = null)
        {
            var target = replacing ?? NewPath(owner, contentRoot, baseName, ext)
                         ?? throw new MediaStorageException();
            var temp = UploadPath(target);
            Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
            try
            {
                await writeTo(temp);
                if (replacing != null)
                {
                    File.Move(temp, target, overwrite: true);
                    return target;
                }
                for (var attempt = 0; ; attempt++)
                {
                    try
                    {
                        File.Move(temp, target, overwrite: false);
                        return target;
                    }
                    catch (IOException) when (attempt < 5 && File.Exists(target))
                    {
                        target = NewPath(owner, contentRoot, baseName, ext)
                                 ?? throw new MediaStorageException();
                    }
                }
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        public static string ThumbnailPath(string mediaPath) =>
            Path.Combine(Path.GetDirectoryName(mediaPath) ?? "", ThumbnailFolder, Path.GetFileName(mediaPath) + ".jpg");

        public static string UploadPath(string mediaPath) =>
            Path.Combine(Path.GetDirectoryName(mediaPath) ?? "", UploadFolder, Guid.NewGuid().ToString("N") + ".part");

        /// <summary>Removes a file and its preview. A preview left behind would be harmless; a file that cannot be
        /// removed throws, so the caller can say so.</summary>
        public static void Delete(string mediaPath)
        {
            File.Delete(mediaPath);
            var thumb = ThumbnailPath(mediaPath);
            try { if (File.Exists(thumb)) File.Delete(thumb); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        /// <summary>The owner's photos and videos, newest first, as FileManagementService finds them (relative to the
        /// working directory, like every other reader of the Images folder).</summary>
        public static async Task<List<MediaItem>> ListAsync(object? owner, IEnumerable<Element>? elements = null)
        {
            var paths = new List<string>();
            if (elements != null)
                foreach (var element in elements)
                    paths.AddRange(await FileManagementService.GetPathForFilesAsync(element, false));
            else if (owner != null)
                paths = await FileManagementService.GetPathForFilesAsync(owner, false);

            var items = new List<MediaItem>();
            foreach (var path in paths.Distinct())
            {
                var info = new FileInfo(path);
                if (!info.Exists) continue;
                var thumb = ThumbnailPath(path);
                var thumbInfo = new FileInfo(thumb);
                items.Add(new MediaItem(
                    Path: path,
                    Name: info.Name,
                    Kind: KindOfFile(path),
                    Size: info.Length,
                    Modified: info.LastWriteTime,
                    Url: UrlOf(path, info.LastWriteTimeUtc),
                    ThumbnailUrl: thumbInfo.Exists ? UrlOf(thumb, thumbInfo.LastWriteTimeUtc) : null));
            }
            return items.OrderByDescending(i => i.Modified).ThenByDescending(i => i.Name, StringComparer.Ordinal).ToList();
        }

        /// <summary>A URL relative to the page's base (so /Aliant1 keeps working): '/' separators, each segment
        /// escaped (names have spaces), and the file's time as a version, since nginx lets browsers cache images for
        /// an hour and a photo marked up again keeps its name.</summary>
        public static string UrlOf(string path, DateTime modifiedUtc)
        {
            var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Where(s => s != ".").Select(Uri.EscapeDataString);
            return string.Join("/", segments) + "?v=" + modifiedUtc.Ticks.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Writes the time a photo was taken (and where, when known) into its EXIF data, so any photo viewer
        /// sorts and places it correctly. Photos from the browser camera have none: a canvas keeps no metadata.
        /// ImageDescription is left alone - MediaComponent's tags live there.</summary>
        public static void WriteExif(string jpegPath, DateTime takenLocal, double? latitude, double? longitude)
        {
            ImageFile image;
            using (var input = File.OpenRead(jpegPath)) image = ImageFile.FromStream(input);
            image.Properties.Set(ExifTag.DateTimeOriginal, takenLocal);
            image.Properties.Set(ExifTag.DateTimeDigitized, takenLocal);
            image.Properties.Set(ExifTag.Software, "Antares");
            if (latitude is double lat && longitude is double lon && Math.Abs(lat) <= 90 && Math.Abs(lon) <= 180)
            {
                var (latD, latM, latS) = Sexagesimal(lat);
                var (lonD, lonM, lonS) = Sexagesimal(lon);
                image.Properties.Set(ExifTag.GPSLatitude, latD, latM, latS);
                image.Properties.Set(ExifTag.GPSLatitudeRef, lat >= 0 ? GPSLatitudeRef.North : GPSLatitudeRef.South);
                image.Properties.Set(ExifTag.GPSLongitude, lonD, lonM, lonS);
                image.Properties.Set(ExifTag.GPSLongitudeRef, lon >= 0 ? GPSLongitudeRef.East : GPSLongitudeRef.West);
            }
            var rewritten = jpegPath + ".exif";
            image.Save(rewritten);
            File.Move(rewritten, jpegPath, overwrite: true);
        }

        private static (float Degrees, float Minutes, float Seconds) Sexagesimal(double value)
        {
            value = Math.Abs(value);
            var degrees = Math.Floor(value);
            var minutes = Math.Floor((value - degrees) * 60);
            var seconds = (value - degrees - minutes / 60) * 3600;
            return ((float)degrees, (float)minutes, (float)Math.Round(seconds, 3));
        }
    }

    public enum MediaKind { Photo, Video, Other }

    /// <summary>The item has no folder for photos and videos (an unsupported type, or not saved yet).</summary>
    public sealed class MediaStorageException : Exception
    {
        public MediaStorageException() : base("This item cannot keep photos or videos.") { }
    }

    public sealed record MediaItem(string Path, string Name, MediaKind Kind, long Size, DateTime Modified, string Url, string? ThumbnailUrl);
}
