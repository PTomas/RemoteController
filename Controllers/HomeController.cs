using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Diagnostics;

[ApiController]
[Route("drive")]
public class DriveWebhookController : Controller
{
    private readonly DriveService _driveService;
    private static string _savedPageToken;
    private const string FolderId = "1X5JgNmHmWOQm7XwKhqDarLgtF34Ov0n0";
    private static long _lastMessageNumber = 0;
    private readonly IWebHostEnvironment _env;

    public DriveWebhookController(DriveService driveService, IWebHostEnvironment env)
    {
        _driveService = driveService;
        _env = env;
    }

    [HttpGet("/")]
    public IActionResult Home()
    {
        return Ok("Server is running");
    }

    public IActionResult Index()
    {
        return Content("Server running");
    }



    [HttpGet("webhook")]
    public IActionResult WebhookInfo()
    {
        return Content("Google Drive webhook endpoint.");
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        if (!Request.Headers.ContainsKey("X-Goog-Resource-State"))
        return Ok();

        foreach (var header in Request.Headers)
        {
            Console.WriteLine($"{header.Key}: {header.Value}");
        }
        
        try
        {

            var messageNumber = Request.Headers["X-Goog-Message-Number"].ToString();

            Console.WriteLine($"MessageNumber: {messageNumber}");

            if (long.TryParse(messageNumber, out long msgNum) && msgNum <= _lastMessageNumber)
            {
                Console.WriteLine("Duplicate message received, ignoring.");
                return Ok();
            }

            _lastMessageNumber = msgNum;
            
            _ = Task.Run(CheckDriveChanges);
            return Ok();
 
        }
        catch (Exception ex)
        {
            Console.WriteLine("==== ERROR ====");
            Console.WriteLine(ex.ToString());
            Console.WriteLine("===============");
            return Ok();//Big O little k to the Services!
        }
              
    }
    
    private async Task CheckDriveChanges()
    {
        var request = _driveService.Changes.List(_savedPageToken);
        request.Fields = "newStartPageToken,changes(fileId,file(name,mimeType))";

        var response = await request.ExecuteAsync();

        foreach (var change in response.Changes)
        {
            if (change.File != null)
            {
                Console.WriteLine($"Changed file: {change.File.Name}");
            }
        }

        // Save the next token
        if (response.NewStartPageToken != null)
            _savedPageToken = response.NewStartPageToken;
    }
        

    [HttpGet]//Upload
    public async Task<IActionResult> Upload()
    {
        var fileMetadata = new Google.Apis.Drive.v3.Data.File()
        {
            Name = "Test.txt",
            Parents = new List<string> { FolderId }
        };

        var bytes = System.Text.Encoding.UTF8.GetBytes("Hello from ASP.NET!");
        var streamContent = new MemoryStream(bytes);

        var request = _driveService.Files.Create(fileMetadata, streamContent, "text/plain");
        request.Fields = "id";

        await request.UploadAsync();

        var file = request.ResponseBody;

        return Content($"Upload success! File ID: {file.Id}");
    }

    [HttpGet]//DownloadLatest
    private async Task DownloadLatestInternal()
    {
        var request = _driveService.Files.List();
        request.Q = $"'{FolderId}' in parents";
        request.PageSize = 1;
        request.Fields = "files(id,name,mimeType,modifiedTime)";
        request.OrderBy = "modifiedTime desc";
        request.SupportsAllDrives = true;
        request.IncludeItemsFromAllDrives = true;

        var result = await request.ExecuteAsync();

        if (result.Files.Count == 0)
        {
            Console.WriteLine("No files found");
            return;
        }

        var file = result.Files[0];
        string folderPath = Path.Combine(_env.ContentRootPath, "downloads");
        Directory.CreateDirectory(folderPath);

        string fileName = file.Name;
        if (file.MimeType.StartsWith("application/vnd.google-apps"))
            fileName += ".txt";

        string fullPath = Path.Combine(folderPath, fileName);

        using var stream = new FileStream(fullPath, FileMode.Create);

        if (file.MimeType.StartsWith("application/vnd.google-apps"))
            await _driveService.Files.Export(file.Id, "text/plain").DownloadAsync(stream);
        else
            await _driveService.Files.Get(file.Id).DownloadAsync(stream);
            Console.WriteLine($"Downloaded file: {fullPath}");
        // await RunScript(fullPath);
        
    }

    public async Task RunScript(string filePath)
    {
        if (!System.IO.File.Exists(filePath))
        {
            Console.WriteLine($"File not found: {filePath}");
            return;
        }

        string contents = await System.IO.File.ReadAllTextAsync(filePath);

        Console.WriteLine("---- File Contents ----");
        Console.WriteLine(contents);
        Console.WriteLine("-----------------------");
    }

    // public async void RunScript(string folderPath)
    // {
    //     var scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "scripts/runContents.sh");

    //     var process = new Process();
    //     process.StartInfo.FileName = @"C:\Program Files\Git\bin\bash.exe";
    //     process.StartInfo.Arguments = $"{scriptPath} \"{folderPath}\"";
    //     process.StartInfo.RedirectStandardOutput = true;
    //     process.StartInfo.RedirectStandardError = true;
    //     process.StartInfo.UseShellExecute = false;
    //     process.StartInfo.CreateNoWindow = true;

    //     process.Start();

    //     string output = await process.StandardOutput.ReadToEndAsync();
    //     string error = await process.StandardError.ReadToEndAsync();

    //     process.WaitForExit();

    //     Console.WriteLine(output);
    //     Console.WriteLine(error);
    // }
}

