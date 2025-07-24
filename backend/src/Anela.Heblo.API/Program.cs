using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using Microsoft.Identity.Abstractions;
using Microsoft.Identity.Web.Resource;
using Anela.Heblo.API.Authentication;

namespace Anela.Heblo.API;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        if (builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("UseMockAuth"))
        {
            // Mock authentication for development
            builder.Services.AddAuthentication("Mock")
                .AddScheme<MockAuthenticationSchemeOptions, MockAuthenticationHandler>("Mock", _ => { });
            builder.Services.AddAuthorization();
        }
        else
        {
            // Real Microsoft Identity authentication
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"))
                .EnableTokenAcquisitionToCallDownstreamApi()
                .AddMicrosoftGraph(builder.Configuration.GetSection("DownstreamApi"))
                .AddInMemoryTokenCaches();
        }

        builder.Services.AddControllers();
        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddOpenApiDocument();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
            app.UseOpenApi();
        }

        app.UseHttpsRedirection();

        app.UseAuthorization();


        app.MapControllers();

        app.Run();
    }
}