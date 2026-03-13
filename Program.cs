using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Services;

//Update with dotnet publish -c Release -o publish


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<DriveService>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<Program>>();

    try
    {
        var json = Environment.GetEnvironmentVariable("GOOGLE_SERVICE_ACCOUNT_JSON");

        if (string.IsNullOrEmpty(json))
            throw new Exception("Missing GOOGLE_SERVICE_ACCOUNT_JSON");

        var credential = GoogleCredential
            .FromJson(json)
            .CreateScoped(DriveService.Scope.Drive);

        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "DriveUploader"
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to initialize DriveService");
        throw;
    }
});

builder.Services.AddControllersWithViews();

var app = builder.Build();

var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
app.Urls.Add($"http://*:{port}");
// Use middleware
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

// Default route
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// ✅ Get the DriveService from DI and register watch channel
using (var scope = app.Services.CreateScope())
{
    var driveService = scope.ServiceProvider.GetRequiredService<DriveService>();

    // 1️⃣ Get start page token
    // Example using Google Drive API
    var tokenRequest = driveService.Changes.GetStartPageToken().Execute();
    // Use the property that holds the actual string token
    string tokenValue = tokenRequest.StartPageTokenValue;
    // Store 'savedStartPageToken' in your database or configuration
    // to use in future synchronization requests.

    // 2️⃣ Create watch request
    var channel = new Google.Apis.Drive.v3.Data.Channel
    {
        Id = Guid.NewGuid().ToString(),
        Type = "web_hook",
        Address = "https://drive-webhooks.livelypond-68f97ce4.westus2.azurecontainerapps.io/drive/webhook"
    };

    var watchRequest = driveService.Changes.Watch(channel, tokenValue);

    // 3️⃣ Execute
    await watchRequest.ExecuteAsync();

    Console.WriteLine("Watch channel registered!");
}
app.Run();