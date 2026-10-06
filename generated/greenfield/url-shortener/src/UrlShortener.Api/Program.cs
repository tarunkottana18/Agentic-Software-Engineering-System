using Microsoft.EntityFrameworkCore;
using UrlShortener.Application;
using UrlShortener.Application.Interfaces;
using UrlShortener.Core.Interfaces;
using UrlShortener.Infrastructure.Persistence;
using UrlShortener.Infrastructure.Repositories;
using UrlShortener.Infrastructure.ShortCodes;

var builder = WebApplication.CreateBuilder(args);
var publicBaseUrl = builder.Configuration["UrlShortener:PublicBaseUrl"]
	?? throw new InvalidOperationException("UrlShortener:PublicBaseUrl must be configured.");
if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var publicUri) ||
	(publicUri.Scheme != Uri.UriSchemeHttp && publicUri.Scheme != Uri.UriSchemeHttps))
{
	throw new InvalidOperationException("UrlShortener:PublicBaseUrl must be an absolute HTTP or HTTPS URL.");
}

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
	?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured.");

builder.Services.AddDbContext<LinkDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddScoped<ILinkRepository, LinkRepository>();
builder.Services.AddSingleton<IShortCodeGenerator, CryptographicShortCodeGenerator>();
builder.Services.AddApplication();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
	app.UseSwagger();
	app.UseSwaggerUI();
}

app.MapControllers();

app.Run();

public partial class Program;
