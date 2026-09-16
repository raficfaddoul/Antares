using System;
using System.IO;
using AliantWebS.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;

namespace AliantWebS.Controllers
{
    /// <summary>
    /// Streams a file out of a scenario's data or output folder.
    ///
    /// Kept off the Blazor circuit deliberately, for the same reason as
    /// <see cref="BackupController"/>: the previous implementation base64-encoded the whole file into a
    /// JavaScript string and eval'd it, which buffers the file twice in server memory, breaks on large
    /// files, and lets a quote in a file name escape into the script. Here the bytes go over a plain
    /// HTTP response and the file name never reaches a script.
    /// </summary>
    [Authorize]
    [Route("ScenarioFiles")]
    public class ScenarioFilesController : Controller
    {
        private static readonly FileExtensionContentTypeProvider ContentTypes = new();

        private readonly ScenarioStorageService _storage;

        public ScenarioFilesController(ScenarioStorageService storage)
        {
            _storage = storage;
        }

        /// <summary>
        /// Builds the relative URL the file manager links to. Kept here so the query-string shape is
        /// defined once, next to the code that parses it.
        /// </summary>
        public static string BuildDownloadUrl(Guid eventId, Guid scenarioId, string folder, string fileName) =>
            "ScenarioFiles/Download" +
            $"?eventId={Uri.EscapeDataString(eventId.ToString())}" +
            $"&scenarioId={Uri.EscapeDataString(scenarioId.ToString())}" +
            $"&folder={Uri.EscapeDataString(folder)}" +
            $"&file={Uri.EscapeDataString(fileName)}";

        [HttpGet("Download")]
        public IActionResult Download(
            [FromQuery] Guid eventId,
            [FromQuery] Guid scenarioId,
            [FromQuery] string? folder,
            [FromQuery] string? file)
        {
            if (eventId == Guid.Empty || scenarioId == Guid.Empty)
                return BadRequest("Scénario non identifié.");

            // Only the two folders the UI exposes. Anything else, including a relative segment, is a
            // caller trying paths we never meant to serve.
            var isData = string.Equals(folder, ScenarioStorageService.DataFolderName, StringComparison.OrdinalIgnoreCase);
            var isOutput = string.Equals(folder, ScenarioStorageService.OutputFolderName, StringComparison.OrdinalIgnoreCase);
            if (!isData && !isOutput)
                return BadRequest("Dossier invalide.");

            var safeName = ScenarioStorageService.SanitizeFileName(file);
            if (safeName is null)
                return BadRequest("Nom de fichier invalide.");

            // Ids only: looking the folder up must not create or rename anything just because someone
            // hit a stale link.
            var paths = _storage.FindScenarioPaths(eventId, scenarioId);
            if (paths is null) return NotFound();

            var folderPath = isData ? paths.Value.DataDir : paths.Value.OutputDir;
            var fullPath = Path.GetFullPath(Path.Combine(folderPath, safeName));

            // Belt and braces: the name is already sanitized, but the containment check is what makes
            // traversal impossible rather than merely unlikely.
            if (!ScenarioStorageService.IsInside(folderPath, fullPath) || !System.IO.File.Exists(fullPath))
                return NotFound();

            if (!ContentTypes.TryGetContentType(fullPath, out var contentType))
                contentType = "application/octet-stream";

            var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 81920,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);

            return File(stream, contentType, safeName);
        }
    }
}
