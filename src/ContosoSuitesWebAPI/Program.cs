using Azure.Identity;
using Microsoft.Azure.Cosmos;
using ContosoSuitesWebAPI.Agents;
using ContosoSuitesWebAPI.Entities;
using ContosoSuitesWebAPI.Plugins;
using ContosoSuitesWebAPI.Services;
using Microsoft.Data.SqlClient;
// using Azure.AI.OpenAI;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Azure;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Use dependency injection to inject services into the application.
builder.Services.AddSingleton<IVectorizationService, VectorizationService>();
builder.Services.AddSingleton<MaintenanceCopilot, MaintenanceCopilot>();

// // Add the Azure OpenAI text embedding generation service to the kernel builder.
// var kernelBuilder = Kernel.CreateBuilder();

// // Define the plugins to be used in the kernel.
// kernelBuilder.Plugins.AddFromType<MaintenanceRequestPlugin>("MaintenanceCopilot");

// // Add a singleton instance of CosmosClient for the MaintenanceRequestPlugin.
// kernelBuilder.Services.AddSingleton<CosmosClient>((_) =>
// {
//     string userAssignedClientId = builder.Configuration["AZURE_CLIENT_ID"]!;
//     var credential = new DefaultAzureCredential(
//         new DefaultAzureCredentialOptions
//         {
//             ManagedIdentityClientId = userAssignedClientId
//         });
//     CosmosClient client = new(
//         accountEndpoint: builder.Configuration["CosmosDB:AccountEndpoint"]!,
//         tokenCredential: credential
//     );
//     return client;
// });

// #pragma warning disable SKEXP0010
// kernelBuilder.AddAzureOpenAITextEmbeddingGeneration(
//     deploymentName: builder.Configuration["AzureOpenAI:EmbeddingDeploymentName"]!,
//     endpoint: builder.Configuration["AzureOpenAI:Endpoint"]!,
//     apiKey: builder.Configuration["AzureOpenAI:ApiKey"]!
// );
// #pragma warning restore SKEXP0010
// Kernel kernel = kernelBuilder.Build();

// // Register the Kernel as a singleton
// builder.Services.AddSingleton(kernel);

builder.Services.AddSingleton<Kernel>((_) =>
        {
            IKernelBuilder kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.AddAzureOpenAIChatCompletion(
                deploymentName: builder.Configuration["AzureOpenAI:DeploymentName"]!,
                endpoint: builder.Configuration["AzureOpenAI:Endpoint"]!,
                apiKey: builder.Configuration["AzureOpenAI:ApiKey"]!
            );
            var databaseService = _.GetRequiredService<IDatabaseService>();
            kernelBuilder.Plugins.AddFromObject(databaseService);

#pragma warning disable SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            kernelBuilder.AddAzureOpenAITextEmbeddingGeneration(
                deploymentName: builder.Configuration["AzureOpenAI:EmbeddingDeploymentName"]!,
                endpoint: builder.Configuration["AzureOpenAI:Endpoint"]!,
                apiKey: builder.Configuration["AzureOpenAI:ApiKey"]!
            );
#pragma warning restore SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

            kernelBuilder.Plugins.AddFromType<MaintenanceRequestPlugin>("MaintenanceCopilot");
            kernelBuilder.Services.AddSingleton<CosmosClient>((_) =>
                {
                    string userAssignedClientId = builder.Configuration["AZURE_CLIENT_ID"]!;
                    var credential = new DefaultAzureCredential(
                        new DefaultAzureCredentialOptions
                        {
                            ManagedIdentityClientId = userAssignedClientId
                        });
                    CosmosClient client = new(
                        accountEndpoint: builder.Configuration["CosmosDB:AccountEndpoint"]!,
                        tokenCredential: credential
                    );
                    return client;
                });
            return kernelBuilder.Build();
        });

// Create a single instance of the DatabaseService to be shared across the application.
builder.Services.AddSingleton<IDatabaseService, DatabaseService>((_) =>
{
    var connectionString = builder.Configuration.GetConnectionString("ContosoSuites");
    return new DatabaseService(connectionString!);
});


// Create a single instance of the CosmosClient to be shared across the application.
builder.Services.AddSingleton<CosmosClient>((_) =>
{

    string userAssignedClientId = builder.Configuration["AZURE_CLIENT_ID"]!;
    var credential = new DefaultAzureCredential(
        new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = userAssignedClientId
        });
    CosmosClient client = new(
        accountEndpoint: builder.Configuration["CosmosDB:AccountEndpoint"]!,
        tokenCredential: credential
    );
    return client;
});

// Create a single instance of the AzureOpenAIClient to be shared across the application.
// builder.Services.AddSingleton<AzureOpenAIClient>((_) =>
// {
//     var endpoint = new Uri(builder.Configuration["AzureOpenAI:Endpoint"]!);
//     var credentials = new AzureKeyCredential(builder.Configuration["AzureOpenAI:ApiKey"]!);

//     var client = new AzureOpenAIClient(endpoint, credentials);
//     return client;
// });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

/**** Endpoints ****/
// This endpoint serves as the default landing page for the API.
app.MapGet("/", async () =>
{
    return "Welcome to the Contoso Suites Web API!";
})
    .WithName("Index")
    .WithOpenApi();

// Retrieve the set of hotels from the database.
app.MapGet("/Hotels", async () =>
{
    // throw new NotImplementedException();
    var hotels = await app.Services.GetRequiredService<IDatabaseService>().GetHotels();
    return hotels;
})
    .WithName("GetHotels")
    .WithOpenApi();

// Retrieve the bookings for a specific hotel.
app.MapGet("/Hotels/{hotelId}/Bookings/", async (int hotelId) =>
{
    // throw new NotImplementedException();
    var bookings = await app.Services.GetRequiredService<IDatabaseService>().GetBookingsForHotel(hotelId);
    return bookings;
})
    .WithName("GetBookingsForHotel")
    .WithOpenApi();

// Retrieve the bookings for a specific hotel that are after a specified date.
app.MapGet("/Hotels/{hotelId}/Bookings/{min_date}", async (int hotelId, DateTime min_date) =>
{
    // throw new NotImplementedException();
    var bookings = await app.Services.GetRequiredService<IDatabaseService>().GetBookingsByHotelAndMinimumDate(hotelId, min_date);
    return bookings;
})
    .WithName("GetRecentBookingsForHotel")
    .WithOpenApi();

// This endpoint is used to send a message to the Azure OpenAI endpoint.
app.MapPost("/Chat", async Task<string> (HttpRequest request) =>
{
    var message = await Task.FromResult(request.Form["message"]);

    return "This endpoint is not yet available.";
})
    .WithName("Chat")
    .WithOpenApi();

// This endpoint is used to vectorize a text string.
// We will use this to generate embeddings for the maintenance request text.
app.MapGet("/Vectorize", async (string text, [FromServices] IVectorizationService vectorizationService) =>
{
    var embeddings = await vectorizationService.GetEmbeddings(text);
    return embeddings;
})
    .WithName("Vectorize")
    .WithOpenApi();

// This endpoint is used to search for maintenance requests based on a vectorized query.
app.MapPost("/VectorSearch", async ([FromBody] float[] queryVector, [FromServices] IVectorizationService vectorizationService, int max_results = 0, double minimum_similarity_score = 0.8) =>
{
    // Exercise 3 Task 3 TODO #3: Insert code to call the ExecuteVectorSearch function on the Vectorization Service. Don't forget to remove the NotImplementedException.
    // throw new NotImplementedException();
    var results = await vectorizationService.ExecuteVectorSearch(queryVector, max_results, minimum_similarity_score);
    return results;
})
    .WithName("VectorSearch")
    .WithOpenApi();

// This endpoint is used to send a message to the Maintenance Copilot.
app.MapPost("/MaintenanceCopilotChat", async ([FromBody] string message, [FromServices] MaintenanceCopilot copilot) =>
{
    // Exercise 5 Task 2 TODO #10: Insert code to call the Chat function on the MaintenanceCopilot. Don't forget to remove the NotImplementedException.
    // throw new NotImplementedException();
    var response = await copilot.Chat(message);
    return response;
})
    .WithName("Copilot")
    .WithOpenApi();

app.Run();
