using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using UrlShortener.Application;
using UrlShortener.Agents;
using UrlShortener.Infrastructure;
using UrlShortener.Infrastructure.Persistence;
using UrlShortener.Application.Common;
using UrlShortener.Application.Workflows;
using UrlShortener.Api.Security;

var builder = WebApplication.CreateBuilder(args);

// 1. Add Framework Services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("ApprovalToken", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "X-Approval-Token",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Approval token required by approval and rollback endpoints."
    });
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "ApprovalToken"
                }
            },
            Array.Empty<string>()
        }
    });
});

// 2. Add Custom Layer Dependencies
builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAgents(builder.Configuration);
builder.Services.AddSingleton<IApprovalTokenValidator, ApprovalTokenValidator>();

var app = builder.Build();

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, title, detail) = exception switch
    {
        WorkflowNotFoundException workflowException => (StatusCodes.Status404NotFound, "Workflow not found", workflowException.Message),
        WorkflowConflictException workflowException => (StatusCodes.Status409Conflict, "Workflow conflict", workflowException.Message),
        WorkflowValidationException workflowException => (StatusCodes.Status400BadRequest, "Invalid workflow request", workflowException.Message),
        InvalidUrlException urlException => (StatusCodes.Status400BadRequest, "Invalid URL", urlException.Message),
        ShortCodeGenerationException codeException => (StatusCodes.Status503ServiceUnavailable, "Short link unavailable", codeException.Message),
        _ => (StatusCodes.Status500InternalServerError, "Unexpected server error", "The request could not be completed.")
    };

    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(new ProblemDetails
    {
        Status = status,
        Title = title,
        Detail = detail,
        Extensions = { ["traceId"] = context.TraceIdentifier }
    });
}));

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<UrlDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
    await dbContext.Database.ExecuteSqlRawAsync(
        "CREATE TABLE IF NOT EXISTS EngineeringWorkflowRuns (" +
        "Id TEXT NOT NULL PRIMARY KEY, Requirement TEXT NOT NULL, Status TEXT NOT NULL, " +
        "PlanVersion INTEGER NOT NULL, Revision INTEGER NOT NULL, StateJson TEXT NOT NULL, " +
        "CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL)");
    await dbContext.Database.ExecuteSqlRawAsync(
        "CREATE INDEX IF NOT EXISTS IX_EngineeringWorkflowRuns_Status ON EngineeringWorkflowRuns (Status)");
}
else
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<UrlDbContext>();
    await dbContext.Database.MigrateAsync();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapGet("/", () => Results.Redirect("/swagger"));
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;
