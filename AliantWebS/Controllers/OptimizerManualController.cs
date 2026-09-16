using System;
using System.IO;
using AliantWebS.Manual;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AliantWebS.Controllers
{
    /// <summary>
    /// Hands out the optimiser's manual as a Word file.
    ///
    /// Built on demand rather than kept on disk: the manual ships inside the assembly, so the file a user
    /// downloads is always the one the page above it is showing, with no copy to keep in step.
    ///
    /// Off the Blazor circuit for the same reason as <see cref="BackupController"/> and
    /// <see cref="ScenarioFilesController"/>: the bytes go over a plain HTTP response instead of being
    /// base64-encoded into a script and pushed through SignalR.
    ///
    /// Reserved to the Developer role, like the page and the card that lead here.
    /// </summary>
    [Authorize(Roles = "Developer")]
    [Route("OptimizerManual")]
    public class OptimizerManualController : Controller
    {
        private const string DocxContentType =
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

        /// <summary>The URL the page links to. Spelled here, next to the route that answers it.</summary>
        public static string DownloadUrl => "OptimizerManual/Download";

        [HttpGet("Download")]
        public IActionResult Download()
        {
            var manual = ManualDocument.Load();
            var generatedOn = DateTime.Now;

            // Written into memory, not a temp file: the manual is a few hundred kilobytes, and a file on
            // disk would be one more thing to clean up after a download the user may abandon.
            var buffer = new MemoryStream();
            ManualWordWriter.Write(manual, buffer, generatedOn);
            buffer.Position = 0;

            return File(buffer, DocxContentType, ManualWordWriter.FileName(generatedOn));
        }
    }
}
