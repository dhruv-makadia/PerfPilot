using System.Text.Json;
using PerfPilot.Core.Common;
using PerfPilot.Infrastructure.OpenApi;

namespace PerfPilot.Tests;

public class OpenApiSpecParserTests
{
    private readonly OpenApiSpecParser _parser = new();

    [Fact]
    public void Parses_openapi3_document_metadata()
    {
        var result = _parser.Parse(TestFiles.ReadFixture("petstore-openapi3.json"));

        Assert.Equal("Swagger Petstore - OpenAPI 3.0", result.Title);
        Assert.Equal("1.0.27", result.ApiVersion);
        Assert.Equal("OpenAPI 3.0.4", result.SpecFormat);
        Assert.Equal(6, result.Endpoints.Count);
    }

    [Fact]
    public void Maps_methods_paths_tags_and_defaults_missing_tag()
    {
        var endpoints = _parser.Parse(TestFiles.ReadFixture("petstore-openapi3.json")).Endpoints;

        Assert.Equal(
            ["POST /pet", "GET /pet/findByStatus", "GET /pet/{petId}", "DELETE /pet/{petId}", "GET /store/inventory", "POST /categories"],
            endpoints.Select(e => $"{e.Method} {e.Path}"));
        Assert.Equal("pet", endpoints[0].Tag);
        Assert.Equal("default", endpoints.Single(e => e.Path == "/store/inventory").Tag);
        Assert.True(endpoints.Single(e => e.Method == "DELETE").Deprecated);
        Assert.Equal("Find pet by ID.", endpoints[2].Summary);
        Assert.Equal("getPetById", endpoints[2].OperationId);
        Assert.Equal(["200", "404"], endpoints[2].ResponseCodes);
    }

    [Fact]
    public void Merges_path_level_and_operation_parameters()
    {
        var getPet = _parser.Parse(TestFiles.ReadFixture("petstore-openapi3.json")).Endpoints
            .Single(e => e.Method == "GET" && e.Path == "/pet/{petId}");

        Assert.Collection(getPet.Parameters,
            p =>
            {
                Assert.Equal("petId", p.Name);
                Assert.Equal("path", p.In);
                Assert.True(p.Required);
                Assert.Equal("integer", p.Type);
                Assert.Equal("int64", p.Format);
                Assert.Equal("ID of pet", p.Description);
            },
            p =>
            {
                Assert.Equal("api_key", p.Name);
                Assert.Equal("header", p.In);
                Assert.False(p.Required);
            });
    }

    [Fact]
    public void Maps_query_parameter_enum_and_default()
    {
        var find = _parser.Parse(TestFiles.ReadFixture("petstore-openapi3.json")).Endpoints.Single(e => e.Path == "/pet/findByStatus");
        var status = Assert.Single(find.Parameters);

        Assert.Equal("query", status.In);
        Assert.Equal("available", status.Example);
        Assert.Equal(["available", "pending", "sold"], status.Enum);
    }

    [Fact]
    public void Prefers_json_request_body_and_generates_sample_from_examples()
    {
        var addPet = _parser.Parse(TestFiles.ReadFixture("petstore-openapi3.json")).Endpoints.Single(e => e.Path == "/pet");

        Assert.Equal("application/json", addPet.RequestBodyContentType);
        using var sample = JsonDocument.Parse(addPet.SampleRequestBody!);
        var root = sample.RootElement;
        Assert.Equal(10, root.GetProperty("id").GetInt64());
        Assert.Equal("doggie", root.GetProperty("name").GetString());
        Assert.Equal("Dogs", root.GetProperty("category").GetProperty("name").GetString());
        Assert.Equal("string", root.GetProperty("photoUrls")[0].GetString());
        Assert.Equal("available", root.GetProperty("status").GetString());
        Assert.False(root.TryGetProperty("createdAt", out _), "readOnly properties are not part of request samples");

        using var schema = JsonDocument.Parse(addPet.RequestBodySchema!);
        Assert.Equal("Pet", schema.RootElement.GetProperty("$ref").GetString());
        Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
        Assert.Equal(2, schema.RootElement.GetProperty("required").GetArrayLength());
    }

    [Fact]
    public void Handles_recursive_schemas_without_overflow()
    {
        var create = _parser.Parse(TestFiles.ReadFixture("petstore-openapi3.json")).Endpoints.Single(e => e.Path == "/categories");

        using var sample = JsonDocument.Parse(create.SampleRequestBody!);
        Assert.Equal("user@example.com", sample.RootElement.GetProperty("name").GetString());
        Assert.Contains("\"recursive\": true", create.RequestBodySchema);
    }

    [Fact]
    public void Parses_swagger2_yaml_including_body_and_header_parameters()
    {
        var result = _parser.Parse(TestFiles.ReadFixture("orders-swagger2.yaml"));

        Assert.Equal("Orders API", result.Title);
        Assert.Equal("Swagger 2.0", result.SpecFormat);
        Assert.Equal(3, result.Endpoints.Count);

        var list = result.Endpoints.Single(e => e.Method == "GET" && e.Path == "/orders");
        Assert.Equal(["page", "X-Tenant"], list.Parameters.Select(p => p.Name));
        Assert.Equal("1", list.Parameters[0].Example);
        Assert.True(list.Parameters[1].Required);

        var create = result.Endpoints.Single(e => e.Method == "POST");
        Assert.Equal("application/json", create.RequestBodyContentType);
        using var sample = JsonDocument.Parse(create.SampleRequestBody!);
        Assert.Equal("user@example.com", sample.RootElement.GetProperty("customerEmail").GetString());
        Assert.Equal(1, sample.RootElement.GetProperty("quantity").GetInt32());
        Assert.True(sample.RootElement.GetProperty("express").GetBoolean());

        var get = result.Endpoints.Single(e => e.Path == "/orders/{orderId}");
        Assert.Equal("uuid", get.Parameters.Single().Format);
    }

    [Fact]
    public void Reads_openapi31_documents_with_a_warning()
    {
        var json = TestFiles.ReadFixture("petstore-openapi3.json").Replace("\"openapi\": \"3.0.4\"", "\"openapi\": \"3.1.0\"");

        var result = _parser.Parse(json);

        Assert.Equal("OpenAPI 3.1.0", result.SpecFormat);
        Assert.Equal(6, result.Endpoints.Count);
        Assert.Contains(result.Warnings, w => w.Contains("3.1.0"));
    }

    [Theory]
    [InlineData("{\"hello\": \"world\"}")]
    [InlineData("not: an api")]
    public void Rejects_documents_that_are_not_openapi(string content)
    {
        var ex = Assert.Throws<ValidationException>(() => _parser.Parse(content));
        Assert.Contains("not an OpenAPI", ex.Message);
    }

    [Fact]
    public void Rejects_documents_without_paths()
    {
        Assert.Throws<ValidationException>(() => _parser.Parse("{\"openapi\":\"3.0.1\",\"info\":{\"title\":\"x\",\"version\":\"1\"},\"paths\":{}}"));
    }
}
