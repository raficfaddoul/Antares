using System;
using System.IO;
using AliantWebS.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace AliantWebS.Controllers
{
    [Authorize(Roles = "Administrator")]
    [Route("Backup")]
    public class BackupController : Controller
    {
        private readonly IConfiguration _configuration;

        public BackupController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // Streams a .bak that BackupDatabase already wrote into the shared backup
        // folder, then deletes it. Kept off the Blazor circuit on purpose: the file
        // transfer goes over a plain HTTP response, not SignalR — so no base64 and no
        // buffering the whole backup in server memory regardless of DB size.
        [HttpGet("Download")]
        public IActionResult Download([FromQuery] string file)
        {
            string backupPath = _configuration["BackupPath"];
            if (string.IsNullOrEmpty(backupPath))
                backupPath = Path.GetTempPath();

            // Accept only a bare .bak filename that resolves inside backupPath — guards
            // against path traversal via the query string.
            string safeName = Path.GetFileName(file ?? string.Empty);
            if (string.IsNullOrEmpty(safeName) ||
                !safeName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Invalid backup file name.");

            string root = Path.GetFullPath(backupPath);
            string rootWithSep = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(Path.Combine(root, safeName));

            if (!fullPath.StartsWith(rootWithSep, StringComparison.Ordinal) ||
                !System.IO.File.Exists(fullPath))
                return NotFound();

            // DeleteOnClose: the OS removes the temp .bak once the response stream is
            // disposed (i.e. after the download finishes or the client aborts).
            var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);

            return File(stream, "application/octet-stream", "AliantWeb.bak");
        }
    }
}
