var builder = WebApplication.CreateBuilder(args);
builder.AddPlatform();
// builder.AddModule<MovieNightModule>();   // этап 1+
var app = builder.Build();
app.MapPlatform();
app.Run();

public partial class Program;   // для WebApplicationFactory
