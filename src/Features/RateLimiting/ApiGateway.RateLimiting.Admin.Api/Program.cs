using ApiGateway.RateLimiting.Admin.Api.Endpoints;
using ApiGateway.RateLimiting.Core.Interfaces;
using ApiGateway.RateLimiting.Persistence;
using ApiGateway.RateLimiting.Persistence.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add CORS
const string myAllowSpecificOrigins = "_myAllowSpecificOrigins";
builder.Services.AddCors(options =>
{
    options.AddPolicy(name: myAllowSpecificOrigins,
        policy =>
        {
            policy.WithOrigins("https://localhost:7232")
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
});


var sqlConnectionString = builder.Configuration.GetConnectionString("Sql");

if (string.IsNullOrWhiteSpace(sqlConnectionString))
    throw new InvalidOperationException("Connection string 'Sql' is missing.");

// Add Services
builder.Services.AddDbContext<RateLimitingDbContext>(options =>
{
    options.UseSqlServer(sqlConnectionString);
});
builder.Services.AddScoped<IRateLimitPolicyService, RateLimitPolicyService>();

var app = builder.Build();



app.UseHttpsRedirection();

// Routes
app.MapRateLimitEndpoints();


app.UseCors(myAllowSpecificOrigins);

app.Run();