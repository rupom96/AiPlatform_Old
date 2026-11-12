using Asp.Versioning.ApiExplorer;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace AiPlatform.Api;

// প্রতিটি API version-এর জন্য একটা করে Swagger doc বানায়।
// Phase 1 এ এখানেই JWT security definition যোগ হবে।
public class ConfigureSwaggerOptions(IApiVersionDescriptionProvider provider)
    : IConfigureOptions<SwaggerGenOptions>
{
    public void Configure(SwaggerGenOptions options)
    {
        foreach (var desc in provider.ApiVersionDescriptions)
        {
            options.SwaggerDoc(desc.GroupName, new OpenApiInfo
            {
                Title = "AiPlatform API",
                Version = desc.ApiVersion.ToString(),
                Description = desc.IsDeprecated ? "এই version deprecated" : "Production-grade AI platform API"
            });
        }

        // "Authorize" button — JWT access token paste করে protected endpoint call করা যায়
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "শুধু access token paste করো (Swagger নিজে 'Bearer ' যোগ করবে)।"
        });
        options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
        {
            { new OpenApiSecuritySchemeReference("Bearer", document), new List<string>() }
        });
    }
}
