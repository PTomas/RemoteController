using System.Buffers.Text;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using System.Text;

//Update with dotnet publish -c Release -o publish


Console.WriteLine("STEP 1: Builder created");

var builder = WebApplication.CreateBuilder(args);

Console.WriteLine("STEP 2: Before DriveService registration");

Console.WriteLine($"ENV: {builder.Environment.EnvironmentName}");
Console.WriteLine("ENV VAR EXISTS: " + (Environment.GetEnvironmentVariable("GoogleServiceAccountB64") != null));

builder.Services.AddSingleton<DriveService>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<Program>>();
    var config = sp.GetRequiredService<IConfiguration>();

    try
    {
        Console.WriteLine("Initializing DriveService...");

        var base64 =
            Environment.GetEnvironmentVariable("GoogleServiceAccountB64")
            ?? config["GoogleServiceAccountB64"];

        Console.WriteLine("Base64 exists: " + (base64 != null));

        if (string.IsNullOrEmpty(base64))
            throw new Exception("Missing GOOGLE_SERVICE_ACCOUNT_B64");


        Console.WriteLine("STEP 3: Inside DriveService factory");

        Console.WriteLine("Base64 length: " + base64?.Length);

        string json;

        try
        {
            json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch
        {
            Console.WriteLine("⚠️ Base64 invalid, assuming raw JSON");
            json = base64; // fallback
        }

        var credential = GoogleCredential
            .FromJson(json)
            .CreateScoped(DriveService.Scope.DriveFile);

        Console.WriteLine("DriveService initialized");

        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "DriveUploader"
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine("❌ DriveService FAILED:");
        Console.WriteLine(ex.ToString());

        throw; // DO NOT return null
    }
});


builder.Services.AddControllersWithViews();

var app = builder.Build();
Console.WriteLine("STEP 4: App built");

// var port =//not for azure Web Apps I guess.
//     Environment.GetEnvironmentVariable("PORT")
//     ?? Environment.GetEnvironmentVariable("WEBSITES_PORT")
//     ?? "8080";

// Console.WriteLine($"Listening on port: {port}");

// app.Urls.Add($"http://*:{port}");

// Use middleware
// app.UseHttpsRedirection();//Disable for testing on Azure!!!
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

// Default route
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// ✅ Get the DriveService from DI and register watch channel
app.Lifetime.ApplicationStarted.Register(() =>
{
    Task.Run(async () =>
    {
        try
        {
            Console.WriteLine("Starting Drive watcher setup...");

            using var scope = app.Services.CreateScope();
            var driveService = scope.ServiceProvider.GetRequiredService<DriveService>();

            var tokenRequest = await driveService.Changes.GetStartPageToken().ExecuteAsync();
            string tokenValue = tokenRequest.StartPageTokenValue;

            var channel = new Google.Apis.Drive.v3.Data.Channel
            {
                Id = Guid.NewGuid().ToString(),
                Type = "web_hook",
                Address = "https://drivewebhookservice-cmckcnexbaadadd9.canadacentral-01.azurewebsites.net/drive/webhook"
            };

            var watchRequest = driveService.Changes.Watch(channel, tokenValue);
            await watchRequest.ExecuteAsync();

            Console.WriteLine("✅ Watch channel registered!");
        }
        catch (Exception ex)
        {
            Console.WriteLine("❌ Watch setup failed:");
            Console.WriteLine(ex.ToString());
        }
    });
});

Console.WriteLine("STEP 5: Before app.Run()");
app.Run();