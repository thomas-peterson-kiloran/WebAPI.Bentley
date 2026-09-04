using Microsoft.EntityFrameworkCore;
using WebAPI.Bentley.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Authorization
builder.Services.AddAuthorization();
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, WebAPI.Bentley.Authorization.FormDataAuthorizationHandler>();

// Entity Framework Core - InMemory

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseInMemoryDatabase("FormDataDb")
);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
